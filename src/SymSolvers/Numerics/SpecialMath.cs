using System;

namespace SymSolvers.Numerics;

internal static class SpecialMath
{
    public static double Log1p(double x)
    {
        if (double.IsNaN(x) || double.IsPositiveInfinity(x)) return x;
        if (x == -1d) return double.NegativeInfinity;
        if (x < -1d) return double.NaN;

        var ax = Math.Abs(x);
        if (ax < 1e-4)
        {
            // Taylor series around 0: log(1+x) = x - x^2/2 + x^3/3 - x^4/4 + ...
            var x2 = x * x;
            var x3 = x2 * x;
            var x4 = x2 * x2;
            var x5 = x4 * x;
            return x - (x2 / 2d) + (x3 / 3d) - (x4 / 4d) + (x5 / 5d);
        }

        return Math.Log(1d + x);
    }

    public static double Expm1(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return x;

        var ax = Math.Abs(x);
        if (ax < 1e-5)
        {
            // Taylor series around 0: exp(x)-1 = x + x^2/2 + x^3/6 + x^4/24 + x^5/120
            var x2 = x * x;
            var x3 = x2 * x;
            var x4 = x2 * x2;
            var x5 = x4 * x;
            return x + (x2 / 2d) + (x3 / 6d) + (x4 / 24d) + (x5 / 120d);
        }

        return Math.Exp(x) - 1d;
    }
}
