using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Attempts to solve linear inequalities (lt/le/gt/ge) by isolating the target variable and intersecting bounds.
/// </summary>
public class InequalitySolveStrategy : ISolverStrategy
{
    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        context.ThrowIfCancellationRequested();

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? symbolAttributes = null;
        if (context.AdditionalData != null && context.AdditionalData.TryGetValue("Attributes", out var attrObj))
        {
            symbolAttributes = attrObj as IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>;
            if (symbolAttributes == null && attrObj is Dictionary<string, Dictionary<string, double>> dict)
            {
                var builder = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in dict) builder[kvp.Key] = kvp.Value;
                symbolAttributes = builder;
            }
        }

        if (problem is Vector vec)
        {
            // Treat vector as a conjunction of constraints. 
            // We only solve the parts that are inequalities.
            var parts = vec.Arguments.ToList();
            var solvedAny = false;
            var newParts = new List<IExpression>();
            var otherParts = new List<IExpression>();

            foreach (var part in parts)
            {
                if (IsInequalityContainer(part))
                {
                    var res = Solve(part, context);
                    if (res.IsSuccess && res.ResultExpression is not null)
                    {
                        newParts.Add(res.ResultExpression);
                        solvedAny = true;
                    }
                    else
                    {
                        newParts.Add(part);
                    }
                }
                else
                {
                    otherParts.Add(part);
                }
            }

            if (!solvedAny) return SolveResult.Failure(problem, "No inequalities solved in vector.");

            if (otherParts.Count == 0 && newParts.Count > 1)
            {
                // All parts were inequalities. Conjoin them into a single interval result.
                var conjunction = new Function("and", newParts.ToImmutableList()).Canonicalize();
                if (conjunction is Function fnConj)
                {
                    return SolveConjunction(fnConj, context);
                }
            }

            var finalParts = otherParts.Concat(newParts).ToImmutableList();
            var result = new Vector(finalParts).Canonicalize();
            var trace = context.EnableTracing ? ImmutableList.Create(problem, result) : null;
            return SolveResult.Success(result, "Inequalities solved within vector.", trace);
        }

        if (problem is Function combo)
        {
            if (combo.Name.Equals("and", StringComparison.OrdinalIgnoreCase))
            {
                return SolveConjunction(combo, context);
            }
            if (combo.Name.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                return SolveDisjunction(combo, context);
            }
            if (combo.Name.Equals("not", StringComparison.OrdinalIgnoreCase))
            {
                return SolveNegation(combo, context);
            }
        }
        if (problem is Piecewise pw)
        {
            return SolvePiecewise(pw, context);
        }

        if (problem is not Function fn || fn.Arguments.Count != 2) return SolveResult.Failure(problem, "InequalitySolveStrategy requires a two-argument inequality function.");

        var target = context.TargetVariable ?? ExpressionHelpers.FindFirstSymbol(problem);
        if (target is null) return SolveResult.Failure(problem, "Target variable must be specified or inferable.");

        var name = fn.Name.ToLowerInvariant();
        if (name is not ("lt" or "gt" or "le" or "ge"))
        {
            return SolveResult.Failure(problem, "Unsupported inequality function.");
        }

        var lhs = fn.Arguments[0];
        var rhs = fn.Arguments[1];

        if (TryMonotoneTransform(fn, target, out var transformed, out var guard) && !transformed.InternalEquals(problem))
        {
            var transformedResult = Solve(transformed, context);
            if (!transformedResult.IsSuccess || transformedResult.ResultExpression is null)
            {
                return transformedResult;
            }

            IExpression finalExpr = transformedResult.ResultExpression;
            if (guard is not null)
            {
                finalExpr = new Function("and", ImmutableList.Create<IExpression>(guard, finalExpr)).Canonicalize();
            }
            var trace = context.EnableTracing ? ImmutableList.Create(problem, transformed, finalExpr) : null;
            return SolveResult.Success(finalExpr, "Monotone transform applied.", trace);
        }

        var diff = new Add(lhs, new Multiply(new Number(-1m), rhs).Canonicalize()).Canonicalize();

        if (LinearExtraction.TryExtractLinear(diff, new[] { target }, out var coeffs, out var b))
        {
            var a = coeffs[0];
            if (a == 0m)
            {
                return SolveResult.Failure(problem, "Left side has zero coefficient for target variable.");
            }

            var thresholdExpr = new Divide(new Number(-b), new Number(a)).Canonicalize();
            var flipped = a < 0m ? Flip(name) : name;
            var solved = new Function(flipped, ImmutableList.Create<IExpression>(target, thresholdExpr)).Canonicalize();
            solved = ApplyAssumptions(solved, target, context);
            var trace = context.EnableTracing ? ImmutableList.Create(problem, solved) : null;
            return SolveResult.Success(solved, "Inequality isolated.", trace);
        }

        if (TryExtractQuadratic(diff, target, out var qa, out var qb, out var qc))
        {
            return SolveQuadratic(name, qa, qb, qc, target, problem, context);
        }

        return SolveResult.Failure(problem, "Unsupported inequality form.");
    }

    private IExpression ApplyAssumptions(IExpression solved, Symbol target, SolveContext context)
    {
        // Currently only supporting 'Positive' assumption for intersection.
        if (!context.Assumptions.IsPositive(target.Name)) return solved;

        if (!IntervalSet.TryExtractIntervalSet(solved, target, out var intervals))
        {
            return solved;
        }

        // Assumption: x > 0 -> (0, inf)
        var positive = IntervalSet.Normalize(new[] { new NumericInterval(0m, null) });
        var intersected = IntervalSet.Intersect(intervals, positive);
        return IntervalSet.ToExpression(intersected);
    }

    private static bool IsInequality(IExpression expr)
    {
        if (expr is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            return name is "lt" or "le" or "gt" or "ge";
        }
        return false;
    }

    private static bool IsInequalityContainer(IExpression expr)
    {
        if (IsInequality(expr)) return true;

        if (expr is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            if (name == "interval") return true;
            if (name is "and" or "or" or "not")
            {
                return fn.Arguments.Any(IsInequalityContainer);
            }
        }

        if (expr is Operation op)
        {
            return op.Arguments.Any(IsInequalityContainer);
        }

        return false;
    }

    private SolveResult SolvePiecewise(Piecewise pw, SolveContext context)
    {
        if (pw.Arguments.Count == 0)
        {
            return SolveResult.Failure(pw, "Piecewise requires at least one argument.");
        }

        var newArgs = ImmutableList.CreateBuilder<IExpression>();
        for (int i = 0; i < pw.Arguments.Count; i += 2)
        {
            context.ThrowIfCancellationRequested();
            if (i + 1 < pw.Arguments.Count)
            {
                var value = pw.Arguments[i];
                var guard = pw.Arguments[i + 1];
                var solved = Solve(value, context);
                if (!solved.IsSuccess || solved.ResultExpression is null)
                {
                    return SolveResult.Failure(pw, $"Failed to solve piece {i / 2}. {solved.Message}");
                }
                newArgs.Add(solved.ResultExpression);
                newArgs.Add(guard.Canonicalize());
            }
            else
            {
                // Default case
                var solved = Solve(pw.Arguments[i], context);
                if (!solved.IsSuccess || solved.ResultExpression is null)
                {
                    return SolveResult.Failure(pw, $"Failed to solve default piece. {solved.Message}");
                }
                newArgs.Add(solved.ResultExpression);
            }
        }

        var result = new Piecewise(newArgs.ToImmutable()).Canonicalize();
        var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(pw, result) : null;
        return SolveResult.Success(result, "Piecewise inequality solved per branch.", trace);
    }

    private SolveResult SolveConjunction(Function combo, SolveContext context)
    {
        var parts = FlattenAnd(combo);
        if (parts.Count == 0) return SolveResult.Failure(combo, "Conjunction requires inequalities.");

        var target = context.TargetVariable ?? ExpressionHelpers.FindFirstSymbol(combo);
        if (target is null) return SolveResult.Failure(combo, "Target variable must be specified or inferable.");

        var guards = new List<IExpression>();
        var hasInterval = false;
        var intervalSet = ImmutableList<NumericInterval>.Empty;
        foreach (var p in parts)
        {
            context.ThrowIfCancellationRequested();
            if (!p.ContainsSymbol(target))
            {
                guards.Add(p.Canonicalize());
                continue;
            }

            var solved = Solve(p, context);
            if (!solved.IsSuccess || solved.ResultExpression is null)
            {
                return SolveResult.Failure(combo, "Failed to solve one of the inequalities.");
            }

            var solvedExpr = solved.ResultExpression;
            if (IntervalSet.TryExtractIntervalSet(solvedExpr, target, out var extracted))
            {
                intervalSet = hasInterval ? IntervalSet.Intersect(intervalSet, extracted) : extracted;
                hasInterval = true;
                continue;
            }

            guards.Add(solvedExpr);
        }

        if (!hasInterval)
        {
            intervalSet = IntervalSet.Normalize(new[] { new NumericInterval(null, null) });
        }

        var intervalExpr = IntervalSet.ToExpression(intervalSet);
        if (IsFalse(intervalExpr))
        {
            var traceEmpty = context.EnableTracing ? ImmutableList.Create<IExpression>(combo, intervalExpr) : null;
            return SolveResult.Success(intervalExpr, "Conjoined interval solved.", traceEmpty);
        }

        IExpression resultExpr = guards.Count == 0
            ? intervalExpr
            : new Function("piecewise", ImmutableList.Create<IExpression>(intervalExpr).AddRange(guards)).Canonicalize();

        var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(combo, resultExpr) : null;
        return SolveResult.Success(resultExpr, "Conjoined interval solved.", trace);
    }

    private SolveResult SolveDisjunction(Function combo, SolveContext context)
    {
        var parts = FlattenOr(combo);
        if (parts.Count == 0) return SolveResult.Failure(combo, "Disjunction requires inequalities.");

        var target = context.TargetVariable ?? ExpressionHelpers.FindFirstSymbol(combo);
        if (target is null) return SolveResult.Failure(combo, "Target variable must be specified or inferable.");

        var solvedParts = new List<IExpression>();
        var canUnion = true;
        var union = ImmutableList<NumericInterval>.Empty;

        foreach (var p in parts)
        {
            context.ThrowIfCancellationRequested();
            if (!p.ContainsSymbol(target))
            {
                solvedParts.Add(p.Canonicalize());
                canUnion = false;
                continue;
            }

            var solved = Solve(p, context);
            if (!solved.IsSuccess || solved.ResultExpression is null)
            {
                solvedParts.Add(p);
                canUnion = false;
                continue;
            }

            solvedParts.Add(solved.ResultExpression);

            if (!IntervalSet.TryExtractIntervalSet(solved.ResultExpression, target, out var extracted))
            {
                canUnion = false;
                continue;
            }

            union = IntervalSet.Union(union, extracted);
        }

        if (canUnion)
        {
            var expr = IntervalSet.ToExpression(union);
            var traceUnion = context.EnableTracing ? ImmutableList.Create<IExpression>(combo, expr) : null;
            return SolveResult.Success(expr, "Disjunction converted to interval union.", traceUnion);
        }

        var result = new Function("or", solvedParts.ToImmutableList()).Canonicalize();
        var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(combo, result) : null;
        return SolveResult.Success(result, "Disjunction simplified.", trace);
    }

    private SolveResult SolveNegation(Function combo, SolveContext context)
    {
        if (combo.Arguments.Count != 1)
        {
            return SolveResult.Failure(combo, "Negation requires a single argument.");
        }

        var target = context.TargetVariable ?? ExpressionHelpers.FindFirstSymbol(combo);
        if (target is null) return SolveResult.Failure(combo, "Target variable must be specified or inferable.");

        var inner = combo.Arguments[0];
        var solved = Solve(inner, context);
        if (!solved.IsSuccess || solved.ResultExpression is null)
        {
            return SolveResult.Failure(combo, "Failed to solve negated inequality.");
        }

        var solvedExpr = solved.ResultExpression;
        if (IntervalSet.TryExtractIntervalSet(solvedExpr, target, out var extracted))
        {
            var complemented = IntervalSet.Complement(extracted);
            var resultExpr = IntervalSet.ToExpression(complemented);
            var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(combo, resultExpr) : null;
            return SolveResult.Success(resultExpr, "Negated inequality solved.", trace);
        }

        var fallback = new Function("not", ImmutableList.Create<IExpression>(solvedExpr)).Canonicalize();
        var traceFallback = context.EnableTracing ? ImmutableList.Create<IExpression>(combo, fallback) : null;
        return SolveResult.Success(fallback, "Negated inequality simplified.", traceFallback);
    }

    private bool TryMonotoneTransform(Function inequality, Symbol target, out IExpression transformed, out IExpression? guard)
    {
        transformed = inequality;
        guard = null;

        var cmp = inequality.Name.ToLowerInvariant();
        var lhs = inequality.Arguments[0];
        var rhs = inequality.Arguments[1];

        // Constant factor isolation: k * f(x) ? c -> f(x) ? c/k (if k > 0)
        if (lhs is Multiply mul && mul.Arguments.Count == 2 && 
            mul.Arguments[0] is Number k && k.Value > 0m && 
            mul.Arguments[1].ContainsSymbol(target))
        {
            var nextLhs = mul.Arguments[1];
            var nextRhs = new Divide(rhs, k).Canonicalize();
            transformed = new Function(cmp, ImmutableList.Create(nextLhs, nextRhs)).Canonicalize();
            return true;
        }

        // Monotone increasing functions: exp, log, Pow(base > 1, x)
        if ((lhs is Function || lhs is Power) && ContainsOnlyTarget(lhs, target))
        {
            if (IsMonotoneIncreasing(lhs, target, out var inverse, out var domainGuard))
            {
                transformed = new Function(cmp, ImmutableList.Create(GetPrimaryArg(lhs), inverse(rhs))).Canonicalize();
                guard = domainGuard;
                return true;
            }
        }
        if ((rhs is Function || rhs is Power) && ContainsOnlyTarget(rhs, target))
        {
            if (IsMonotoneIncreasing(rhs, target, out var inverse, out var domainGuard))
            {
                var flipped = Flip(cmp);
                transformed = new Function(flipped, ImmutableList.Create(GetPrimaryArg(rhs), inverse(lhs))).Canonicalize();
                guard = domainGuard;
                return true;
            }
        }

        // Sign normalization: k * expr ? 0
        if (lhs is Multiply mul2 && rhs is Number rn && rn.Value == 0m)
        {
            if (TryGetSign(mul2, target, out var sign, out var remainder))
            {
                var newCmp = sign < 0 ? Flip(cmp) : cmp;
                transformed = new Function(newCmp, ImmutableList.Create(remainder, rhs)).Canonicalize();
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSign(Multiply mul, Symbol target, out int sign, out IExpression remainder)
    {
        sign = 1;
        var args = mul.Arguments;
        var remaining = new List<IExpression>();
        foreach (var a in args)
        {
            if (a is Number n)
            {
                if (n.Value < 0m) sign *= -1;
                remaining.Add(new Number(Math.Abs(n.Value)));
            }
            else
            {
                remaining.Add(a);
            }
        }

        remainder = remaining.Count == 1 ? remaining[0] : new Multiply(remaining.ToImmutableList()).Canonicalize();
        return remainder.ContainsSymbol(target);
    }

    private static bool IsMonotoneIncreasing(IExpression expr, Symbol target, out Func<IExpression, IExpression> inverse, out IExpression? domainGuard)
    {
        inverse = e => e;
        domainGuard = null;

        if (expr is Power p && p.Base is Number b && b.Value > 1m && p.Exponent.ContainsSymbol(target))
        {
            inverse = e => new Function("log", ImmutableList.Create(e, b)).Canonicalize();
            return true;
        }

        if (expr is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();

            if (name == "exp" && fn.Arguments.Count == 1)
            {
                inverse = e => new Function("log", ImmutableList.Create(e)).Canonicalize();
                return true;
            }
            
            if (name == "log")
            {
                if (fn.Arguments.Count == 1)
                {
                    var arg = fn.Arguments[0];
                    inverse = e => new Function("exp", ImmutableList.Create(e)).Canonicalize();
                    domainGuard = new Function("gt", ImmutableList.Create(arg, new Number(0m))).Canonicalize();
                    return true;
                }
                if (fn.Arguments.Count == 2 && fn.Arguments[0] is Number b2 && b2.Value > 1m)
                {
                    var arg = fn.Arguments[1];
                    inverse = e => new Power(b2, e).Canonicalize();
                    domainGuard = new Function("gt", ImmutableList.Create(arg, new Number(0m))).Canonicalize();
                    return true;
                }
            }

            if (name == "pow" && fn.Arguments.Count == 2)
            {
                if (fn.Arguments[0] is Number b3 && b3.Value > 1m && fn.Arguments[1].ContainsSymbol(target))
                {
                    inverse = e => new Function("log", ImmutableList.Create(e, b3)).Canonicalize();
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsOnlyTarget(IExpression expr, Symbol target)
    {
        if (!expr.ContainsSymbol(target)) return false;
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        void Collect(IExpression e)
        {
            if (e is Symbol s) symbols.Add(s.Name);
            else if (e is Operation op) foreach (var a in op.Arguments) Collect(a);
        }
        Collect(expr);
        return symbols.Count == 1 && symbols.First() == target.Name;
    }

    private static IExpression GetPrimaryArg(IExpression expr)
    {
        if (expr is Power p) return p.Exponent;
        if (expr is Function f && f.Arguments.Count > 0) return f.Arguments[0];
        return expr;
    }

    private static List<IExpression> FlattenAnd(Function combo)
    {
        var list = new List<IExpression>();
        foreach (var arg in combo.Arguments)
        {
            if (arg is Function f && f.Name.Equals("and", StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(FlattenAnd(f));
            }
            else
            {
                list.Add(arg);
            }
        }
        return list;
    }

    private static List<IExpression> FlattenOr(Function combo)
    {
        var list = new List<IExpression>();
        foreach (var arg in combo.Arguments)
        {
            if (arg is Function f && f.Name.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(FlattenOr(f));
            }
            else
            {
                list.Add(arg);
            }
        }
        return list;
    }

    private static bool IsFalse(IExpression expr)
    {
        return expr is Symbol s && s.Name.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static string Flip(string name) => name switch
    {
        "lt" => "gt",
        "gt" => "lt",
        "le" => "ge",
        "ge" => "le",
        _ => name
    };

    private static bool TryExtractQuadratic(IExpression expr, Symbol target, out decimal a, out decimal b, out decimal c)
    {
        a = 0m; b = 0m; c = 0m;

        switch (expr)
        {
            case Number n:
                c += n.Value; return true;
            case Symbol s when s.InternalEquals(target):
                b += 1m; return true;
            case Power p when p.Base is Symbol sym && sym.InternalEquals(target) && p.Exponent is Number num:
                if (num.Value == 2m) { a += 1m; return true; }
                if (num.Value == 1m) { b += 1m; return true; }
                return false;
            case Add add:
                foreach (var t in add.Arguments)
                {
                    if (!TryExtractQuadratic(t, target, out var la, out var lb, out var lc)) return false;
                    a += la; b += lb; c += lc;
                }
                return true;
            case Multiply mul:
                decimal coeff = 1m;
                int power = 0;
                foreach (var t in mul.Arguments)
                {
                    if (t is Number num)
                    {
                        coeff *= num.Value;
                        continue;
                    }
                    if (t is Symbol s && s.InternalEquals(target))
                    {
                        power += 1;
                        continue;
                    }
                    if (t is Power pow && pow.Base is Symbol symPow && symPow.InternalEquals(target) && pow.Exponent is Number n2)
                    {
                        if (n2.Value == 2m) power += 2;
                        else if (n2.Value == 1m) power += 1;
                        else return false;
                        continue;
                    }
                    return false;
                }

                if (power == 0) { c += coeff; return true; }
                if (power == 1) { b += coeff; return true; }
                if (power == 2) { a += coeff; return true; }
                return false;
            default:
                return false;
        }
    }

    private SolveResult SolveQuadratic(string cmp, decimal a, decimal b, decimal c, Symbol target, IExpression problem, SolveContext context)
    {
        if (a == 0m)
        {
            return SolveResult.Failure(problem, "Quadratic reduction yielded linear coefficient 0.");
        }

        var disc = b * b - 4m * a * c;
        if (disc < 0m)
        {
            var alwaysPositive = a > 0m;
            var alwaysNegative = a < 0m;
            if (cmp is "lt" or "le")
            {
                return alwaysNegative
                    ? SolveResult.Success(new Function("interval", ImmutableList.Create<IExpression>(MinusInfinity(), Infinity())).Canonicalize(), "Quadratic always below zero.", context.EnableTracing ? ImmutableList.Create(problem) : null)
                    : SolveResult.Failure(problem, "No real roots to satisfy inequality.");
            }
            if (cmp is "gt" or "ge")
            {
                return alwaysPositive
                    ? SolveResult.Success(new Function("interval", ImmutableList.Create<IExpression>(MinusInfinity(), Infinity())).Canonicalize(), "Quadratic always above zero.", context.EnableTracing ? ImmutableList.Create(problem) : null)
                    : SolveResult.Failure(problem, "No real roots to satisfy inequality.");
            }
        }

        var sqrt = (decimal)Math.Sqrt((double)Math.Abs(disc));
        var r1 = (-b - sqrt) / (2m * a);
        var r2 = (-b + sqrt) / (2m * a);
        if (r1 > r2) (r1, r2) = (r2, r1);

        var intervals = cmp switch
        {
            "lt" or "le" when a > 0m => new[] { BuildInterval(r1, r2) },
            "gt" or "ge" when a > 0m => new[] { BuildInterval(null, r1), BuildInterval(r2, null) },
            "lt" or "le" => new[] { BuildInterval(null, r1), BuildInterval(r2, null) },
            "gt" or "ge" => new[] { BuildInterval(r1, r2) },
            _ => Array.Empty<IExpression>()
        };

        if (intervals.Length == 0) return SolveResult.Failure(problem, "Unable to build intervals for quadratic inequality.");

        IExpression expr = intervals.Length == 1
            ? intervals[0]
            : new Function("or", ImmutableList.Create<IExpression>().AddRange(intervals)).Canonicalize();

        expr = ApplyAssumptions(expr, target, context);

        var trace = context.EnableTracing ? ImmutableList.Create(problem, expr) : null;
        return SolveResult.Success(expr, "Quadratic inequality solved to intervals.", trace);
    }

    private static IExpression BuildInterval(decimal? lower, decimal? upper)
    {
        IExpression lowerExpr = lower is null ? MinusInfinity() : new Number(lower.Value);
        IExpression upperExpr = upper is null ? Infinity() : new Number(upper.Value);
        return new Function("interval", ImmutableList.Create<IExpression>(lowerExpr, upperExpr)).Canonicalize();
    }

    private static IExpression MinusInfinity() => new Function("minus_infinity", ImmutableList<IExpression>.Empty).Canonicalize();
    private static IExpression Infinity() => new Function("infinity", ImmutableList<IExpression>.Empty).Canonicalize();
}
