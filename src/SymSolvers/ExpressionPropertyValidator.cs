using System.Collections.Generic;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymCore;

namespace SymSolvers;

/// <summary>
/// Validates algebraic properties (e.g., equality holds) via numeric sampling.
/// </summary>
public static class ExpressionPropertyValidator
{
    private static readonly decimal[] CoreSamples = new[]
    {
        -3m, -2m, -1.5m, -1m, -2m / 3m, -0.5m, -0.25m, 0m, 0.25m, 0.5m, 2m / 3m, 1m, 1.5m, 2m, 3m
    };

    /// <summary>
    /// Builds a deterministic set of validation samples that exercises rationals and small intervals.
    /// Respects common assumptions (positive/integer) when possible.
    /// </summary>
    public static IReadOnlyList<decimal> BuildDefaultSamples(Symbol? target = null, Assumptions? assumptions = null)
    {
        var samples = new List<decimal>();
        var seen = new HashSet<decimal>();

        void Add(decimal value)
        {
            if (seen.Add(value))
            {
                samples.Add(value);
            }
        }

        foreach (var s in CoreSamples)
        {
            Add(s);
        }

        if (assumptions is not null && target is not null)
        {
            if (assumptions.IsPositive(target.Name))
            {
                samples = samples.Where(s => s > 0m).ToList();
                if (samples.Count == 0)
                {
                    samples.AddRange(new[] { 0.25m, 0.5m, 1m, 2m });
                }
            }

            if (assumptions.IsInteger(target.Name))
            {
                var ints = samples.Select(decimal.Truncate).Where(v => v >= -4m && v <= 4m).ToList();
                samples.Clear();
                foreach (var v in ints)
                {
                    Add(v);
                }
                if (samples.Count == 0)
                {
                    Add(0m);
                    Add(1m);
                    Add(-1m);
                }
            }
        }

        if (samples.Count == 0)
        {
            samples.AddRange(new[] { -1m, 0m, 1m });
        }

        return samples;
    }

    public static PropertyValidationResult ValidateEquality(IExpression expr, Symbol? target = null, IEnumerable<decimal>? samples = null, decimal tolerance = 1e-6m, Assumptions? assumptions = null)
    {
        if (expr is not Equality equality)
        {
            return PropertyValidationResult.Failure("Expression is not an equality.");
        }

        var hasAssumptions = assumptions is not null && assumptions.HasAny;

        // If there are no symbols at all, validate directly as a numeric equality.
        if (FindFirstSymbol(expr) is null)
        {
            var empty = new Dictionary<string, decimal>();
            if (!NumericEvaluator.TryEvaluate(equality.LeftOperand, empty, out var left, out var errL))
            {
                var reason = string.IsNullOrWhiteSpace(errL) ? "Evaluation failed." : errL;
                return PropertyValidationResult.Failure(reason);
            }

            if (!NumericEvaluator.TryEvaluate(equality.RightOperand, empty, out var right, out var errR))
            {
                var reason = string.IsNullOrWhiteSpace(errR) ? "Evaluation failed." : errR;
                return PropertyValidationResult.Failure(reason);
            }

            return decimal.Abs(left - right) <= tolerance
                ? PropertyValidationResult.Ok("Equality holds for sampled points.")
                : PropertyValidationResult.Failure($"Counterexample: {left} != {right}");
        }

        var symbol = target;
        if (symbol is null || !expr.ContainsSymbol(symbol))
        {
            symbol = FindFirstSymbol(expr);
        }
        if (symbol is null)
        {
            return PropertyValidationResult.Failure("No symbol to bind for validation.");
        }

        var samplePoints = BuildSamples(samples, equality, symbol, assumptions);

        if (hasAssumptions && assumptions is not null && TryGetIsolatedNumericCandidate(equality, symbol, out var isolated))
        {
            var conflict = CheckAssumptionConflict(symbol, isolated, assumptions);
            if (conflict is not null)
            {
                return PropertyValidationResult.Failure(conflict, true);
            }
        }

        var allSymbols = SymbolCollector.CollectSymbolNames(
            equality,
            name => name.Equals("pi", System.StringComparison.OrdinalIgnoreCase) || name.Equals("e", System.StringComparison.OrdinalIgnoreCase),
            StringComparer.Ordinal);

        foreach (var val in samplePoints.Distinct())
        {
            var assignments = new Dictionary<string, decimal>();
            foreach (var s in allSymbols)
            {
                assignments[s] = s == symbol.Name ? val : 1m;
            }

            if (!NumericEvaluator.TryEvaluate(equality.LeftOperand, assignments, out var left, out var errL))
            {
                continue; // Skip points outside the domain
            }

            if (!NumericEvaluator.TryEvaluate(equality.RightOperand, assignments, out var right, out var errR))
            {
                continue; // Skip points outside the domain
            }

            if (decimal.Abs(left - right) > tolerance)
            {
                return PropertyValidationResult.Failure($"Counterexample at {symbol.Name}={val}: {left} != {right}");
            }
        }

        return PropertyValidationResult.Ok("Equality holds for sampled points.");
    }

    /// <summary>
    /// Validates assumption compatibility for an isolated solution equality (e.g., x = 2).
    /// This does NOT validate the equality as an identity over all x.
    /// </summary>
    public static PropertyValidationResult ValidateIsolatedSolutionAssumptions(Equality equality, Assumptions? assumptions)
    {
        if (assumptions is null || !assumptions.HasAny)
        {
            return PropertyValidationResult.Ok("No assumptions to validate.");
        }

        var empty = new Dictionary<string, decimal>();

        if (TryGetIsolatedSolutionSymbol(equality, out var symbol, out var boundExpr))
        {
            if (NumericEvaluator.TryEvaluate(boundExpr, empty, out var candidate, out _))
            {
                var conflict = CheckAssumptionConflict(symbol, candidate, assumptions);
                return conflict is null
                    ? PropertyValidationResult.Ok("Isolated solution assumptions validated.")
                    : PropertyValidationResult.Failure(conflict, true);
            }

            return PropertyValidationResult.Ok("Isolated solution is non-numeric; assumptions not checked.");
        }

        return PropertyValidationResult.Ok("Not an isolated solution equality.");
    }

    public static bool TryGetIsolatedSolutionSymbol(Equality equality, out Symbol symbol, out IExpression boundExpression)
    {
        symbol = null!;
        boundExpression = null!;

        bool HasPrefix(IExpression expr) => expr is Symbol s && s.Name.StartsWith("ans_", System.StringComparison.Ordinal);

        if (HasPrefix(equality.LeftOperand))
        {
            symbol = (Symbol)equality.LeftOperand;
            boundExpression = equality.RightOperand;
            return true;
        }

        if (HasPrefix(equality.RightOperand))
        {
            symbol = (Symbol)equality.RightOperand;
            boundExpression = equality.LeftOperand;
            return true;
        }

        bool IsIsolated(IExpression expr, IExpression other, out Symbol s)
        {
            s = null!;
            var sym = FindFirstSymbol(expr);
            if (sym is not null && !other.ContainsSymbol(sym) && expr.InternalEquals(sym))
            {
                // Heuristic: if the other side is a Function that typically acts as a constraint (e.g. Integer), 
                // don't consider it isolated here.
                if (other is Function fn && (fn.Name.Equals("Integer", StringComparison.OrdinalIgnoreCase) ||
                                             fn.Name.Equals("IsSquare", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
                s = sym;
                return true;
            }
            return false;
        }

        if (IsIsolated(equality.LeftOperand, equality.RightOperand, out var leftSym))
        {
            symbol = leftSym;
            boundExpression = equality.RightOperand;
            return true;
        }

        if (IsIsolated(equality.RightOperand, equality.LeftOperand, out var rightSym))
        {
            symbol = rightSym;
            boundExpression = equality.LeftOperand;
            return true;
        }

        // Handle simple linear scaling: n * x = val => x = val / n
        bool IsLinearScaling(IExpression expr, IExpression other, out Symbol s, out IExpression val)
        {
            s = null!;
            val = null!;
            if (expr is Multiply mul && mul.Arguments.Count == 2)
            {
                var n = mul.Arguments.OfType<Number>().FirstOrDefault();
                var sym = mul.Arguments.OfType<Symbol>().FirstOrDefault();
                if (n != null && n.Value != 0m && sym != null && !other.ContainsSymbol(sym))
                {
                    s = sym;
                    val = new Divide(other, n).Canonicalize();
                    return true;
                }
            }
            return false;
        }

        if (IsLinearScaling(equality.LeftOperand, equality.RightOperand, out var s1, out var v1))
        {
            symbol = s1;
            boundExpression = v1;
            return true;
        }

        if (IsLinearScaling(equality.RightOperand, equality.LeftOperand, out var s2, out var v2))
        {
            symbol = s2;
            boundExpression = v2;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Validates interval expressions when concrete numeric bounds are present.
    /// Ensures ordering (lower &lt;= upper) and bound orientation (minus_infinity only on the lower side).
    /// </summary>
    public static PropertyValidationResult ValidateIntervals(IExpression expr)
    {
        var intervals = new List<Function>();
        CollectIntervals(expr, intervals);
        if (intervals.Count == 0)
        {
            return PropertyValidationResult.Ok("No intervals to validate.");
        }

        foreach (var interval in intervals)
        {
            if (interval.Arguments.Count != 2)
            {
                return PropertyValidationResult.Failure("Interval must contain exactly two bounds.");
            }

            var lowerExpr = interval.Arguments[0];
            var upperExpr = interval.Arguments[1];

            if (!TryExtractBound(lowerExpr, out var lower, out var lowerNegInf, out var lowerPosInf))
            {
                return PropertyValidationResult.Failure("Interval lower bound is not numeric.");
            }

            if (!TryExtractBound(upperExpr, out var upper, out var upperNegInf, out var upperPosInf))
            {
                return PropertyValidationResult.Failure("Interval upper bound is not numeric.");
            }

            if (lowerPosInf || upperNegInf)
            {
                return PropertyValidationResult.Failure("Interval bounds use infinities in the wrong position.");
            }

            if (lowerNegInf || upperPosInf)
            {
                continue;
            }

            if (lower.HasValue && upper.HasValue && lower.Value > upper.Value)
            {
                // Avoid embedding '=' or other characters that may confuse XML doc parsers in warnings.
                return PropertyValidationResult.Failure($"Interval lower bound {lower.Value} exceeds upper bound {upper.Value}.");
            }
        }

        return PropertyValidationResult.Ok("Intervals validated.");
    }

    private static bool TryGetIsolatedNumericCandidate(Equality equality, Symbol symbol, out decimal value)
    {
        value = 0m;

        var empty = new Dictionary<string, decimal>();

        if (equality.LeftOperand is Symbol leftSym && string.Equals(leftSym.Name, symbol.Name, System.StringComparison.Ordinal))
        {
            return NumericEvaluator.TryEvaluate(equality.RightOperand, empty, out value, out _);
        }

        if (equality.RightOperand is Symbol rightSym && string.Equals(rightSym.Name, symbol.Name, System.StringComparison.Ordinal))
        {
            return NumericEvaluator.TryEvaluate(equality.LeftOperand, empty, out value, out _);
        }

        return false;
    }

    private static List<decimal> BuildSamples(IEnumerable<decimal>? explicitSamples, Equality eq, Symbol symbol, Assumptions? assumptions)
    {
        bool Accept(decimal v, ICollection<decimal> list, ISet<decimal> seen)
        {
            if (assumptions is not null)
            {
                if (assumptions.IsPositive(symbol.Name) && v <= 0m) return false;
                if (assumptions.IsInteger(symbol.Name) && decimal.Truncate(v) != v) return false;
            }
            if (seen.Add(v))
            {
                list.Add(v);
                return true;
            }
            return false;
        }

        if (explicitSamples is not null)
        {
            return explicitSamples.ToList();
        }

        var samples = new List<decimal>();
        var seen = new HashSet<decimal>();

        if (TryGetIsolatedNumericCandidate(eq, symbol, out var candidate))
        {
            Accept(candidate, samples, seen);
            Accept(candidate + 1m, samples, seen);
            Accept(candidate - 1m, samples, seen);
        }

        foreach (var sample in BuildDefaultSamples(symbol, assumptions))
        {
            Accept(sample, samples, seen);
        }

        if (samples.Count == 0)
        {
            Accept(0m, samples, seen);
            Accept(1m, samples, seen);
        }

        return samples;
    }

    private static Symbol? FindFirstSymbol(IExpression expr)
    {
        if (expr is Symbol s)
        {
            var keywords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Integer", "IsSquare", "GCD", "LCM", "abs", "sqrt", "exp", "log", "log10", "log2", "sin", "cos", "tan", "factorial", "combination", "binomial", "permutation", "φ", "phi", "Greater", "Less", "GreaterEqual", "LessEqual", "ne", "NotEqual", "valuation", "round", "floor", "ceil", "ceiling", "csc", "sec", "cot", "sind", "cosd", "tand", "cscd", "secd", "cotd", "atan2", "mod", "isprime" };
            if (keywords.Contains(s.Name)) return null;
            return s;
        }
        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                var found = FindFirstSymbol(arg);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static void CollectIntervals(IExpression expr, List<Function> intervals)
    {
        if (expr is Function fn && string.Equals(fn.Name, "interval", System.StringComparison.OrdinalIgnoreCase))
        {
            intervals.Add(fn);
        }

        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                CollectIntervals(arg, intervals);
            }
        }
    }

    private static bool TryExtractBound(IExpression expr, out decimal? value, out bool isNegativeInfinity, out bool isPositiveInfinity)
    {
        return IntervalSet.TryExtractBound(expr, out value, out isNegativeInfinity, out isPositiveInfinity);
    }

    private static string? CheckAssumptionConflict(Symbol symbol, decimal value, Assumptions assumptions)
    {
        if (assumptions.IsPositive(symbol.Name) && value <= 0m)
        {
            return $"Assumption conflict: {symbol.Name} is positive but candidate value {value} violates it.";
        }

        if (assumptions.IsInteger(symbol.Name) && decimal.Truncate(value) != value)
        {
            return $"Assumption conflict: {symbol.Name} is integer but candidate value {value} is non-integer.";
        }

        return null;
    }
}

public sealed record PropertyValidationResult(bool Success, string Message, bool AssumptionConflict = false)
{
    public static PropertyValidationResult Ok(string message) => new(true, message, false);
    public static PropertyValidationResult Failure(string message, bool assumptionConflict = false) => new(false, message, assumptionConflict);
}
