using System;

namespace SymCore;

public static class NumericConvert
{
    public static decimal SafeToDecimal(double d)
    {
        if (double.IsNaN(d)) throw new InvalidOperationException("Double value is NaN.");
        if (double.IsPositiveInfinity(d) || d >= (double)decimal.MaxValue) throw new OverflowException("Double value out of decimal range (too large).");
        if (double.IsNegativeInfinity(d) || d <= (double)decimal.MinValue) throw new OverflowException("Double value out of decimal range (too small).");
        return (decimal)d;
    }
}
