using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.Strategies;
using Sym.CSharpIO;
using Sym.Operations;
using SymRules;
using SymSolvers;
using WordsToSym;

namespace SymTools;

public static class SymToolFunctions
{
    public static SymToolResult EvalExpression(
        string expression,
        IReadOnlyDictionary<string, decimal>? substitutions = null)
    {
        try
        {
            var parsed = ParseSingleExpression(expression, nameof(expression));
            if (parsed is null)
            {
                return Failure("", "Expression could not be parsed.");
            }

            var assignmentMap = substitutions is null
                ? ImmutableDictionary<string, decimal>.Empty
                : ImmutableDictionary.CreateRange(substitutions);

            if (!NumericEvaluator.TryEvaluate(parsed, assignmentMap, out var result, out var error))
            {
                return Failure(FormatExpression(parsed), error ?? "Numeric evaluation failed.");
            }

            return Success(result.ToString(CultureInfo.InvariantCulture), "Numeric evaluation completed.");
        }
        catch (Exception ex)
        {
            return Failure("", ex.Message);
        }
    }

    public static SymToolResult SimplifyExpression(
        string expression,
        IEnumerable<string>? rulePacks = null,
        bool enableTracing = false,
        int maxIterations = 100)
    {
        try
        {
            var parsed = ParseSingleExpression(expression, nameof(expression));
            if (parsed is null)
            {
                return Failure("", "Expression could not be parsed.");
            }

            var context = BuildContext(
                targetVariable: null,
                enableTracing: enableTracing,
                maxIterations: maxIterations,
                rulePacks: rulePacks);

            var rules = RuleProvider.BuildRules(context);
            var strategy = new FullSimplificationStrategy();
            var result = strategy.Solve(parsed, new SolveContext(null, rules, maxIterations, enableTracing, context.AdditionalData));
            return FromSolveResult(result);
        }
        catch (Exception ex)
        {
            return Failure("", ex.Message);
        }
    }

    public static SymToolResult SolveEquation(
        string equation,
        string targetVariable,
        bool enableTracing = false,
        int maxIterations = 100)
    {
        try
        {
            var parsed = ParseSingleExpression(equation, nameof(equation));
            if (parsed is not Equality equality)
            {
                return Failure(parsed is null ? "" : FormatExpression(parsed), "Input must parse to a single equation.");
            }

            var context = BuildContext(new Symbol(targetVariable), enableTracing, maxIterations, rulePacks: null);
            var strategy = new EquationSolverStrategy();
            var result = strategy.Solve(equality, context);
            return FromSolveResult(result);
        }
        catch (Exception ex)
        {
            return Failure("", ex.Message);
        }
    }

    public static SymToolResult SolveSystem(
        IEnumerable<string> equations,
        string? targetVariable = null,
        bool enableTracing = false,
        int maxIterations = 100)
    {
        try
        {
            if (equations is null)
            {
                return Failure("", "Equations collection cannot be null.");
            }

            var parts = new List<IExpression>();
            foreach (var equation in equations)
            {
                var parsed = ParseSingleExpression(equation, nameof(equations));
                if (parsed is not Equality)
                {
                    return Failure(parsed is null ? "" : FormatExpression(parsed), "Each system entry must parse to a single equation.");
                }

                parts.Add(parsed);
            }

            if (parts.Count == 0)
            {
                return Failure("", "At least one equation is required.");
            }

            var problem = parts.Count == 1 ? parts[0] : new Vector(parts.ToImmutableList()).Canonicalize();
            var target = string.IsNullOrWhiteSpace(targetVariable) ? null : new Symbol(targetVariable);
            var context = BuildContext(target, enableTracing, maxIterations, rulePacks: null);
            var rules = RuleProvider.BuildRules(context);
            var solveContext = new SolveContext(target, rules, maxIterations, enableTracing, context.AdditionalData);
            var result = new EGraphSolverStrategy().Solve(problem, solveContext);
            return FromSolveResult(result);
        }
        catch (Exception ex)
        {
            return Failure("", ex.Message);
        }
    }

    public static SymToolResult SolveProblemScript(string problemScript)
    {
        try
        {
            var wrapper = new ProblemScriptEGraphWrapper();
            var output = wrapper.SolveWithEGraph(problemScript ?? string.Empty);
            return output.StartsWith("Error:", StringComparison.Ordinal)
                ? Failure(output, "ProblemScript solving failed.")
                : Success(output, "ProblemScript solving completed.");
        }
        catch (Exception ex)
        {
            return Failure("", ex.Message);
        }
    }

    public static SymToolResult OptimizeTensorExpression(
        string expression,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? shapes = null,
        bool enableTracing = false,
        int maxIterations = 200)
    {
        try
        {
            var script = BuildTensorProblemScript(expression, shapes, maxIterations);
            var wrapper = new ProblemScriptEGraphWrapper();
            var output = wrapper.SolveWithEGraph(script);
            return output.StartsWith("Error:", StringComparison.Ordinal)
                ? Failure(output, "Tensor optimization failed.")
                : Success(output, enableTracing ? "Tensor optimization completed." : "Tensor optimization completed.");
        }
        catch (Exception ex)
        {
            return Failure("", ex.Message);
        }
    }

    public static SymToolResult DifferentiateExpression(string expression, string variable)
    {
        try
        {
            var parsed = ParseSingleExpression(expression, nameof(expression));
            if (parsed is null)
            {
                return Failure("", "Expression could not be parsed.");
            }

            var differentiated = CalculusHelper.DifferentiateExpression(parsed, new Symbol(variable)).Canonicalize();
            return Success(FormatExpression(differentiated), "Differentiation completed.");
        }
        catch (Exception ex)
        {
            return Failure("", ex.Message);
        }
    }

    public static SymToolResult IntegrateExpression(
        string expression,
        string variable,
        bool enableTracing = false,
        int maxIterations = 100)
    {
        try
        {
            var parsed = ParseSingleExpression(expression, nameof(expression));
            if (parsed is null)
            {
                return Failure("", "Expression could not be parsed.");
            }

            var integral = new Integral(parsed, new Symbol(variable)).Canonicalize();
            var context = BuildContext(new Symbol(variable), enableTracing, maxIterations, rulePacks: new[] { "IntegrationStrategy" });
            var result = new IntegrationStrategy().Solve(integral, context);
            return FromSolveResult(result);
        }
        catch (Exception ex)
        {
            return Failure("", ex.Message);
        }
    }

    private static SolveContext BuildContext(
        Symbol? targetVariable,
        bool enableTracing,
        int maxIterations,
        IEnumerable<string>? rulePacks)
    {
        ImmutableDictionary<string, object>? additionalData = null;
        if (rulePacks is not null)
        {
            var joined = string.Join(",", rulePacks.Where(static pack => !string.IsNullOrWhiteSpace(pack)));
            if (!string.IsNullOrWhiteSpace(joined))
            {
                additionalData = ImmutableDictionary<string, object>.Empty.Add("RulePacks", joined);
            }
        }

        return new SolveContext(targetVariable, rules: null, maxIterations, enableTracing, additionalData);
    }

    private static IExpression? ParseSingleExpression(string source, string paramName)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Input cannot be null or whitespace.", paramName);
        }

        var parsed = CSharpIO.ParseExpressionsStrict(source);
        if (parsed.Count != 1)
        {
            throw new InvalidOperationException("Input must contain exactly one expression.");
        }

        return parsed[0];
    }

    private static string BuildTensorProblemScript(
        string expression,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? shapes,
        int maxIterations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<Options>");
        builder.AppendLine("  RulePacks: Tensor");
        builder.AppendLine("  CostModel: Tensor");
        builder.AppendLine($"  MaxIterations: {maxIterations.ToString(CultureInfo.InvariantCulture)}");
        if (shapes is not null && shapes.Count > 0)
        {
            var shapePairs = shapes.Select(static entry => $"{entry.Key}=[{string.Join(",", entry.Value)}]");
            builder.AppendLine($"  Shapes: {string.Join(", ", shapePairs)}");
        }
        builder.AppendLine("</Options>");
        builder.AppendLine(expression);
        return builder.ToString();
    }

    private static SymToolResult FromSolveResult(SolveResult result)
    {
        var output = result.ResultExpression is null ? string.Empty : FormatExpression(result.ResultExpression);
        var trace = result.Trace?.Select(FormatExpression).ToImmutableList();
        return result.IsSuccess
            ? Success(output, result.Message, trace)
            : Failure(string.IsNullOrEmpty(output) ? output : output, result.Message, trace);
    }

    private static string FormatExpression(IExpression expression)
    {
        return CSharpIO.FormatExpr(expression);
    }

    private static SymToolResult Success(string output, string message, IReadOnlyList<string>? trace = null)
    {
        return new SymToolResult(true, output, message, trace);
    }

    private static SymToolResult Failure(string output, string message, IReadOnlyList<string>? trace = null)
    {
        return new SymToolResult(false, output, message, trace);
    }
}