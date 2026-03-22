using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Produces Taylor/Maclaurin expansions and lightweight series compositions.
/// </summary>
public class SeriesExpansionStrategy : ISolverStrategy
{
    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is not SeriesExpansion series) return SolveResult.Failure(problem, "SeriesExpansionStrategy requires a SeriesExpansion expression.");

        var variable = series.Variable;
        var order = series.Order <= 0 ? 1 : series.Order;
        var center = series.Center is Number n ? n.Value : 0m;

        if (!TryExpandPolynomial(series.TargetExpression, variable, center, order, out var poly))
        {
            return SolveResult.Failure(problem, "Series expansion not available for expression.");
        }

        var expansion = poly.ToExpression(variable, center);
        var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(problem, expansion) : null;
        return SolveResult.Success(expansion, "Series expansion completed.", trace);
    }

    internal static bool TryExpandPolynomial(IExpression expr, Symbol variable, decimal center, int order, out SeriesPolynomial series)
    {
        order = order <= 0 ? 1 : order;
        series = ExpandInternal(expr, variable, center, order) ?? default!;
        return series is not null;
    }

    private static SeriesPolynomial? ExpandInternal(IExpression expr, Symbol variable, decimal center, int order)
    {
        switch (expr)
        {
            case Number n:
                return SeriesPolynomial.FromConstant(new Number(n.Value), order);
            case Symbol s:
                if (s.InternalEquals(variable))
                {
                    var sp = SeriesPolynomial.FromConstant(new Number(center), order);
                    sp.AddTerm(1, new Number(1m));
                    return sp;
                }
                return SeriesPolynomial.FromConstant(s, order);
            case Add add:
            {
                var acc = SeriesPolynomial.FromConstant(new Number(0m), order);
                foreach (var arg in add.Arguments)
                {
                    var term = ExpandInternal(arg, variable, center, order);
                    if (term is null) return null;
                    acc = acc.Add(term);
                }
                return acc;
            }
            case Subtract sub:
            {
                var left = ExpandInternal(sub.LeftOperand, variable, center, order);
                var right = ExpandInternal(sub.RightOperand, variable, center, order);
                if (left is null || right is null) return null;
                return left.Add(right.MultiplyConstant(new Number(-1m)));
            }
            case Multiply mul:
            {
                // Canonicalization often rewrites A/B into Multiply(A, Power(B, -1)) or Multiply(A, Power(B, -n)).
                // Handle those by collecting numerator/denominator series and doing a single series division.
                var numerator = SeriesPolynomial.FromConstant(new Number(1m), order);
                SeriesPolynomial? denominator = null;

                foreach (var arg in mul.Arguments)
                {
                    if (arg is Power p && p.Exponent is Number expNum)
                    {
                        var expVal = expNum.Value;
                        var expInt = (int)expVal;
                        if (expVal == expInt && expInt < 0)
                        {
                            var baseSeries = ExpandInternal(p.Base, variable, center, order);
                            if (baseSeries is null) return null;
                            var positivePower = SeriesPolynomial.FromConstant(new Number(1m), order);
                            for (int i = 0; i < -expInt; i++)
                            {
                                positivePower = positivePower.Multiply(baseSeries);
                            }

                            denominator = denominator is null ? positivePower : denominator.Multiply(positivePower);
                            continue;
                        }
                    }

                    var term = ExpandInternal(arg, variable, center, order);
                    if (term is null) return null;
                    numerator = numerator.Multiply(term);
                }

                if (denominator is null) return numerator;
                return numerator.Divide(denominator);
            }
            case Divide div:
            {
                var num = ExpandInternal(div.Numerator, variable, center, order);
                var den = ExpandInternal(div.Denominator, variable, center, order);
                if (num is null || den is null) return null;
                return num.Divide(den);
            }
            case Power pow when pow.Exponent is Number expNum:
            {
                var expVal = expNum.Value;
                if (expVal == Math.Truncate(expVal) && Math.Abs(expVal) <= order + 2)
                {
                    var baseSeries = ExpandInternal(pow.Base, variable, center, order);
                    if (baseSeries is null) return null;

                    var expInt = (int)expVal;
                    if (expInt == 0) return SeriesPolynomial.FromConstant(new Number(1m), order);

                    SeriesPolynomial result = SeriesPolynomial.FromConstant(new Number(1m), order);
                    var factor = baseSeries;
                    var times = Math.Abs(expInt);
                    for (int i = 0; i < times; i++)
                    {
                        result = result.Multiply(factor);
                    }

                    if (expInt > 0) return result;
                    var one = SeriesPolynomial.FromConstant(new Number(1m), order);
                    return one.Divide(result);
                }
                break;
            }
            case Function fn when fn.Arguments.Count == 1:
                return ExpandFunction(fn, variable, center, order);
        }

        if (Polynomial.TryCreate(expr, variable, out var poly))
        {
            return ExpandInternal(poly.ToExpression(variable), variable, center, order);
        }

        return null;
    }

    private static SeriesPolynomial? ExpandFunction(Function fn, Symbol variable, decimal center, int order)
    {
        var name = fn.Name.ToLowerInvariant();
        var arg = fn.Arguments[0];
        if (!TryLinear(arg, variable, out var a, out var b))
        {
            return null;
        }

        var u0 = a * center + b;

        switch (name)
        {
            case "exp":
            {
                var scale = SymCore.NumericConvert.SafeToDecimal(Math.Exp((double)u0));
                return BuildExpSeries(a, order).MultiplyConstant(new Number(scale));
            }
            case "sin":
            {
                var sin0 = SymCore.NumericConvert.SafeToDecimal(Math.Sin((double)u0));
                var cos0 = SymCore.NumericConvert.SafeToDecimal(Math.Cos((double)u0));
                var sinSeries = BuildSinSeries(a, order);
                var cosSeries = BuildCosSeries(a, order);
                return cosSeries.MultiplyConstant(new Number(sin0)).Add(sinSeries.MultiplyConstant(new Number(cos0)));
            }
            case "cos":
            {
                var sin0 = SymCore.NumericConvert.SafeToDecimal(Math.Sin((double)u0));
                var cos0 = SymCore.NumericConvert.SafeToDecimal(Math.Cos((double)u0));
                var sinSeries = BuildSinSeries(a, order);
                var cosSeries = BuildCosSeries(a, order);
                return cosSeries.MultiplyConstant(new Number(cos0)).Add(sinSeries.MultiplyConstant(new Number(-sin0)));
            }
            case "sinh":
            {
                var sinh0 = SymCore.NumericConvert.SafeToDecimal(Math.Sinh((double)u0));
                var cosh0 = SymCore.NumericConvert.SafeToDecimal(Math.Cosh((double)u0));
                var sinhSeries = BuildSinhSeries(a, order);
                var coshSeries = BuildCoshSeries(a, order);
                return coshSeries.MultiplyConstant(new Number(sinh0)).Add(sinhSeries.MultiplyConstant(new Number(cosh0)));
            }
            case "cosh":
            {
                var sinh0 = SymCore.NumericConvert.SafeToDecimal(Math.Sinh((double)u0));
                var cosh0 = SymCore.NumericConvert.SafeToDecimal(Math.Cosh((double)u0));
                var sinhSeries = BuildSinhSeries(a, order);
                var coshSeries = BuildCoshSeries(a, order);
                return coshSeries.MultiplyConstant(new Number(cosh0)).Add(sinhSeries.MultiplyConstant(new Number(sinh0)));
            }
            case "log":
            {
                if (u0 == 0m) return null;
                var constant = new Number(SymCore.NumericConvert.SafeToDecimal(Math.Log((double)u0)));
                var scaled = BuildLog1pSeries(a / u0, order);
                scaled.AddTerm(0, constant);
                return scaled;
            }
        }

        return null;
    }

    private static SeriesPolynomial BuildExpSeries(decimal scale, int order)
    {
        var series = SeriesPolynomial.FromConstant(new Number(0m), order);
        for (int k = 0; k <= order; k++)
        {
            var coeff = Pow(scale, k) / Factorial(k);
            series.AddTerm(k, new Number(coeff));
        }
        return series;
    }

    private static SeriesPolynomial BuildSinSeries(decimal scale, int order)
    {
        var series = SeriesPolynomial.FromConstant(new Number(0m), order);
        var sign = 1m;
        for (int k = 0; k <= order; k++)
        {
            var degree = 2 * k + 1;
            if (degree > order) break;
            var coeff = sign * Pow(scale, degree) / Factorial(degree);
            series.AddTerm(degree, new Number(coeff));
            sign *= -1m;
        }
        return series;
    }

    private static SeriesPolynomial BuildCosSeries(decimal scale, int order)
    {
        var series = SeriesPolynomial.FromConstant(new Number(0m), order);
        var sign = 1m;
        for (int k = 0; k <= order; k++)
        {
            var degree = 2 * k;
            if (degree > order) break;
            var coeff = sign * Pow(scale, degree) / Factorial(degree);
            series.AddTerm(degree, new Number(coeff));
            sign *= -1m;
        }
        return series;
    }

    private static SeriesPolynomial BuildSinhSeries(decimal scale, int order)
    {
        var series = SeriesPolynomial.FromConstant(new Number(0m), order);
        for (int k = 0; k <= order; k++)
        {
            var degree = 2 * k + 1;
            if (degree > order) break;
            var coeff = Pow(scale, degree) / Factorial(degree);
            series.AddTerm(degree, new Number(coeff));
        }
        return series;
    }

    private static SeriesPolynomial BuildCoshSeries(decimal scale, int order)
    {
        var series = SeriesPolynomial.FromConstant(new Number(0m), order);
        for (int k = 0; k <= order; k++)
        {
            var degree = 2 * k;
            if (degree > order) break;
            var coeff = Pow(scale, degree) / Factorial(degree);
            series.AddTerm(degree, new Number(coeff));
        }
        return series;
    }

    private static SeriesPolynomial BuildLog1pSeries(decimal scale, int order)
    {
        var series = SeriesPolynomial.FromConstant(new Number(0m), order);
        for (int k = 1; k <= order; k++)
        {
            var coeff = SymCore.NumericConvert.SafeToDecimal((k % 2 == 0 ? -1.0 : 1.0) / k) * Pow(scale, k);
            series.AddTerm(k, new Number(coeff));
        }
        return series;
    }

    private static bool TryLinear(IExpression expr, Symbol variable, out decimal a, out decimal b)
    {
        a = 0m; b = 0m;
        if (expr is Add add)
        {
            decimal accConst = 0m; decimal accCoeff = 0m;
            foreach (var arg in add.Arguments)
            {
                if (arg is Number n) { accConst += n.Value; continue; }
                if (arg is Multiply m && m.Arguments.Count == 2 && m.Arguments[0] is Number k && m.Arguments[1].InternalEquals(variable))
                {
                    accCoeff += k.Value; continue;
                }
                if (arg.InternalEquals(variable))
                {
                    accCoeff += 1m; continue;
                }
                return false;
            }
            a = accCoeff; b = accConst; return true;
        }
        if (expr.InternalEquals(variable))
        {
            a = 1m; b = 0m; return true;
        }
        if (expr is Multiply mul && mul.Arguments.Count == 2 && mul.Arguments[0] is Number n0 && mul.Arguments[1].InternalEquals(variable))
        {
            a = n0.Value; b = 0m; return true;
        }
        if (expr is Number num)
        {
            a = 0m; b = num.Value; return true;
        }
        return false;
    }

    private static decimal Factorial(int n)
    {
        decimal result = 1m;
        for (int i = 2; i <= n; i++) result *= i;
        return result;
    }

    private static decimal Pow(decimal value, int power) => SymCore.NumericConvert.SafeToDecimal(Math.Pow((double)value, power));
}

internal sealed class SeriesPolynomial
{
    private readonly SortedDictionary<int, IExpression> _terms = new();
    public int Order { get; }

    private SeriesPolynomial(int order)
    {
        Order = Math.Max(0, order);
    }

    public static SeriesPolynomial FromConstant(IExpression value, int order)
    {
        var poly = new SeriesPolynomial(order);
        poly.AddTerm(0, value);
        return poly;
    }

    public void AddTerm(int degree, IExpression coeff)
    {
        if (degree < 0 || degree > Order) return;
        if (IsZero(coeff)) return;

        if (_terms.TryGetValue(degree, out var existing))
        {
            var combined = AddExpr(existing, coeff);
            if (IsZero(combined))
            {
                _terms.Remove(degree);
            }
            else
            {
                _terms[degree] = combined;
            }
        }
        else
        {
            _terms[degree] = coeff;
        }
    }

    public SeriesPolynomial Add(SeriesPolynomial other)
    {
        var result = new SeriesPolynomial(Math.Min(Order, other.Order));
        foreach (var kvp in _terms) result.AddTerm(kvp.Key, kvp.Value);
        foreach (var kvp in other._terms) result.AddTerm(kvp.Key, kvp.Value);
        return result;
    }

    public SeriesPolynomial Multiply(SeriesPolynomial other)
    {
        var result = new SeriesPolynomial(Math.Min(Order, other.Order));
        foreach (var (degA, coeffA) in _terms)
        {
            foreach (var (degB, coeffB) in other._terms)
            {
                var degree = degA + degB;
                if (degree > result.Order) continue;
                result.AddTerm(degree, MultiplyExpr(coeffA, coeffB));
            }
        }
        return result;
    }

    public SeriesPolynomial MultiplyConstant(IExpression constant)
    {
        if (IsZero(constant)) return FromConstant(new Number(0m), Order);
        var result = new SeriesPolynomial(Order);
        foreach (var kvp in _terms)
        {
            result.AddTerm(kvp.Key, MultiplyExpr(kvp.Value, constant));
        }
        return result;
    }

    public SeriesPolynomial? Divide(SeriesPolynomial denominator)
    {
        var denLead = denominator.LowestDegree();
        var numLead = LowestDegree();
        if (denLead is null || numLead is null) return null;
        if (numLead.Value < denLead.Value) return null;

        var shiftedNum = Shift(-denLead.Value);
        var shiftedDen = denominator.Shift(-denLead.Value);

        if (!shiftedDen.TryGetCoefficient(0, out var den0) || den0 is not Number dn || dn.Value == 0m) return null;
        var invDen0 = new Number(1m / dn.Value);

        var maxOrder = Order - denLead.Value;
        var quotient = new SeriesPolynomial(Math.Max(0, maxOrder));
        for (int k = 0; k <= quotient.Order; k++)
        {
            var target = shiftedNum.CoefficientOrZero(k);
            IExpression accum = new Number(0m);
            for (int i = 1; i <= k; i++)
            {
                if (shiftedDen.TryGetCoefficient(i, out var dk) && quotient.TryGetCoefficient(k - i, out var q))
                {
                    accum = AddExpr(accum, MultiplyExpr(dk, q));
                }
            }

            var numerator = SubtractExpr(target, accum);
            var termCoeff = MultiplyExpr(numerator, invDen0);
            quotient.AddTerm(k, termCoeff);
        }

        return quotient;
    }

    public SeriesPolynomial Shift(int delta)
    {
        var result = new SeriesPolynomial(Math.Max(0, Order - delta));
        foreach (var kvp in _terms)
        {
            var newDeg = kvp.Key + delta;
            if (newDeg < 0 || newDeg > result.Order) continue;
            result.AddTerm(newDeg, kvp.Value);
        }
        return result;
    }

    public int? LowestDegree()
    {
        return _terms.Count == 0 ? null : _terms.Keys.Min();
    }

    public bool TryGetCoefficient(int degree, out IExpression coeff) => _terms.TryGetValue(degree, out coeff!);

    public IExpression CoefficientOrZero(int degree) => TryGetCoefficient(degree, out var c) ? c : new Number(0m);

    private static IExpression AddExpr(IExpression a, IExpression b)
    {
        if (IsZero(a)) return b;
        if (IsZero(b)) return a;
        return new Add(a, b).Canonicalize();
    }

    private static IExpression SubtractExpr(IExpression a, IExpression b)
    {
        if (IsZero(b)) return a;
        return new Add(a, new Multiply(new Number(-1m), b).Canonicalize()).Canonicalize();
    }

    private static IExpression MultiplyExpr(IExpression a, IExpression b)
    {
        if (IsZero(a) || IsZero(b)) return new Number(0m);
        return new Multiply(a, b).Canonicalize();
    }

    private static bool IsZero(IExpression expr) => expr is Number n && n.Value == 0m;

    public IExpression ToExpression(Symbol variable, decimal center)
    {
        if (_terms.Count == 0) return new Number(0m);

        // Emit as a polynomial in the original variable (expand (x-center)^n), so callers/tests don't depend on
        // formatting choices like Pow(-1 + x, 2).
        var terms = ImmutableList.CreateBuilder<IExpression>();
        foreach (var kvp in _terms)
        {
            var degree = kvp.Key;
            var coeff = kvp.Value;
            if (degree == 0)
            {
                terms.Add(coeff);
                continue;
            }

            if (center == 0m)
            {
                var power = degree == 1 ? variable : new Power(variable, new Number(degree)).Canonicalize();
                terms.Add(new Multiply(coeff, power).Canonicalize());
                continue;
            }

            // (x - c)^n = sum_{k=0..n} choose(n,k) * x^k * (-c)^(n-k)
            var expanded = ImmutableList.CreateBuilder<IExpression>();
            for (int k = 0; k <= degree; k++)
            {
                var binom = Binomial(degree, k);
                var scale = binom * PowDecimal(-center, degree - k);
                if (scale == 0m) continue;

                IExpression xPow = k switch
                {
                    0 => new Number(1m),
                    1 => variable,
                    _ => new Power(variable, new Number(k)).Canonicalize()
                };

                expanded.Add(new Multiply(new Number(scale), xPow).Canonicalize());
            }

            var powerExpanded = expanded.Count switch
            {
                0 => (IExpression)new Number(0m),
                1 => expanded[0],
                _ => new Add(expanded.ToImmutable()).Canonicalize()
            };

            terms.Add(new Multiply(coeff, powerExpanded).Canonicalize());
        }

        return terms.Count == 1 ? terms[0] : new Add(terms.ToImmutable()).Canonicalize();
    }

    private static decimal PowDecimal(decimal value, int power) => SymCore.NumericConvert.SafeToDecimal(Math.Pow((double)value, power));

    private static decimal Binomial(int n, int k)
    {
        if (k < 0 || k > n) return 0m;
        if (k == 0 || k == n) return 1m;
        k = Math.Min(k, n - k);
        decimal result = 1m;
        for (int i = 1; i <= k; i++)
        {
            result = result * (n - (k - i)) / i;
        }
        return result;
    }
}
