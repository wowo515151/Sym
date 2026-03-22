// Copyright Warren Harding 2026
using System;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers.Numerics;

public static class NumericRefinement
{
    public static bool TryReconstructRadical(decimal value, out IExpression result)
    {
        result = null!;
        if (value == 0) return false;
        
        // Safety check for huge values that might overflow during double conversion or multiplication
        if (Math.Abs(value) > 1e20m || Math.Abs(value) < 1e-20m) return false;

        // Try b in small square-free integers
        var commonB = new[] { 2, 3, 5, 6, 7, 10, 11, 13, 14, 15, 17, 19, 21, 22, 23, 26, 29, 30, 31, 33, 34, 35, 37, 38, 39, 41, 42, 43, 46, 47 };
        
        foreach (var b in commonB)
        {
            double sqrtB = Math.Sqrt(b);
            double ratio = (double)value / sqrtB;
            
            // Search for a simple fraction for the ratio
            for (int d = 1; d <= 1000; d++)
            {
                var n = Math.Round(ratio * d);
                if (Math.Abs(ratio - n / d) < 1e-10)
                {
                    IExpression coeff = (d == 1) 
                        ? new Number((decimal)n) 
                        : new Divide(new Number((decimal)n), new Number(d)).Canonicalize();
                    
                    result = new Multiply(coeff, new Power(new Number(b), new Number(0.5m))).Canonicalize();
                    return true;
                }
            }
        }
        
        return false;
    }

    public static bool TrySnapToFraction(decimal value, out IExpression result, int maxDenominator = 100)
    {
        result = null!;
        // No need for a limit here if it's already a valid decimal, 
        // but we'll use a very large one to be safe.
        if (Math.Abs(value) > 1e28m) 
        {
             // If it's an integer, we can still snap it to itself
             if (value % 1 == 0) { result = new Number(value); return true; }
             return false;
        }

        // First try Rational.FromDecimal to get a reduced fraction quickly for common decimals
        try
        {
            var rat0 = Rational.FromDecimal(value);
            if (!rat0.Denominator.IsOne && rat0.Denominator <= new System.Numerics.BigInteger(maxDenominator))
            {
                if (rat0.Numerator.IsZero == false)
                {
                    var rn = (long)rat0.Numerator;
                    var rd = (long)rat0.Denominator;
                    result = new Divide(new Number((decimal)rn), new Number(rd));
                    return true;
                }
            }
        }
        catch { }

        for (int d = 1; d <= maxDenominator; d++)
        {
            var n = Math.Round(value * d);
            if (d == 1 && Math.Abs(value - Math.Round(value, 0)) < 1e-12m)
            {
                // value is effectively an integer
                var intVal = (decimal)Math.Round(value, 0);
                result = new Number(intVal);
                return true;
            }
            if (Math.Abs(value - (decimal)n / d) < 1e-8m)
            {
                // Reduce fraction to lowest terms
                var ni = (long)n;
                var di = d;
                long a = Math.Abs(ni);
                int b = di;
                while (b != 0)
                {
                    var t = a % b;
                    a = b;
                    b = (int)t;
                }
                var gcd = (long)a;
                ni /= gcd;
                di /= (int)gcd;
                if (di == 1)
                {
                    result = new Number(ni);
                }
                else
                {
                    result = new Divide(new Number((decimal)ni), new Number(di));
                }
                return true;
            }
        }
        return false;
    }
}
