using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Minimal multivariate monomial representation (coefficient * Π symbol^exp) for factoring.
/// </summary>
internal sealed class MultivariateMonomial
{
    public Rational Coefficient { get; }
    public ImmutableDictionary<string, int> Exponents { get; }

    private MultivariateMonomial(Rational coeff, IDictionary<string, int> exps)
    {
        Coefficient = coeff;
        Exponents = exps.ToImmutableDictionary();
    }

    public static bool TryParse(IExpression expr, out MultivariateMonomial mono)
    {
        var coeff = Rational.One;
        var exps = new Dictionary<string, int>(System.StringComparer.Ordinal);

        bool Visit(IExpression node)
        {
            switch (node)
            {
                case Number n:
                    coeff *= Rational.FromDecimal(n.Value);
                    return true;
                case Symbol s:
                    AddExp(s.Name, 1);
                    return true;
                case Power p when p.Base is Symbol sym && p.Exponent is Number powNum:
                    var asInt = (int)powNum.Value;
                    if (powNum.Value != asInt) return false;
                    AddExp(sym.Name, asInt);
                    return true;
                case Divide d:
                    if (!Visit(d.Numerator)) return false;
                    if (d.Denominator is Number nDenom)
                    {
                        if (nDenom.Value == 0m) return false;
                        coeff *= Rational.One / Rational.FromDecimal(nDenom.Value);
                        return true;
                    }
                    if (d.Denominator is Symbol sDenom)
                    {
                        AddExp(sDenom.Name, -1);
                        return true;
                    }
                    if (d.Denominator is Power pDenom && pDenom.Base is Symbol symDenom && pDenom.Exponent is Number nExpDenom)
                    {
                        var asIntDenom = (int)nExpDenom.Value;
                        if (nExpDenom.Value != asIntDenom) return false;
                        AddExp(symDenom.Name, -asIntDenom);
                        return true;
                    }
                    return false;
                case Multiply m:
                    foreach (var arg in m.Arguments)
                    {
                        if (!Visit(arg)) return false;
                    }
                    return true;
                case Add a:
                    if (a.Arguments.Count == 1) return Visit(a.Arguments[0]);
                    return false;
                case Subtract s:
                    if (!Visit(s.LeftOperand)) return false;
                    if (s.RightOperand is Number nSub)
                    {
                        coeff -= Rational.FromDecimal(nSub.Value);
                        return true;
                    }
                    // Subtract(X, Y) where Y is not a number is usually not a monomial unless Y is Number(0).
                    return false;
                default:
                    return false;
            }
        }

        void AddExp(string name, int power)
        {
            exps[name] = exps.TryGetValue(name, out var existing) ? existing + power : power;
        }

        if (!Visit(expr.Canonicalize()))
        {
            mono = default!;
            return false;
        }

        mono = new MultivariateMonomial(coeff, exps);
        return true;
    }

    public static MultivariateMonomial Gcd(MultivariateMonomial a, MultivariateMonomial b)
    {
        var coeffGcd = Gcd(a.Coefficient, b.Coefficient);
        var exps = new Dictionary<string, int>(System.StringComparer.Ordinal);

        foreach (var kv in a.Exponents)
        {
            if (b.Exponents.TryGetValue(kv.Key, out var other))
            {
                exps[kv.Key] = kv.Value < other ? kv.Value : other;
            }
        }

        return new MultivariateMonomial(coeffGcd, exps);
    }

    public MultivariateMonomial Divide(MultivariateMonomial divisor)
    {
        var coeff = Coefficient / divisor.Coefficient;
        var exps = new Dictionary<string, int>(Exponents, System.StringComparer.Ordinal);
        foreach (var kv in divisor.Exponents)
        {
            if (!exps.TryGetValue(kv.Key, out var current) || current < kv.Value)
            {
                return new MultivariateMonomial(Rational.Zero, new Dictionary<string, int>());
            }
            var remaining = current - kv.Value;
            if (remaining == 0) exps.Remove(kv.Key);
            else exps[kv.Key] = remaining;
        }
        return new MultivariateMonomial(coeff, exps);
    }

    public IExpression ToExpression()
    {
        var posFactors = new List<IExpression>();
        var negFactors = new List<IExpression>();

        if (!Coefficient.IsOne || Exponents.Count == 0)
        {
            posFactors.Add(Coefficient.ToExpression());
        }

        foreach (var kv in Exponents.OrderBy(k => k.Key, System.StringComparer.Ordinal))
        {
            IExpression symbol = new Symbol(kv.Key);
            if (kv.Value == 1)
            {
                posFactors.Add(symbol);
            }
            else if (kv.Value > 1)
            {
                posFactors.Add(new Power(symbol, new Number(kv.Value)).Canonicalize());
            }
            else if (kv.Value == -1)
            {
                negFactors.Add(symbol);
            }
            else if (kv.Value < -1)
            {
                negFactors.Add(new Power(symbol, new Number(-kv.Value)).Canonicalize());
            }
        }

        IExpression num = posFactors.Count == 0 ? new Number(1m) :
                          posFactors.Count == 1 ? posFactors[0].Canonicalize() :
                          new Multiply(posFactors.ToImmutableList()).Canonicalize();

        if (negFactors.Count == 0) return num;

        IExpression den = negFactors.Count == 1 ? negFactors[0].Canonicalize() :
                          new Multiply(negFactors.ToImmutableList()).Canonicalize();

        return new Divide(num, den).Canonicalize();
    }

    private static Rational Gcd(Rational a, Rational b)
    {
        var num = System.Numerics.BigInteger.GreatestCommonDivisor(
            System.Numerics.BigInteger.Abs(a.Numerator),
            System.Numerics.BigInteger.Abs(b.Numerator));
        var den = System.Numerics.BigInteger.GreatestCommonDivisor(
            System.Numerics.BigInteger.Abs(a.Denominator),
            System.Numerics.BigInteger.Abs(b.Denominator));
        return new Rational(num, den);
    }
}
