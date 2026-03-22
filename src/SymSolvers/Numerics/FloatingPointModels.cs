// Copyright Warren Harding 2026
using System;

namespace SymSolvers.Numerics;

public sealed class Float64Model : IFloatingPointModel
{
    public string Name => "FP64";

    public double Round(double value) => value;
    public double Add(double a, double b) => Round(a + b);
    public double Subtract(double a, double b) => Round(a - b);
    public double Multiply(double a, double b) => Round(a * b);
    public double Divide(double a, double b) => Round(a / b);
    public double Power(double a, double b) => Round(Math.Pow(a, b));
    public double Exp(double x) => Round(Math.Exp(x));
    public double Log(double x) => Round(Math.Log(x));
    public double Log(double x, double @base) => Round(Math.Log(x, @base));
    public double Log1p(double x) => Round(SpecialMath.Log1p(x));
    public double Expm1(double x) => Round(SpecialMath.Expm1(x));
    public double Sqrt(double x) => Round(Math.Sqrt(x));
    public double LogSumExp(ReadOnlySpan<double> values) => Round(LogSumExpCore(values));

    internal static double LogSumExpCore(ReadOnlySpan<double> values)
    {
        if (values.Length == 0) return double.NegativeInfinity;
        double max = double.NegativeInfinity;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > max) max = values[i];
        }
        if (double.IsInfinity(max)) return max;
        double sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += Math.Exp(values[i] - max);
        }
        return max + Math.Log(sum);
    }
}

public sealed class Float32Model : IFloatingPointModel
{
    public string Name => "FP32";

    public double Round(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return value;
        return (double)(float)value;
    }

    public double Add(double a, double b) => Round((float)a + (float)b);
    public double Subtract(double a, double b) => Round((float)a - (float)b);
    public double Multiply(double a, double b) => Round((float)a * (float)b);
    public double Divide(double a, double b) => Round((float)a / (float)b);
    public double Power(double a, double b) => Round(Math.Pow((float)a, (float)b));
    public double Exp(double x) => Round(Math.Exp((float)x));
    public double Log(double x) => Round(Math.Log((float)x));
    public double Log(double x, double @base) => Round(Math.Log((float)x, (float)@base));
    public double Log1p(double x) => Round(SpecialMath.Log1p((float)x));
    public double Expm1(double x) => Round(SpecialMath.Expm1((float)x));
    public double Sqrt(double x) => Round(Math.Sqrt((float)x));
    public double LogSumExp(ReadOnlySpan<double> values)
    {
        Span<double> buffer = values.Length <= 8 ? stackalloc double[values.Length] : new double[values.Length];
        values.CopyTo(buffer);
        for (int i = 0; i < buffer.Length; i++) buffer[i] = Round(buffer[i]);
        return Round(Float64Model.LogSumExpCore(buffer));
    }
}

public sealed class Float16Model : IFloatingPointModel
{
    public string Name => "FP16";

    private static double FromHalf(Half value) => (double)value;

    // Deterministic FP16 rounding with correct subnormal/underflow behavior.
    private static double RoundToFloat16(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return value;

        var f = (float)value;
        if (float.IsNaN(f) || float.IsInfinity(f)) return f;

        int bits = BitConverter.SingleToInt32Bits(f);
        int sign = (bits >> 31) & 0x1;
        int exp = (bits >> 23) & 0xFF;
        int mant = bits & 0x7FFFFF;

        // Handle zero/subnormal float
        if (exp == 0)
        {
            return sign == 1 ? -0.0 : 0.0;
        }

        // Unbias single exponent.
        int e = exp - 127;

        // FP16 exponent range is [-14, 15] for normals. Values below become subnormal or zero.
        if (e < -24)
        {
            return sign == 1 ? -0.0 : 0.0;
        }

        // Build a 24-bit significand with implicit leading 1.
        int sig = mant | (1 << 23);

        if (e < -14)
        {
            // Subnormal FP16: shift right to place leading 1 into subnormal mantissa.
            int shift = (-14 - e);
            // We need 10-bit mantissa, so total shift is shift + (23-10) = shift + 13.
            int totalShift = shift + 13;
            if (totalShift > 31) return sign == 1 ? -0.0 : 0.0;

            int mant16 = sig >> totalShift;
            int remMask = (1 << totalShift) - 1;
            int rem = sig & remMask;
            int half = 1 << (totalShift - 1);
            if (rem > half || (rem == half && (mant16 & 1) == 1)) mant16++;

            if (mant16 == 0) return sign == 1 ? -0.0 : 0.0;
            if (mant16 >= (1 << 10))
            {
                // Rounded up into the smallest normal.
                var h = (Half)(sign == 1 ? -Math.Pow(2, -14) : Math.Pow(2, -14));
                return (double)h;
            }

            // Value = sign * mant16 * 2^-24
            double v = mant16 * Math.Pow(2, -24);
            return sign == 1 ? -v : v;
        }

        // Normal FP16.
        int exp16 = e + 15;
        // Mantissa: take top 10 bits from sig (after dropping implicit 1).
        int mant16Norm = (sig >> 13) & 0x3FF;
        int remNorm = sig & 0x1FFF;
        if (remNorm > 0x1000 || (remNorm == 0x1000 && (mant16Norm & 1) == 1))
        {
            mant16Norm++;
            if (mant16Norm == 0x400)
            {
                mant16Norm = 0;
                exp16++;
            }
        }

        if (exp16 >= 31)
        {
            return sign == 1 ? double.NegativeInfinity : double.PositiveInfinity;
        }

        double norm = (1.0 + mant16Norm / 1024.0) * Math.Pow(2, e);
        return sign == 1 ? -norm : norm;
    }

    private static double ToHalfRounded(double value) => RoundToFloat16(value);

    private static Half ToHalf(double value) => (Half)ToHalfRounded(value);

    public double Round(double value) => ToHalfRounded(value);

    public double Add(double a, double b) => FromHalf((Half)(ToHalf(a) + ToHalf(b)));
    public double Subtract(double a, double b) => FromHalf((Half)(ToHalf(a) - ToHalf(b)));
    public double Multiply(double a, double b) => FromHalf((Half)(ToHalf(a) * ToHalf(b)));
    public double Divide(double a, double b) => FromHalf((Half)(ToHalf(a) / ToHalf(b)));
    public double Power(double a, double b) => Round(Math.Pow(ToHalfRounded(a), ToHalfRounded(b)));
    public double Exp(double x) => Round(Math.Exp(ToHalfRounded(x)));
    public double Log(double x) => Round(Math.Log(ToHalfRounded(x)));
    public double Log(double x, double @base) => Round(Math.Log(ToHalfRounded(x), ToHalfRounded(@base)));
    public double Log1p(double x) => Round(SpecialMath.Log1p(ToHalfRounded(x)));
    public double Expm1(double x) => Round(SpecialMath.Expm1(ToHalfRounded(x)));
    public double Sqrt(double x) => Round(Math.Sqrt(ToHalfRounded(x)));
    public double LogSumExp(ReadOnlySpan<double> values)
    {
        Span<double> buffer = values.Length <= 8 ? stackalloc double[values.Length] : new double[values.Length];
        for (int i = 0; i < values.Length; i++) buffer[i] = Round(values[i]);
        return Round(Float64Model.LogSumExpCore(buffer));
    }
}

public sealed class BFloat16Model : IFloatingPointModel
{
    public string Name => "BF16";

    private static double RoundBFloat(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return value;
        var f = (float)value;
        int bits = BitConverter.SingleToInt32Bits(f);
        // Keep sign + exponent + top 7 mantissa bits (truncate lower 16 bits).
        bits &= unchecked((int)0xFFFF0000);
        var rounded = BitConverter.Int32BitsToSingle(bits);
        return (double)rounded;
    }

    public double Round(double value) => RoundBFloat(value);
    public double Add(double a, double b) => Round((float)a + (float)b);
    public double Subtract(double a, double b) => Round((float)a - (float)b);
    public double Multiply(double a, double b) => Round((float)a * (float)b);
    public double Divide(double a, double b) => Round((float)a / (float)b);
    public double Power(double a, double b) => Round(Math.Pow((float)a, (float)b));
    public double Exp(double x) => Round(Math.Exp((float)x));
    public double Log(double x) => Round(Math.Log((float)x));
    public double Log(double x, double @base) => Round(Math.Log((float)x, (float)@base));
    public double Log1p(double x) => Round(SpecialMath.Log1p((float)x));
    public double Expm1(double x) => Round(SpecialMath.Expm1((float)x));
    public double Sqrt(double x) => Round(Math.Sqrt((float)x));
    public double LogSumExp(ReadOnlySpan<double> values)
    {
        Span<double> buffer = values.Length <= 8 ? stackalloc double[values.Length] : new double[values.Length];
        for (int i = 0; i < values.Length; i++) buffer[i] = Round(values[i]);
        return Round(Float64Model.LogSumExpCore(buffer));
    }
}
