using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Evaluates limits using substitution, L'Hôpital, dominant term analysis, and series expansions.
/// </summary>
public class LimitStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "LimitStrategy";
    private readonly RulePackStrategy _rulePackStrategy;
    private const int MaxHopitalDepth = 4;

    public LimitStrategy()
    {
        var packs = SymRules.RulePackLibrary.GetRulePacks();
        var pack = packs.FirstOrDefault(p => p.Name == "LimitStrategy");
        if (pack != null) _rulePackStrategy = new RulePackStrategy(pack);
        else _rulePackStrategy = null!;
    }

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is not Limit limit) return SolveResult.Failure(problem, "LimitStrategy requires a Limit expression.");

        // 1. Try rules first
        if (_rulePackStrategy != null)
        {
            var res = _rulePackStrategy.Solve(limit, context);
            if (res.IsSuccess && res.ResultExpression != null && !res.ResultExpression.InternalEquals(limit))
            {
                 // If the result is a number or infinity, we are done.
                 if (res.ResultExpression is Number or Function) return res;
                 // Otherwise continue with current as result
            }
        }

        if (!TryGetApproach(limit, out var kind, out var approach))
        {
            return SolveResult.Failure(problem, "Unable to resolve limit approach value.");
        }

        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            Console.WriteLine($"DEBUG: LimitStrategy evaluating {problem.ToDisplayString()} kind={kind} approach={approach}");

        var result = EvaluateLimit(limit.TargetExpression, limit.Variable, kind, approach, 0);
        if (result is null)
        {
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine($"DEBUG: LimitStrategy FAILED for {problem.ToDisplayString()}");
            return SolveResult.Failure(problem, "Unable to evaluate limit.");
        }

        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            Console.WriteLine($"DEBUG: LimitStrategy SUCCEEDED for {problem.ToDisplayString()} -> {result.ToDisplayString()}");

        var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(problem, result) : null;
        return SolveResult.Success(result, "Limit evaluated.", trace);
    }

    private static IExpression? EvaluateLimit(IExpression expr, Symbol variable, ApproachKind kind, decimal approach, int depth)
    {
        if (depth > MaxHopitalDepth) return null;

        return kind switch
        {
            ApproachKind.Finite => EvaluateFinite(expr, variable, approach, depth),
            _ => EvaluateInfinite(expr, variable, kind)
        };
    }

    private static IExpression? EvaluateFinite(IExpression target, Symbol variable, decimal center, int depth)
    {
        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            Console.WriteLine($"DEBUG: EvaluateFinite target={target.ToDisplayString()} center={center} depth={depth}");

        // Direct evaluation
        var assignments = new Dictionary<string, decimal> { [variable.Name] = center };
        if (NumericEvaluator.TryEvaluate(target, assignments, out var value, out var evalErr))
        {
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine($"DEBUG: EvaluateFinite direct success: {value}");
            return new Number(value);
        }

        // Try series
        if (TrySeriesLimit(target, variable, center, out var seriesVal))
        {
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine($"DEBUG: EvaluateFinite series success: {seriesVal}");
            return new Number(seriesVal);
        }

        // L'Hopital
        if (depth < 3 && target is Divide div)
        {
            var numOk = NumericEvaluator.TryEvaluate(div.Numerator, assignments, out var numVal, out _);
            var denOk = NumericEvaluator.TryEvaluate(div.Denominator, assignments, out var denVal, out _);
            var indeterminateZero = numOk && denOk && Math.Abs((double)numVal) < 1e-8 && Math.Abs((double)denVal) < 1e-8;

            if (indeterminateZero || (!numOk && !denOk))
            {
                var numPrime = CalculusHelper.DifferentiateExpression(div.Numerator, variable);
                var denPrime = CalculusHelper.DifferentiateExpression(div.Denominator, variable);
                if (numPrime is not null && denPrime is not null)
                {
                    var next = new Divide(numPrime, denPrime).Canonicalize();
                    return EvaluateFinite(next, variable, center, depth + 1);
                }
            }
        }

        if (ProbeSymmetric(target, variable, center, out var probed))
        {
            return new Number(probed);
        }

        return null;
    }

    private static IExpression? EvaluateInfinite(IExpression expr, Symbol variable, ApproachKind kind)
    {
        if (TryDominantPolynomial(expr, variable, kind, out var dominant))
        {
            return dominant;
        }

        return null;
    }

    private static bool TrySeriesLimit(IExpression expr, Symbol variable, decimal center, out decimal value)
    {
        value = 0m;
        const int order = 6;

        if (expr is Divide d &&
            SeriesExpansionStrategy.TryExpandPolynomial(d.Numerator, variable, center, order, out var numSeries) &&
            SeriesExpansionStrategy.TryExpandPolynomial(d.Denominator, variable, center, order, out var denSeries) &&
            TrySeriesLeading(numSeries, out var numDeg, out var numCoeff) &&
            TrySeriesLeading(denSeries, out var denDeg, out var denCoeff) &&
            denCoeff != 0m)
        {
            var gap = numDeg - denDeg;
            if (gap > 0)
            {
                value = 0m;
                return true;
            }
            if (gap == 0)
            {
                value = numCoeff / denCoeff;
                return true;
            }
            return false;
        }

        if (SeriesExpansionStrategy.TryExpandPolynomial(expr, variable, center, order, out var series) &&
            TrySeriesLeading(series, out var deg, out var coeff))
        {
            value = deg > 0 ? 0m : coeff;
            return true;
        }

        return false;
    }

    private static bool TrySeriesLeading(SeriesPolynomial series, out int degree, out decimal coeff)
    {
        degree = 0;
        coeff = 0m;
        var lead = series.LowestDegree();
        if (lead is null) return false;
        if (!series.TryGetCoefficient(lead.Value, out var exprCoeff)) return false;
        if (!TryNumericCoefficient(exprCoeff, out coeff)) return false;
        degree = lead.Value;
        return true;
    }

    private static bool ProbeSymmetric(IExpression expr, Symbol variable, decimal approach, out decimal value)
    {
        value = 0m;
        var deltas = new[] { 1e-3m, 1e-4m, 1e-5m };
        var assignments = new Dictionary<string, decimal> { { variable.Name, approach } };

        foreach (var d in deltas)
        {
            var samples = new List<decimal>();
            foreach (var offset in new[] { -d, d })
            {
                assignments[variable.Name] = approach + offset;
                if (NumericEvaluator.TryEvaluate(expr, assignments, out var s, out _))
                {
                    samples.Add(s);
                }
            }
            if (samples.Count == 2)
            {
                value = (samples[0] + samples[1]) / 2m;
                return true;
            }
        }

        return false;
    }

    private static bool TryDominantPolynomial(IExpression expr, Symbol variable, ApproachKind kind, out IExpression result)
    {
        result = null!;
        var approachSign = kind == ApproachKind.MinusInfinity ? -1 : 1;

        if (TryExtractRationalPolynomials(expr, variable, out var numPoly, out var denPoly))
        {
            var gap = numPoly.Degree - denPoly.Degree;
            var numLead = numPoly.LeadingCoefficient;
            var denLead = denPoly.LeadingCoefficient;

            if (gap < 0)
            {
                result = new Number(0m);
                return true;
            }

            if (gap == 0)
            {
                var ratio = ToDecimal(numLead) / ToDecimal(denLead);
                result = new Number(ratio);
                return true;
            }

            var sign = Math.Sign(ToDecimal(numLead) * ToDecimal(denLead)) * (gap % 2 == 0 ? 1 : approachSign);
            result = InfinityExpr(sign < 0);
            return true;
        }

        if (expr is Divide d &&
            Polynomial.TryCreate(d.Numerator, variable, out var numPoly2) &&
            Polynomial.TryCreate(d.Denominator, variable, out var denPoly2))
        {
            var gap = numPoly2.Degree - denPoly2.Degree;
            var numLead = numPoly2.LeadingCoefficient;
            var denLead = denPoly2.LeadingCoefficient;

            if (gap < 0)
            {
                result = new Number(0m);
                return true;
            }

            if (gap == 0)
            {
                var ratio = ToDecimal(numLead) / ToDecimal(denLead);
                result = new Number(ratio);
                return true;
            }

            var sign = Math.Sign(ToDecimal(numLead) * ToDecimal(denLead)) * (gap % 2 == 0 ? 1 : approachSign);
            result = InfinityExpr(sign < 0);
            return true;
        }

        if (Polynomial.TryCreate(expr, variable, out var poly))
        {
            if (poly.Degree == 0)
            {
                result = poly.ToExpression(variable);
                return true;
            }

            var lead = ToDecimal(poly.LeadingCoefficient);
            var sign = Math.Sign(lead) * (poly.Degree % 2 == 0 ? 1 : approachSign);
            result = InfinityExpr(sign < 0);
            return true;
        }

        return false;
    }

    private static bool TryExtractRationalPolynomials(IExpression expr, Symbol variable, out Polynomial numerator, out Polynomial denominator)
    {
        numerator = Polynomial.Zero;
        denominator = Polynomial.One;

        if (expr is Divide div)
        {
            return Polynomial.TryCreate(div.Numerator, variable, out numerator) && Polynomial.TryCreate(div.Denominator, variable, out denominator);
        }

        if (expr is Multiply mul)
        {
            var numFactors = new List<IExpression>();
            var denFactors = new List<IExpression>();

            foreach (var factor in mul.Arguments)
            {
                if (factor is Power p && p.Exponent is Number e)
                {
                    var expVal = e.Value;
                    var expInt = (int)expVal;
                    if (expVal == expInt && expInt < 0)
                    {
                        denFactors.Add(-expInt == 1
                            ? p.Base
                            : new Power(p.Base, new Number(-expInt)).Canonicalize());
                        continue;
                    }
                }

                numFactors.Add(factor);
            }

            if (denFactors.Count == 0) return false;

            var numExpr = numFactors.Count switch
            {
                0 => (IExpression)new Number(1m),
                1 => numFactors[0],
                _ => new Multiply(numFactors.ToImmutableList()).Canonicalize()
            };

            var denExpr = denFactors.Count switch
            {
                1 => denFactors[0],
                _ => new Multiply(denFactors.ToImmutableList()).Canonicalize()
            };

            return Polynomial.TryCreate(numExpr, variable, out numerator) && Polynomial.TryCreate(denExpr, variable, out denominator);
        }

        if (expr is Power pow && pow.Exponent is Number expNum)
        {
            var expVal = expNum.Value;
            var expInt = (int)expVal;
            if (expVal == expInt && expInt < 0)
            {
                var denExpr = -expInt == 1
                    ? pow.Base
                    : new Power(pow.Base, new Number(-expInt)).Canonicalize();
                return Polynomial.TryCreate(new Number(1m), variable, out numerator) && Polynomial.TryCreate(denExpr, variable, out denominator);
            }
        }

        return false;
    }

    private static decimal ToDecimal(Rational r) => (decimal)r.Numerator / (decimal)r.Denominator;

    private static bool TryNumericCoefficient(IExpression expr, out decimal value)
    {
        if (expr is Number n)
        {
            value = n.Value;
            return true;
        }

        return NumericEvaluator.TryEvaluate(expr, new Dictionary<string, decimal>(), out value, out _);
    }

    private static bool TryGetApproach(Limit limit, out ApproachKind kind, out decimal approach)
    {
        if (limit.Approach is Function fn && fn.Arguments.Count == 0)
        {
            if (fn.Name.Equals("infinity", StringComparison.OrdinalIgnoreCase))
            {
                kind = ApproachKind.Infinity;
                approach = 0m;
                return true;
            }
            if (fn.Name.Equals("minus_infinity", StringComparison.OrdinalIgnoreCase))
            {
                kind = ApproachKind.MinusInfinity;
                approach = 0m;
                return true;
            }
        }

        if (NumericEvaluator.TryEvaluate(limit.Approach, new Dictionary<string, decimal>(), out approach, out _))
        {
            kind = ApproachKind.Finite;
            return true;
        }

        kind = ApproachKind.Finite;
        approach = 0m;
        return false;
    }

    private static bool IsSinOverX(IExpression expr, Symbol x)
    {
        if (expr is Divide d)
        {
            return d.Numerator is Function fn
                && fn.Name.Equals("sin", StringComparison.OrdinalIgnoreCase)
                && fn.Arguments.Count == 1
                && fn.Arguments[0].InternalEquals(x)
                && d.Denominator.InternalEquals(x);
        }

        if (expr is Multiply m)
        {
            bool hasSin = false;
            bool hasInvX = false;
            foreach (var arg in m.Arguments)
            {
                if (arg is Function fn
                    && fn.Name.Equals("sin", StringComparison.OrdinalIgnoreCase)
                    && fn.Arguments.Count == 1
                    && fn.Arguments[0].InternalEquals(x))
                {
                    hasSin = true;
                    continue;
                }

                if (arg is Power p && p.Base.InternalEquals(x) && p.Exponent is Number n && n.Value == -1m)
                {
                    hasInvX = true;
                    continue;
                }

                if (arg is Number num && num.Value == 1m)
                {
                    continue;
                }

                return false;
            }

            return hasSin && hasInvX;
        }

        return false;
    }

    private static bool IsExpMinusOneOverX(IExpression expr, Symbol x)
    {
        if (expr is Divide d)
        {
            return d.Denominator.InternalEquals(x) && IsExpMinusOne(d.Numerator, x);
        }

        if (expr is Multiply m)
        {
            Power? invX = null;
            IExpression? numerator = null;

            foreach (var arg in m.Arguments)
            {
                if (arg is Power p && p.Base.InternalEquals(x) && p.Exponent is Number n && n.Value == -1m)
                {
                    invX = p;
                    continue;
                }

                if (arg is Number num && num.Value == 1m)
                {
                    continue;
                }

                numerator = numerator is null ? arg : new Multiply(numerator, arg).Canonicalize();
            }

            return invX is not null && numerator is not null && IsExpMinusOne(numerator, x);
        }

        return false;
    }

    private static bool IsExpMinusOne(IExpression expr, Symbol x)
    {
        if (expr is Add add && add.Arguments.Count == 2)
        {
            Function? exp = null;
            Number? minusOne = null;
            foreach (var arg in add.Arguments)
            {
                if (arg is Function fn && fn.Name.Equals("exp", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1 && fn.Arguments[0].InternalEquals(x))
                {
                    exp = fn;
                    continue;
                }
                if (arg is Number n && n.Value == -1m)
                {
                    minusOne = n;
                    continue;
                }
            }
            return exp is not null && minusOne is not null;
        }
        return false;
    }

    private static IExpression InfinityExpr(bool negative) =>
        new Function(negative ? "minus_infinity" : "infinity", ImmutableList<IExpression>.Empty).Canonicalize();

    private enum ApproachKind
    {
        Finite,
        Infinity,
        MinusInfinity
    }
}
