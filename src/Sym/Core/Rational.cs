using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Sym.Atoms;
using Sym.Operations;

namespace Sym.Core;

/// <summary>
/// Exact rational number backed by BigInteger for polynomial arithmetic.
/// </summary>
public readonly struct Rational : IEquatable<Rational>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public static Rational Zero => new(BigInteger.Zero, BigInteger.One);
    public static Rational One => new(BigInteger.One, BigInteger.One);

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException("Denominator cannot be zero.");
        if (denominator.Sign < 0)
        {
            numerator = BigInteger.Negate(numerator);
            denominator = BigInteger.Negate(denominator);
        }

        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), BigInteger.Abs(denominator));
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    public bool IsZero => Numerator.IsZero;
    public bool IsOne => Numerator.Equals(Denominator);

    public static Rational FromInteger(long value) => new(value, 1);

    public static Rational FromDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var lo = (uint)bits[0];
        var mid = (uint)bits[1];
        var hi = (uint)bits[2];
        var scale = (bits[3] >> 16) & 0x7F;
        var sign = (bits[3] & unchecked((int)0x80000000)) != 0 ? -1 : 1;

        BigInteger unscaled = new BigInteger(lo);
        unscaled += new BigInteger(mid) << 32;
        unscaled += new BigInteger(hi) << 64;
        unscaled *= sign;

        var denominator = BigInteger.Pow(10, scale);
        return new Rational(unscaled, denominator);
    }

    public Rational Reciprocal() => new(Denominator, Numerator);

    public bool TrySqrt(out Rational sqrt)
    {
        var numSqrt = IntegerSqrt(Numerator);
        var denSqrt = IntegerSqrt(Denominator);

        if (numSqrt * numSqrt != Numerator || denSqrt * denSqrt != Denominator)
        {
            sqrt = Zero;
            return false;
        }

        sqrt = new Rational(numSqrt, denSqrt);
        return true;
    }

    public static Rational operator +(Rational a, Rational b) =>
        new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a, Rational b) =>
        new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator *(Rational a, Rational b) =>
        new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static Rational operator /(Rational a, Rational b)
    {
        if (b.Numerator.IsZero) throw new DivideByZeroException();
        return new Rational(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    public bool Equals(Rational other) => Numerator.Equals(other.Numerator) && Denominator.Equals(other.Denominator);

    public override bool Equals(object? obj) => obj is Rational other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public decimal ToDecimal()
    {
        // Fall back to high-precision conversion when decimal cannot hold the value.
        try 
        {
            double dn = (double)Numerator;
            double dd = (double)Denominator;
            return SymCore.NumericConvert.SafeToDecimal(dn / dd);
        }
        catch (OverflowException)
        {
            // Reduce fraction by dividing numerator and denominator by gcd of their magnitude to fit into decimal range if possible
            var max = BigInteger.Min(BigInteger.Abs(Numerator), BigInteger.Abs(Denominator));
            if (max.IsZero) return 0m;
            var scale = BigInteger.One;
            // Try to scale down until values fit in decimal
            while (true)
            {
                try
                {
                    var n = Numerator / scale;
                    var d = Denominator / scale;
                    if (n > (BigInteger)decimal.MaxValue || n < (BigInteger)decimal.MinValue || d > (BigInteger)decimal.MaxValue || d < (BigInteger)decimal.MinValue)
                    {
                        // increase scale
                        scale *= 10;
                        if (scale > max) break;
                        continue;
                    }
                    return SymCore.NumericConvert.SafeToDecimal((double)n / (double)d);
                }
                catch { break; }
            }
            // If unable to scale down to fit Decimal, return a capped extreme value to avoid throwing.
            return Numerator.Sign >= 0 ? decimal.MaxValue : decimal.MinValue;
        }
    }

    private static decimal ToDecimalChecked(BigInteger value)
    {
        if (value > (BigInteger)decimal.MaxValue || value < (BigInteger)decimal.MinValue)
        {
            // When value is out of decimal range, return a capped extreme to avoid throwing.
            return value.Sign >= 0 ? decimal.MaxValue : decimal.MinValue;
        }
        return SymCore.NumericConvert.SafeToDecimal((double)value);
    }

    public IExpression ToExpression()
    {
        if (Denominator.IsOne) return new Number(ToDecimalChecked(Numerator));
        // If numerator/denominator cannot be represented as decimal, fall back to returning a Divide with BigInteger represented as string parsed into Number where possible.
        var num = ToDecimalChecked(Numerator);
        var den = ToDecimalChecked(Denominator);
        return new Divide(new Number(num), new Number(den)).Canonicalize();
    }

    public override string ToString()
    {
        return Denominator.IsOne ? Numerator.ToString() : $"{Numerator}/{Denominator}";
    }

    private static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n.Sign < 0) return BigInteger.Zero;
        if (n.IsZero) return BigInteger.Zero;

        BigInteger low = BigInteger.Zero;
        BigInteger high = BigInteger.One;

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
}
