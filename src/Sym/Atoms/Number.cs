//Copyright Warren Harding 2025.
using Sym.Core;
using System;
using System.Globalization;

namespace Sym.Atoms
{
    public sealed class Number : Atom
    {
        public override string Head
        {
            get
            {
                var s = Value.ToString(CultureInfo.InvariantCulture);
                if (s.Contains('.', StringComparison.Ordinal))
                {
                    s = s.TrimEnd('0').TrimEnd('.');
                }
                return "Num:" + s;
            }
        }
        public System.Decimal Value { get; init; }
        private readonly Shape _shape;
        public override Shape Shape { get { return _shape; } }

        // Parameterless constructor added to support reflective instantiation in unit tests.
        public Number() : this(0m) { }

        public Number(System.Decimal value)
        {
            Value = value;
            _shape = Sym.Core.Shape.Scalar;
        }

        public override IExpression Canonicalize()
        {
            return this;
        }

        public override string ToDisplayString()
        {
            // Decimal preserves trailing zeros (e.g., 6.00m => "6.00"); trim for stable display.
            var s = Value.ToString(CultureInfo.InvariantCulture);
            if (s.Contains('.', StringComparison.Ordinal))
            {
                s = s.TrimEnd('0').TrimEnd('.');
            }
            return s;
        }

        public override bool InternalEquals(IExpression other)
        {
            if (other is not Number otherNumber)
            {
                return false;
            }
            if (Value.Equals(otherNumber.Value)) return true;
            
            try
            {
                // Allow a small tolerance for decimal round-trip/precision noise
                var diff = System.Math.Abs((double)(Value - otherNumber.Value));
                return diff <= 1e-15;
            }
            catch (System.OverflowException)
            {
                // If subtraction overflows, compare as doubles
                var d1 = (double)Value;
                var d2 = (double)otherNumber.Value;
                return System.Math.Abs(d1 - d2) <= 1e-15 * System.Math.Max(System.Math.Abs(d1), System.Math.Abs(d2));
            }
        }

        public override int InternalGetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}
