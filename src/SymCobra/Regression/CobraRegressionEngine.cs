// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymCobra.Core;
using SymCobra.Runtime;

namespace SymCobra.Regression;

public sealed class CobraRegressionEngine
{
    public CobraRegressionResult SolveTabular(CobraRegressionOptions options, CancellationToken ct = default)
    {
        var dataset = CsvTabularDatasetLoader.Load(options.DatasetPath, options.TargetColumn, options.FeatureColumns);
        var featureIndexes = CreateFeatureIndexMap(dataset.FeatureNames);
        var candidateList = GenerateCandidates(dataset.FeatureNames, options.MaxCandidates).ToList();
        var candidateScores = ScoreCandidates(candidateList, dataset, featureIndexes, options.ComplexityPenalty, options.LossFunction, ct);

        IExpression? bestExpression = null;
        double bestScore = double.PositiveInfinity;
        for (int i = 0; i < candidateList.Count; i++)
        {
            if (candidateScores[i] < bestScore)
            {
                bestScore = candidateScores[i];
                bestExpression = candidateList[i];
            }
        }

        if (bestExpression is null)
        {
            throw new InvalidOperationException("Regression candidate generation produced no candidates.");
        }

        return new CobraRegressionResult(
            bestExpression,
            bestScore,
            candidateList.Count,
            $"Best regression candidate after evaluating {candidateList.Count} graph-deduplicated candidates.");
    }

    private static IEnumerable<IExpression> GenerateCandidates(IReadOnlyList<string> featureNames, int maxCandidates)
    {
        var candidates = new List<IExpression>();
        var symbols = featureNames.Select(name => (IExpression)new Symbol(name)).ToList();
        var constants = new IExpression[]
        {
            new Number(0),
            new Number(1),
            new Number(-1),
            new Number(2)
        };

        candidates.AddRange(symbols);
        candidates.AddRange(constants);

        foreach (var symbol in symbols)
        {
            candidates.Add(new Multiply(symbol, symbol));
            candidates.Add(new Add(symbol, new Number(1)));
            candidates.Add(new Subtract(symbol, new Number(1)));
            candidates.Add(new Multiply(new Number(2), symbol));
        }

        for (int i = 0; i < symbols.Count; i++)
        {
            for (int j = i + 1; j < symbols.Count; j++)
            {
                candidates.Add(new Add(symbols[i], symbols[j]));
                candidates.Add(new Subtract(symbols[i], symbols[j]));
                candidates.Add(new Multiply(symbols[i], symbols[j]));
            }
        }

        return DeduplicateCandidatesWithGraph(candidates, Math.Max(8, maxCandidates));
    }

    private static IReadOnlyList<IExpression> DeduplicateCandidatesWithGraph(
        IEnumerable<IExpression> candidates,
        int maxCandidates)
    {
        var graphState = new CobraGraphState();
        var canonicalByClass = new Dictionary<int, IExpression>();

        foreach (var candidate in candidates)
        {
            var canonical = candidate.Canonicalize();
            int classId = graphState.AddExpression(canonical);
            classId = graphState.Find(classId);
            if (!canonicalByClass.ContainsKey(classId))
            {
                canonicalByClass[classId] = canonical;
            }
        }

        return canonicalByClass
            .OrderBy(static entry => entry.Key)
            .Select(static entry => entry.Value)
            .Take(maxCandidates)
            .ToArray();
    }

    private static double Score(
        IExpression expression,
        TabularDataset dataset,
        IReadOnlyDictionary<string, int> featureIndexes,
        double complexityPenalty,
        string lossFunction)
    {
        double error = 0;
        for (int i = 0; i < dataset.Rows.Count; i++)
        {
            double predicted = Evaluate(expression, featureIndexes, dataset.Rows[i]);
            double delta = predicted - dataset.Targets[i];
            error += lossFunction.Equals("MAE", StringComparison.OrdinalIgnoreCase) ? Math.Abs(delta) : delta * delta;
        }

        error /= Math.Max(1, dataset.Rows.Count);
        return error + (complexityPenalty * EstimateComplexity(expression));
    }

    private static double[] ScoreCandidates(
        IReadOnlyList<IExpression> candidates,
        TabularDataset dataset,
        IReadOnlyDictionary<string, int> featureIndexes,
        double complexityPenalty,
        string lossFunction,
        CancellationToken ct)
    {
        int sampleCount = dataset.Rows.Count;
        int featureCount = dataset.FeatureNames.Count;

        // Transpose to feature-major for better cache locality and easier vectorization
        var featureMajor = new double[featureCount][];
        for (int i = 0; i < featureCount; i++)
        {
            featureMajor[i] = new double[sampleCount];
            for (int j = 0; j < sampleCount; j++)
            {
                featureMajor[i][j] = dataset.Rows[j][i];
            }
        }

        var predictionMatrix = new double[candidates.Count * sampleCount];
        var targets = dataset.Targets.ToArray();

        Parallel.For(0, candidates.Count, new ParallelOptions { CancellationToken = ct }, candidateIndex =>
        {
            var compiled = CompileExpressionFeatureMajor(candidates[candidateIndex], featureIndexes);
            int baseOffset = candidateIndex * sampleCount;
            compiled(featureMajor, predictionMatrix, baseOffset, sampleCount);
        });

        double[] mseScores = Array.Empty<double>();
        bool usedCuda = lossFunction.Equals("MSE", StringComparison.OrdinalIgnoreCase) &&
                        CobraCudaNative.TryBatchMseScores(predictionMatrix, targets, sampleCount, candidates.Count, out mseScores);

        if (!usedCuda)
        {
            mseScores = new double[candidates.Count];
            Parallel.For(0, candidates.Count, new ParallelOptions { CancellationToken = ct }, candidateIndex =>
            {
                int baseOffset = candidateIndex * sampleCount;
                double sum = 0.0;
                for (int rowIndex = 0; rowIndex < sampleCount; rowIndex++)
                {
                    double delta = predictionMatrix[baseOffset + rowIndex] - targets[rowIndex];
                    sum += lossFunction.Equals("MAE", StringComparison.OrdinalIgnoreCase) ? Math.Abs(delta) : delta * delta;
                }

                mseScores[candidateIndex] = sum / Math.Max(1, sampleCount);
            });
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            mseScores[i] += complexityPenalty * EstimateComplexity(candidates[i]);
        }

        return mseScores;
    }

    private static Action<double[][], double[], int, int> CompileExpressionFeatureMajor(IExpression expression, IReadOnlyDictionary<string, int> featureIndexes)
    {
        var featuresParam = System.Linq.Expressions.Expression.Parameter(typeof(double[][]), "features");
        var resultsParam = System.Linq.Expressions.Expression.Parameter(typeof(double[]), "results");
        var offsetParam = System.Linq.Expressions.Expression.Parameter(typeof(int), "offset");
        var countParam = System.Linq.Expressions.Expression.Parameter(typeof(int), "count");

        var indexVar = System.Linq.Expressions.Expression.Variable(typeof(int), "i");
        var breakLabel = System.Linq.Expressions.Expression.Label("break");

        var loop = System.Linq.Expressions.Expression.Block(
            new[] { indexVar },
            System.Linq.Expressions.Expression.Assign(indexVar, System.Linq.Expressions.Expression.Constant(0)),
            System.Linq.Expressions.Expression.Loop(
                System.Linq.Expressions.Expression.IfThenElse(
                    System.Linq.Expressions.Expression.LessThan(indexVar, countParam),
                    System.Linq.Expressions.Expression.Block(
                        System.Linq.Expressions.Expression.Assign(
                            System.Linq.Expressions.Expression.ArrayAccess(resultsParam, System.Linq.Expressions.Expression.Add(offsetParam, indexVar)),
                            BuildExpressionFeatureMajor(expression, featureIndexes, featuresParam, indexVar)
                        ),
                        System.Linq.Expressions.Expression.PostIncrementAssign(indexVar)
                    ),
                    System.Linq.Expressions.Expression.Break(breakLabel)
                ),
                breakLabel
            )
        );

        return System.Linq.Expressions.Expression.Lambda<Action<double[][], double[], int, int>>(loop, featuresParam, resultsParam, offsetParam, countParam).Compile();
    }

    private static System.Linq.Expressions.Expression BuildExpressionFeatureMajor(
        IExpression expression,
        IReadOnlyDictionary<string, int> featureIndexes,
        System.Linq.Expressions.ParameterExpression featuresParam,
        System.Linq.Expressions.ParameterExpression indexVar)
    {
        return expression switch
        {
            Number n => System.Linq.Expressions.Expression.Constant((double)n.Value),
            Symbol s => BuildSymbolExpressionFeatureMajor(s.Name, featureIndexes, featuresParam, indexVar),
            Add add => add.Arguments.Count == 0 ? System.Linq.Expressions.Expression.Constant(0.0) :
                       add.Arguments.Select(a => BuildExpressionFeatureMajor(a, featureIndexes, featuresParam, indexVar)).Aggregate(System.Linq.Expressions.Expression.Add),
            Subtract sub => System.Linq.Expressions.Expression.Subtract(BuildExpressionFeatureMajor(sub.LeftOperand, featureIndexes, featuresParam, indexVar), BuildExpressionFeatureMajor(sub.RightOperand, featureIndexes, featuresParam, indexVar)),
            Multiply mul => mul.Arguments.Count == 0 ? System.Linq.Expressions.Expression.Constant(1.0) :
                            mul.Arguments.Select(a => BuildExpressionFeatureMajor(a, featureIndexes, featuresParam, indexVar)).Aggregate(System.Linq.Expressions.Expression.Multiply),
            Divide div => System.Linq.Expressions.Expression.Call(typeof(CobraRegressionEngine).GetMethod(nameof(SafeDiv), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!, BuildExpressionFeatureMajor(div.Numerator, featureIndexes, featuresParam, indexVar), BuildExpressionFeatureMajor(div.Denominator, featureIndexes, featuresParam, indexVar)),
            Power pow => System.Linq.Expressions.Expression.Call(typeof(Math).GetMethod(nameof(Math.Pow), new[] { typeof(double), typeof(double) })!, BuildExpressionFeatureMajor(pow.Base, featureIndexes, featuresParam, indexVar), BuildExpressionFeatureMajor(pow.Exponent, featureIndexes, featuresParam, indexVar)),
            _ => throw new NotSupportedException($"Compilation does not support {expression.GetType().Name}.")
        };
    }

    private static System.Linq.Expressions.Expression BuildSymbolExpressionFeatureMajor(
        string name,
        IReadOnlyDictionary<string, int> featureIndexes,
        System.Linq.Expressions.ParameterExpression featuresParam,
        System.Linq.Expressions.ParameterExpression indexVar)
    {
        if (featureIndexes.TryGetValue(name, out int index))
        {
            var featureArray = System.Linq.Expressions.Expression.ArrayAccess(featuresParam, System.Linq.Expressions.Expression.Constant(index));
            return System.Linq.Expressions.Expression.ArrayAccess(featureArray, indexVar);
        }

        double val = name.ToLowerInvariant() switch
        {
            "pi" => Math.PI,
            "e" => Math.E,
            _ => 0.0
        };
        return System.Linq.Expressions.Expression.Constant(val);
    }

    private static Dictionary<string, int> CreateFeatureIndexMap(IReadOnlyList<string> featureNames)
    {
        var featureIndexes = new Dictionary<string, int>(featureNames.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < featureNames.Count; i++)
        {
            featureIndexes[featureNames[i]] = i;
        }

        return featureIndexes;
    }

    private static double Evaluate(IExpression expression, IReadOnlyDictionary<string, int> featureIndexes, IReadOnlyList<double> row)
    {
        return expression switch
        {
            Number number => (double)number.Value,
            Symbol symbol => ResolveFeature(symbol.Name, featureIndexes, row),
            Add add => add.Arguments.Sum(arg => Evaluate(arg, featureIndexes, row)),
            Subtract sub => Evaluate(sub.LeftOperand, featureIndexes, row) - Evaluate(sub.RightOperand, featureIndexes, row),
            Multiply mul => mul.Arguments.Aggregate(1.0, (acc, arg) => acc * Evaluate(arg, featureIndexes, row)),
            Divide div => SafeDiv(Evaluate(div.Numerator, featureIndexes, row), Evaluate(div.Denominator, featureIndexes, row)),
            Power pow => Math.Pow(Evaluate(pow.Base, featureIndexes, row), Evaluate(pow.Exponent, featureIndexes, row)),
            _ => throw new NotSupportedException($"Regression evaluation does not support {expression.GetType().Name}.")
        };
    }

    private static double ResolveFeature(string name, IReadOnlyDictionary<string, int> featureIndexes, IReadOnlyList<double> row)
    {
        if (featureIndexes.TryGetValue(name, out int index))
        {
            return row[index];
        }

        return name switch
        {
            "pi" => Math.PI,
            "e" => Math.E,
            _ => 0.0
        };
    }

    private static double SafeDiv(double numerator, double denominator)
    {
        if (Math.Abs(denominator) < 1e-9)
        {
            return numerator >= 0 ? 1e6 : -1e6;
        }

        return numerator / denominator;
    }

    private static int EstimateComplexity(IExpression expression)
    {
        return expression switch
        {
            Atom => 1,
            Operation op => 1 + op.Arguments.Sum(EstimateComplexity),
            _ => 1
        };
    }
}
