using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

internal readonly struct NumericInterval
{
    public decimal? Lower { get; }
    public decimal? Upper { get; }

    public NumericInterval(decimal? lower, decimal? upper)
    {
        Lower = lower;
        Upper = upper;
    }

    public bool IsEmpty
    {
        get
        {
            if (!Lower.HasValue || !Upper.HasValue) return false;
            return Lower.Value >= Upper.Value;
        }
    }
}

internal static class IntervalSet
{
    public static ImmutableList<NumericInterval> Normalize(IEnumerable<NumericInterval> intervals)
    {
        var ordered = intervals
            .Where(i => !i.IsEmpty)
            .OrderBy(i => i.Lower ?? decimal.MinValue)
            .ThenBy(i => i.Upper ?? decimal.MaxValue)
            .ToList();

        if (ordered.Count == 0)
        {
            return ImmutableList<NumericInterval>.Empty;
        }

        var merged = new List<NumericInterval> { ordered[0] };
        for (int i = 1; i < ordered.Count; i++)
        {
            var current = merged[merged.Count - 1];
            var next = ordered[i];
            if (OverlapsOrTouches(current, next))
            {
                merged[merged.Count - 1] = Merge(current, next);
            }
            else
            {
                merged.Add(next);
            }
        }

        return merged.ToImmutableList();
    }

    public static ImmutableList<NumericInterval> Union(ImmutableList<NumericInterval> left, ImmutableList<NumericInterval> right)
        => Normalize(left.Concat(right));

    public static ImmutableList<NumericInterval> Intersect(ImmutableList<NumericInterval> left, ImmutableList<NumericInterval> right)
    {
        if (left.Count == 0 || right.Count == 0) return ImmutableList<NumericInterval>.Empty;

        var result = new List<NumericInterval>();
        foreach (var a in left)
        {
            foreach (var b in right)
            {
                var lower = MaxLower(a.Lower, b.Lower);
                var upper = MinUpper(a.Upper, b.Upper);
                var candidate = new NumericInterval(lower, upper);
                if (!candidate.IsEmpty) result.Add(candidate);
            }
        }

        return Normalize(result);
    }

    public static ImmutableList<NumericInterval> Complement(ImmutableList<NumericInterval> intervals)
    {
        var normalized = Normalize(intervals);
        if (normalized.Count == 0)
        {
            return ImmutableList.Create(new NumericInterval(null, null));
        }

        var result = new List<NumericInterval>();
        decimal? cursor = null;
        foreach (var interval in normalized)
        {
            if (!BoundsEqual(cursor, interval.Lower))
            {
                var gap = new NumericInterval(cursor, interval.Lower);
                if (!gap.IsEmpty) result.Add(gap);
            }
            cursor = interval.Upper;
        }

        if (cursor != null)
        {
            result.Add(new NumericInterval(cursor, null));
        }

        return Normalize(result);
    }

    public static bool TryExtractIntervalSet(IExpression expr, Symbol? target, out ImmutableList<NumericInterval> intervals)
    {
        intervals = ImmutableList<NumericInterval>.Empty;

        if (expr is Symbol s)
        {
            if (IsBooleanConstant(s, out var value))
            {
                intervals = value
                    ? ImmutableList.Create(new NumericInterval(null, null))
                    : ImmutableList<NumericInterval>.Empty;
                return true;
            }
            return false;
        }

        if (expr is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            if (name == "interval" && TryExtractInterval(fn, out var interval))
            {
                intervals = Normalize(new[] { interval });
                return true;
            }

            if (name == "or")
            {
                var union = ImmutableList<NumericInterval>.Empty;
                foreach (var arg in fn.Arguments)
                {
                    if (!TryExtractIntervalSet(arg, target, out var part)) return false;
                    union = Union(union, part);
                }
                intervals = union;
                return true;
            }

            if (name == "and")
            {
                bool hasAny = false;
                var current = ImmutableList<NumericInterval>.Empty;
                foreach (var arg in fn.Arguments)
                {
                    if (!TryExtractIntervalSet(arg, target, out var part)) return false;
                    current = hasAny ? Intersect(current, part) : part;
                    hasAny = true;
                }
                intervals = hasAny ? current : ImmutableList<NumericInterval>.Empty;
                return true;
            }

            if (name == "not" && fn.Arguments.Count == 1)
            {
                if (!TryExtractIntervalSet(fn.Arguments[0], target, out var part)) return false;
                intervals = Complement(part);
                return true;
            }

            if (IsInequalityName(name) && TryExtractIntervalFromInequality(fn, target, out var ineqInterval))
            {
                intervals = Normalize(new[] { ineqInterval });
                return true;
            }
        }

        return false;
    }

    public static IExpression ToExpression(ImmutableList<NumericInterval> intervals)
    {
        if (intervals.Count == 0)
        {
            return new Symbol("false");
        }

        var normalized = Normalize(intervals);
        var parts = normalized.Select(BuildIntervalExpression).ToImmutableList();
        if (parts.Count == 1)
        {
            return parts[0];
        }
        return new Function("or", parts).Canonicalize();
    }

    internal static bool TryExtractBound(IExpression expr, out decimal? value, out bool isNegativeInfinity, out bool isPositiveInfinity)
    {
        value = null;
        isNegativeInfinity = false;
        isPositiveInfinity = false;

        if (expr is Number n)
        {
            value = n.Value;
            return true;
        }

        if (expr is Function f)
        {
            var name = f.Name.ToLowerInvariant();
            if (name == "minus_infinity")
            {
                isNegativeInfinity = true;
                return true;
            }
            if (name == "infinity")
            {
                isPositiveInfinity = true;
                return true;
            }
        }

        if (expr is Symbol sym)
        {
            if (sym.Name.Equals("minus_infinity", StringComparison.OrdinalIgnoreCase))
            {
                isNegativeInfinity = true;
                return true;
            }
            if (sym.Name.Equals("infinity", StringComparison.OrdinalIgnoreCase))
            {
                isPositiveInfinity = true;
                return true;
            }
        }

        if (NumericEvaluator.TryEvaluate(expr, ImmutableDictionary<string, decimal>.Empty, out var eval, out _))
        {
            value = eval;
            return true;
        }

        return false;
    }

    private static bool TryExtractInterval(Function fn, out NumericInterval interval)
    {
        interval = default;
        if (fn.Arguments.Count != 2) return false;

        if (!TryExtractBound(fn.Arguments[0], out var lower, out var lowerNegInf, out var lowerPosInf)) return false;
        if (!TryExtractBound(fn.Arguments[1], out var upper, out var upperNegInf, out var upperPosInf)) return false;

        if (lowerPosInf || upperNegInf) return false;

        interval = new NumericInterval(lowerNegInf ? null : lower, upperPosInf ? null : upper);
        return true;
    }

    private static bool TryExtractIntervalFromInequality(Function fn, Symbol? target, out NumericInterval interval)
    {
        interval = default;
        if (fn.Arguments.Count != 2) return false;

        var left = fn.Arguments[0];
        var right = fn.Arguments[1];
        var name = fn.Name.ToLowerInvariant();

        bool leftIsTarget = target is null ? left is Symbol : left.InternalEquals(target);
        bool rightIsTarget = target is null ? right is Symbol : right.InternalEquals(target);

        if (leftIsTarget && TryExtractBound(right, out var bound, out var negInf, out var posInf))
        {
            interval = name switch
            {
                "lt" or "le" => new NumericInterval(null, posInf ? null : bound),
                "gt" or "ge" => new NumericInterval(negInf ? null : bound, null),
                _ => interval
            };
            return name is "lt" or "le" or "gt" or "ge";
        }

        if (rightIsTarget && TryExtractBound(left, out var leftBound, out var leftNegInf, out var leftPosInf))
        {
            interval = name switch
            {
                "lt" or "le" => new NumericInterval(leftPosInf ? null : leftBound, null),
                "gt" or "ge" => new NumericInterval(null, leftNegInf ? null : leftBound),
                _ => interval
            };
            return name is "lt" or "le" or "gt" or "ge";
        }

        return false;
    }

    private static bool IsBooleanConstant(Symbol s, out bool value)
    {
        if (s.Name.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (s.Name.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }

    private static bool IsInequalityName(string name)
        => name is "lt" or "le" or "gt" or "ge";

    private static bool OverlapsOrTouches(NumericInterval first, NumericInterval second)
    {
        var firstUpper = first.Upper ?? decimal.MaxValue;
        var secondLower = second.Lower ?? decimal.MinValue;
        return secondLower <= firstUpper;
    }

    private static NumericInterval Merge(NumericInterval first, NumericInterval second)
    {
        var lower = MinLower(first.Lower, second.Lower);
        var upper = MaxUpper(first.Upper, second.Upper);
        return new NumericInterval(lower, upper);
    }

    private static decimal? MinLower(decimal? left, decimal? right)
    {
        if (!left.HasValue || !right.HasValue) return null;
        return Math.Min(left.Value, right.Value);
    }

    private static decimal? MaxLower(decimal? left, decimal? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return Math.Max(left.Value, right.Value);
    }

    private static decimal? MinUpper(decimal? left, decimal? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return Math.Min(left.Value, right.Value);
    }

    private static decimal? MaxUpper(decimal? left, decimal? right)
    {
        if (!left.HasValue || !right.HasValue) return null;
        return Math.Max(left.Value, right.Value);
    }

    private static bool BoundsEqual(decimal? left, decimal? right)
    {
        if (left.HasValue && right.HasValue)
        {
            return left.Value == right.Value;
        }
        return !left.HasValue && !right.HasValue;
    }

    private static IExpression BuildIntervalExpression(NumericInterval interval)
    {
        var lowerValue = interval.Lower;
        var upperValue = interval.Upper;
        IExpression lower = lowerValue.HasValue ? new Number(lowerValue.Value) : MinusInfinity();
        IExpression upper = upperValue.HasValue ? new Number(upperValue.Value) : Infinity();
        return new Function("interval", ImmutableList.Create(lower, upper)).Canonicalize();
    }

    private static IExpression MinusInfinity()
        => new Function("minus_infinity", ImmutableList<IExpression>.Empty).Canonicalize();

    private static IExpression Infinity()
        => new Function("infinity", ImmutableList<IExpression>.Empty).Canonicalize();
}
