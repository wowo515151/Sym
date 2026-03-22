// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// NumericApproximationStrategy: conservative numeric evaluator/approximator.
/// </summary>
public class NumericApproximationStrategy : ISolverStrategy
{
    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        var assignments = context.AdditionalData != null && 
                          context.AdditionalData.TryGetValue(SolverOptionKeys.Substitutions, out var s) && 
                          s is ImmutableDictionary<string, IExpression> dict
            ? dict
            : ImmutableDictionary<string, IExpression>.Empty;

        var numericMap = assignments
            .Where(kv => NumericEvaluator.TryEvaluate(kv.Value, ImmutableDictionary<string, decimal>.Empty, out _, out _))
            .ToDictionary(kv => kv.Key, kv => { NumericEvaluator.TryEvaluate(kv.Value, ImmutableDictionary<string, decimal>.Empty, out var v, out _); return v; });

        var result = EvaluateRecursive(problem, numericMap);
        if (!result.InternalEquals(problem))
        {
            return SolveResult.Success(result, "Numeric approximation applied recursively.");
        }

        IExpression canonical = problem.Canonicalize();

        if (canonical is Number)
        {
            return SolveResult.Success(canonical, "Numeric expression returned as-is.");
        }

        if (!ContainsSymbolic(canonical))
        {
            // Do not eagerly evaluate trigonometric functions to decimals if they are constant;
            // we prefer to keep them symbolic (e.g., cos(58)) so trig rules can apply.
            if (ContainsTrig(canonical))
            {
                return SolveResult.Failure(canonical, "Numeric evaluation skipped for trigonometric expression.");
            }

            // Handle Vector/Matrix of purely numeric terms
            if (canonical is Vector v)
            {
                var newArgs = v.Arguments.Select(arg => Solve(arg, context).ResultExpression ?? arg).ToImmutableList();
                return SolveResult.Success(new Vector(newArgs).Canonicalize(), "Vector components approximated.");
            }
            if (canonical is Matrix m)
            {
                var newArgs = m.Arguments.Select(arg => Solve(arg, context).ResultExpression ?? arg).ToImmutableList();
                return SolveResult.Success(new Matrix(m.MatrixDimensions, newArgs).Canonicalize(), "Matrix components approximated.");
            }

            // Expression is composed only of numeric literals and operations; evaluate it.
            if (NumericEvaluator.TryEvaluate(canonical, new Dictionary<string, decimal>(), out var value, out var evalError))
            {
                return SolveResult.Success(new Number(value), "Numeric expression evaluated.");
            }
            else
            {
                // Treat failed numeric evaluation (including overflow) as a preservation signal
                return SolveResult.Failure(canonical, "Numeric evaluation failed (possible overflow); preserving symbolic form.");
            }
        }

        // Try to simplify common functions with constant arguments
        var simplified = SimplifyConstants(canonical);
        if (!simplified.InternalEquals(canonical))
        {
            return SolveResult.Success(simplified, "Constant sub-expressions simplified.");
        }

        return SolveResult.Failure(problem, "NumericApproximationStrategy currently only supports numeric literals and purely numeric expressions.");
    }

    public static IExpression EvaluateRecursive(IExpression expr, IReadOnlyDictionary<string, decimal>? numericMap = null)
    {
        numericMap ??= ImmutableDictionary<string, decimal>.Empty;
        if (expr is Number) return expr;
        
        if (expr is Symbol s)
        {
            if (numericMap.TryGetValue(s.Name, out var val)) return new Number(val);
            return expr;
        }

        if (expr is Equality eq)
        {
            var left = EvaluateRecursive(eq.LeftOperand, numericMap);
            var right = EvaluateRecursive(eq.RightOperand, numericMap);
            return new Equality(left, right).Canonicalize();
        }

        if (expr is Vector vec)
        {
            return new Vector(vec.Arguments.Select(a => EvaluateRecursive(a, numericMap)).ToImmutableList()).Canonicalize();
        }

        if (expr is Operation op)
        {
            var newArgs = op.Arguments.Select(a => EvaluateRecursive(a, numericMap)).ToImmutableList();
            var newOp = op.WithArguments(newArgs).Canonicalize();

            // Only try to evaluate if all symbols are now mapped to numbers (including pi/e if they are in numericMap)
            // Or if they are known constants and we are sure we want to evaluate them.
            if (NumericEvaluator.TryEvaluate(newOp, numericMap, out var val, out _))
            {
                // Check if any symbols remain that are NOT in numericMap
                // (NumericEvaluator might have used its internal pi/e defaults)
                bool hasUnmappedSymbols = newOp.ContainsSymbol(s => !numericMap.ContainsKey(s.Name));
                
                // Do not eagerly evaluate trigonometric functions to decimals if they are constant;
                // we prefer to keep them symbolic (e.g., cos(58)) so trig rules can apply.
                if (!hasUnmappedSymbols && !ExpressionClassification.ContainsTrig(newOp))
                {
                    // Snap to integer if very close
                    if (Math.Abs(val - Math.Round(val)) < 1e-10m)
                    {
                        return new Number(Math.Round(val));
                    }
                    return new Number(val);
                }
            }
            return newOp;
        }

        return expr;
    }

    private static IExpression SimplifyConstants(IExpression expr)
    {
        if (expr is not Operation op) return expr;
        var args = op.Arguments.Select(SimplifyConstants).ToImmutableList();
        var rebuilt = op.WithArguments(args).Canonicalize();

        if (rebuilt is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            if ((name == "floor" || name == "ceil" || name == "ceiling") && fn.Arguments.Count == 1)
            {
                if (NumericEvaluator.TryEvaluate(fn.Arguments[0], new Dictionary<string, decimal>(), out var val, out _))
                {
                    decimal result = name == "floor" ? Math.Floor(val) : Math.Ceiling(val);
                    return new Number(result);
                }
            }
        }
        return rebuilt;
    }

    private static bool ContainsSymbolic(IExpression expr)
    {
        if (expr is null) return false;

        if (expr is Symbol || expr is Wild)
        {
            return true;
        }

        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                if (ContainsSymbolic(arg))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsTrig(IExpression expr)
    {
        if (expr is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            if (name is "sin" or "cos" or "tan") return true;
        }

        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                if (ContainsTrig(arg)) return true;
            }
        }

        return false;
    }
}
