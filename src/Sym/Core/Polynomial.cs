// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace Sym.Core;

/// <summary>
/// Univariate polynomial with exact rational coefficients (lowest degree first).
/// </summary>
public sealed class Polynomial
{
    private readonly ImmutableArray<Rational> _coeffs;

    public IReadOnlyList<Rational> Coefficients => _coeffs;
    public int Degree => _coeffs.Length - 1;
    public bool IsZero => _coeffs.All(c => c.IsZero);
    public Rational LeadingCoefficient => _coeffs[^1];

    public static Polynomial Zero { get; } = new(new[] { Rational.Zero });
    public static Polynomial One { get; } = new(new[] { Rational.One });

    private Polynomial(IEnumerable<Rational> coefficients)
    {
        _coeffs = Trim(coefficients);
    }

    public Polynomial MakeMonic()
    {
        if (IsZero) return this;
        var lead = LeadingCoefficient;
        if (lead.IsOne) return this;
        var inv = lead.Reciprocal();
        return new Polynomial(_coeffs.Select(c => c * inv));
    }

    public static Polynomial Gcd(Polynomial a, Polynomial b, CancellationToken ct = default)
    {
        if (a.IsZero) return b.MakeMonic();
        if (b.IsZero) return a.MakeMonic();

        var first = a;
        var second = b;

        while (!second.IsZero)
        {
            ct.ThrowIfCancellationRequested();
            var (_, remainder) = first.DivideWithRemainder(second);
            first = second;
            second = remainder;
        }

        return first.MakeMonic();
    }

    public static bool TryCreate(IExpression expr, Symbol variable, out Polynomial polynomial)
    {
        if (TryCreateInternal(expr.Canonicalize(), variable, out polynomial))
        {
            polynomial = new Polynomial(polynomial._coeffs);
            return true;
        }

        polynomial = Zero;
        return false;
    }

    private static bool TryCreateInternal(IExpression expr, Symbol variable, out Polynomial polynomial)
    {
        switch (expr)
        {
            case Number n:
                polynomial = new Polynomial(new[] { Rational.FromDecimal(n.Value) });
                return true;
            case Symbol s:
                if (s.InternalEquals(variable))
                {
                    polynomial = new Polynomial(new[] { Rational.Zero, Rational.One });
                    return true;
                }
                // Treat other symbols as constants (degree 0) for the purpose of univariate polynomial isolation.
                polynomial = new Polynomial(new[] { Rational.One }); // Placeholder for symbol as constant
                // Wait, if it's a constant, it should be the symbol itself. 
                // But this Polynomial class only supports Rational coefficients.
                // If we want to support symbolic coefficients, we need a different structure.
                // For now, let's keep it failing if it's not the target, 
                // BUT the callers like EquationSolverStrategy should be updated to handle constants.
                polynomial = Zero;
                return false;
            case Subtract sub:
            {
                if (!TryCreateInternal(sub.LeftOperand, variable, out var leftPoly)) { polynomial = Zero; return false; }
                if (!TryCreateInternal(sub.RightOperand, variable, out var rightPoly)) { polynomial = Zero; return false; }
                polynomial = leftPoly - rightPoly;
                return true;
            }
            case Add add:
            {
                Polynomial acc = Zero;
                foreach (var term in add.Arguments)
                {
                    if (!TryCreateInternal(term, variable, out var p)) { polynomial = Zero; return false; }
                    acc += p;
                }
                polynomial = acc;
                return true;
            }
            case Multiply mult:
            {
                Polynomial acc = One;
                foreach (var factor in mult.Arguments)
                {
                    if (factor is Number numFactor)
                    {
                        acc = acc.Multiply(Rational.FromDecimal(numFactor.Value));
                        continue;
                    }

                    if (factor is Divide divFactor && divFactor.Numerator is Number nLeft && divFactor.Denominator is Number nRight)
                    {
                        acc = acc.Multiply(Rational.FromDecimal(nLeft.Value) / Rational.FromDecimal(nRight.Value));
                        continue;
                    }

                    if (!TryCreateInternal(factor, variable, out var p)) { polynomial = Zero; return false; }
                    acc *= p;
                }
                polynomial = acc;
                return true;
            }
            case Power pow:
            {
                if (pow.Base is Symbol sym && sym.InternalEquals(variable) && pow.Exponent is Number numExp)
                {
                    var asInt = (int)numExp.Value;
                    if (numExp.Value == asInt && asInt >= 0)
                    {
                        var coeffs = Enumerable.Repeat(Rational.Zero, asInt + 1).ToArray();
                        coeffs[asInt] = Rational.One;
                        polynomial = new Polynomial(coeffs);
                        return true;
                    }
                }

                if (pow.Exponent is Number numExpGeneric)
                {
                    var asInt = (int)numExpGeneric.Value;
                    if (numExpGeneric.Value == asInt && asInt >= 0 && TryCreateInternal(pow.Base, variable, out var basePoly))
                    {
                        var acc = One;
                        for (int i = 0; i < asInt; i++)
                        {
                            acc *= basePoly;
                        }
                        polynomial = acc;
                        return true;
                    }
                }

                polynomial = Zero;
                return false;
            }
            case Divide div:
            {
                if (div.Denominator is Number denomNum)
                {
                    if (!TryCreateInternal(div.Numerator, variable, out var numeratorPoly)) { polynomial = Zero; return false; }
                    polynomial = numeratorPoly.Multiply(Rational.One / Rational.FromDecimal(denomNum.Value));
                    return true;
                }
                polynomial = Zero;
                return false;
            }
            default:
                polynomial = Zero;
                return false;
        }
    }

    public static Polynomial operator +(Polynomial a, Polynomial b)
    {
        var length = Math.Max(a._coeffs.Length, b._coeffs.Length);
        var result = new Rational[length];
        for (int i = 0; i < length; i++)
        {
            var av = i < a._coeffs.Length ? a._coeffs[i] : Rational.Zero;
            var bv = i < b._coeffs.Length ? b._coeffs[i] : Rational.Zero;
            result[i] = av + bv;
        }
        return new Polynomial(result);
    }

    public static Polynomial operator -(Polynomial a, Polynomial b)
    {
        var length = Math.Max(a._coeffs.Length, b._coeffs.Length);
        var result = new Rational[length];
        for (int i = 0; i < length; i++)
        {
            var av = i < a._coeffs.Length ? a._coeffs[i] : Rational.Zero;
            var bv = i < b._coeffs.Length ? b._coeffs[i] : Rational.Zero;
            result[i] = av - bv;
        }
        return new Polynomial(result);
    }

    public static Polynomial operator *(Polynomial a, Polynomial b) => a.Multiply(b);

    public Polynomial Multiply(Rational scalar)
    {
        var scaled = _coeffs.Select(c => c * scalar);
        return new Polynomial(scaled);
    }

    public Polynomial Multiply(Polynomial other)
    {
        var result = new Rational[_coeffs.Length + other._coeffs.Length - 1];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Rational.Zero;
        }
        for (int i = 0; i < _coeffs.Length; i++)
        {
            for (int j = 0; j < other._coeffs.Length; j++)
            {
                result[i + j] = result[i + j] + _coeffs[i] * other._coeffs[j];
            }
        }
        return new Polynomial(result);
    }

    public (Polynomial Quotient, Polynomial Remainder) DivideWithRemainder(Polynomial divisor)
    {
        if (divisor._coeffs.All(c => c.IsZero))
        {
            throw new DivideByZeroException("Divisor polynomial cannot be zero.");
        }

        // Constant divisor: divide coefficients directly.
        if (divisor._coeffs.Length == 1)
        {
            var d = divisor._coeffs[0];
            var quotientCoeffs = _coeffs.Select(c => c / d);
            return (new Polynomial(quotientCoeffs), Zero);
        }

        var dividend = _coeffs.ToArray();
        var divisorCoeffs = divisor._coeffs.ToArray();
        var quotient = new Rational[Math.Max(1, dividend.Length - divisorCoeffs.Length + 1)];
        for (int i = 0; i < quotient.Length; i++)
        {
            quotient[i] = Rational.Zero;
        }
        var remainder = dividend.ToArray();

        for (int i = remainder.Length - 1; i >= divisorCoeffs.Length - 1; i--)
        {
            var leadDiv = divisorCoeffs[^1];
            if (leadDiv.IsZero) break;
            var coeff = remainder[i] / leadDiv;
            var qIndex = i - divisorCoeffs.Length + 1;
            quotient[qIndex] = coeff;
            for (int j = 0; j < divisorCoeffs.Length; j++)
            {
                remainder[qIndex + j] = remainder[qIndex + j] - coeff * divisorCoeffs[j];
            }
        }

        var remainderLength = Math.Max(1, divisorCoeffs.Length - 1);
        var remainderTrimmed = remainder.Take(remainderLength);
        return (new Polynomial(quotient), new Polynomial(remainderTrimmed));
    }

    public Polynomial Derivative()
    {
        if (_coeffs.Length == 1) return Zero;
        var derived = new Rational[_coeffs.Length - 1];
        for (int i = 1; i < _coeffs.Length; i++)
        {
            derived[i - 1] = _coeffs[i] * Rational.FromInteger(i);
        }
        return new Polynomial(derived);
    }

    public Rational Evaluate(Rational x)
    {
        // Horner's method, high-degree to low-degree
        var acc = _coeffs[_coeffs.Length - 1];
        for (int i = _coeffs.Length - 2; i >= 0; i--)
        {
            acc = acc * x + _coeffs[i];
        }
        return acc;
    }

    public FactorizationResult FactorLinear(CancellationToken ct = default)
    {
        var normalized = NormalizeContent(out var content);
        var working = normalized;
        var roots = new List<Rational>();

        while (working.Degree > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (!working.TryFindRationalRoot(out var root, ct)) break;
            roots.Add(root);
            working = working.DivideByLinear(root);
        }

        return new FactorizationResult(content, roots, working);
    }

    private Polynomial NormalizeContent(out Rational content)
    {
        var denominatorsLcm = _coeffs.Aggregate(BigInteger.One, (acc, c) => Lcm(acc, c.Denominator));
        if (denominatorsLcm.IsZero) denominatorsLcm = BigInteger.One;

        var scaled = _coeffs
            .Select(c => c.Numerator * (denominatorsLcm / c.Denominator))
            .ToList();

        var coeffGcd = scaled.Aggregate(BigInteger.Zero, (acc, val) =>
        {
            if (acc.IsZero) return BigInteger.Abs(val);
            return BigInteger.GreatestCommonDivisor(acc, BigInteger.Abs(val));
        });

        if (coeffGcd.IsZero) coeffGcd = BigInteger.One;

        content = new Rational(coeffGcd, denominatorsLcm);

        var normalized = scaled.Select(v => new Rational(v / coeffGcd, BigInteger.One));
        return new Polynomial(normalized);
    }

    private bool TryFindRationalRoot(out Rational root, CancellationToken ct = default)
    {
        // Handle zero constant term quickly.
        if (_coeffs[0].IsZero)
        {
            root = Rational.Zero;
            return true;
        }

        var denominatorsLcm = _coeffs.Aggregate(BigInteger.One, (acc, c) => Lcm(acc, c.Denominator));
        var integers = _coeffs
            .Select(c => c.Numerator * (denominatorsLcm / c.Denominator))
            .ToList();

        var leading = integers[^1];
        var constant = integers[0];

        if (leading.IsZero)
        {
            root = Rational.Zero;
            return false;
        }

        var pCandidates = GetIntegerDivisors(constant, ct).ToList();
        var qCandidates = GetIntegerDivisors(leading, ct).ToList();

        int checkCount = 0;
        const int MaxChecks = 20000;

        foreach (var p in pCandidates)
        {
            foreach (var q in qCandidates)
            {
                ct.ThrowIfCancellationRequested();
                if (++checkCount > MaxChecks)
                {
                    root = Rational.Zero;
                    return false;
                }

                if (q.IsZero) continue;
                var candidate = new Rational(p, q);
                if (Evaluate(candidate).IsZero)
                {
                    root = candidate;
                    return true;
                }
                var negCandidate = new Rational(BigInteger.Negate(p), q);
                if (Evaluate(negCandidate).IsZero)
                {
                    root = negCandidate;
                    return true;
                }
            }
        }

        root = Rational.Zero;
        return false;
    }

    private Polynomial DivideByLinear(Rational root)
    {
        // Synthetic division using coefficients in descending order.
        var a = _coeffs.Reverse().ToArray();
        var b = new Rational[a.Length - 1];

        b[0] = a[0];
        for (int i = 1; i < a.Length - 1; i++)
        {
            b[i] = a[i] + root * b[i - 1];
        }

        var quotientHighToLow = b.ToList();
        quotientHighToLow.Reverse();
        return new Polynomial(quotientHighToLow);
    }

    private static BigInteger Lcm(BigInteger a, BigInteger b)
    {
        if (a.IsZero || b.IsZero) return BigInteger.Zero;
        return BigInteger.Abs(a / BigInteger.GreatestCommonDivisor(a, b) * b);
    }

    private static IEnumerable<BigInteger> GetIntegerDivisors(BigInteger value, CancellationToken ct = default)
    {
        value = BigInteger.Abs(value);
        if (value.IsZero)
        {
            yield return BigInteger.Zero;
            yield break;
        }

        var max = Sqrt(value);
        int count = 0;
        for (BigInteger i = BigInteger.One; i <= max; i++)
        {
            if (count % 100 == 0) ct.ThrowIfCancellationRequested();

            if (value % i == 0)
            {
                yield return i;
                var paired = value / i;
                if (paired != i) yield return paired;
            }
            
            if (++count > 50000) break; // Safety limit to prevent hangs on large coefficients
        }
    }

    private static BigInteger Sqrt(BigInteger n)
    {
        if (n <= 0) return BigInteger.Zero;

        BigInteger low = BigInteger.Zero;
        BigInteger high = BigInteger.One;

        // Exponentially expand high bound until high^2 > n.
        while (high * high <= n)
        {
            low = high;
            high <<= 1;
        }

        while (high - low > 1)
        {
            var mid = (high + low) >> 1;
            if (mid * mid <= n)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private static ImmutableArray<Rational> Trim(IEnumerable<Rational> source)
    {
        var list = source.ToList();
        var last = list.Count - 1;
        while (last > 0 && list[last].IsZero)
        {
            last--;
        }
        return list.Take(last + 1).ToImmutableArray();
    }

    public IExpression ToExpression(Symbol variable)
    {
        var terms = new List<IExpression>();
        for (int i = _coeffs.Length - 1; i >= 0; i--)
        {
            var coeff = _coeffs[i];
            if (coeff.IsZero) continue;
            IExpression term = coeff.ToExpression();
            if (i > 0)
            {
                var power = i == 1 ? variable : new Power(variable, new Number(i)).Canonicalize();
                term = new Multiply(term, power).Canonicalize();
            }
            terms.Add(term);
        }

        if (terms.Count == 0) return new Number(0m);
        if (terms.Count == 1) return terms[0];
        return new Add(terms.ToImmutableList()).Canonicalize();
    }
}

/// <summary>
/// Result of linear factorization: content * (x - r1) * ... * residual(x)
/// </summary>
public sealed class FactorizationResult
{
    public Rational Content { get; }
    public IReadOnlyList<Rational> LinearRoots { get; }
    public Polynomial Residual { get; }

    public FactorizationResult(Rational content, IReadOnlyList<Rational> linearRoots, Polynomial residual)
    {
        Content = content;
        LinearRoots = linearRoots;
        Residual = residual;
    }

    public IExpression ToExpression(Symbol variable)
    {
        var factors = new List<IExpression>();

        if (!Content.IsOne)
        {
            factors.Add(Content.ToExpression());
        }

        foreach (var root in LinearRoots)
        {
            var rootExpr = root.ToExpression();
            var term = new Add(variable, new Multiply(new Number(-1m), rootExpr).Canonicalize()).Canonicalize();
            factors.Add(term);
        }

        if (!(Residual.Degree == 0 && Residual.Coefficients[0].IsOne))
        {
            factors.Add(Residual.ToExpression(variable));
        }

        if (factors.Count == 0) return new Number(1m);
        if (factors.Count == 1) return factors[0];
        return new Multiply(factors.ToImmutableList()).Canonicalize();
    }
}
