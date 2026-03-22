//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class Divide : Operation
    {
        public IExpression Numerator { get; init; }
        public IExpression Denominator { get; init; }

        public Divide(IExpression numerator, IExpression denominator)
            : base(ImmutableList.Create(numerator, denominator))
        {
            Numerator = numerator;
            Denominator = denominator;
        }

        public override Shape Shape
        {
            get
            {
                if (!Numerator.Shape.IsValid || !Denominator.Shape.IsValid)
                {
                    return Shape.Error;
                }
                return Numerator.Shape.CombineForElementWise(Denominator.Shape);
            }
        }

        public override IExpression Canonicalize()
        {
            
                IExpression canonicalNumerator = Numerator.Canonicalize();
                IExpression canonicalDenominator = Denominator.Canonicalize();

                // Handle division by zero
                if (canonicalDenominator is Number denNum && denNum.Value == 0m)
                {
                    return this; // Return symbolic form, as it's undefined or leads to infinity
                }

                // A / A => 1 (avoid 0/0 above)
                if (canonicalNumerator.InternalEquals(canonicalDenominator))
                {
                    return new Number(1m);
                }

                // Perform numerical evaluation if both operands are numbers
                if (canonicalNumerator is Number numVal && canonicalDenominator is Number denVal)
                {
                    // Reduce integer fractions before dividing to improve decimal rounding consistency
                    // (e.g., 88/7744 reduces to 1/88; both are equal but decimal rounding can differ).
                    var n = numVal.Value;
                    var d = denVal.Value;

                    if (d != 0m && n % 1m == 0m && d % 1m == 0m)
                    {
                        var absN = Math.Abs(n);
                        var absD = Math.Abs(d);
                        if (absN <= long.MaxValue && absD <= long.MaxValue)
                        {
                            long ln = (long)absN;
                            long ld = (long)absD;
                            if (ln != 0 && ld != 0)
                            {
                                static long Gcd(long a, long b)
                                {
                                    while (b != 0)
                                    {
                                        long t = a % b;
                                        a = b;
                                        b = t;
                                    }
                                    return Math.Abs(a);
                                }

                                var g = Gcd(ln, ld);
                                if (g > 1)
                                {
                                    n /= g;
                                    d /= g;
                                }
                            }
                        }
                    }

                    try
                    {
                        return new Number(n / d);
                    }
                    catch (OverflowException)
                    {
                        return this;
                    }
                }

                // Convert A / B to A * B^(-1) and canonicalize the Multiply operation.
                // This delegates to Multiply and Power for simplification rules.
                Power inverseDenominator = new Power(canonicalDenominator, new Number(-1m));
                Multiply resultMultiply = new Multiply(canonicalNumerator, inverseDenominator.Canonicalize());

                return resultMultiply.Canonicalize();
            
        }

        public override string ToDisplayString()
        {
            return $"({Numerator.ToDisplayString()} / {Denominator.ToDisplayString()})";
        }

        public override bool InternalEquals(IExpression other)
        {
            if (other is not Divide otherDivide)
            {
                return false;
            }
            return Numerator.InternalEquals(otherDivide.Numerator) &&
                   Denominator.InternalEquals(otherDivide.Denominator);
        }

        public override int InternalGetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(GetType());
            hash.Add(Numerator.InternalGetHashCode());
            hash.Add(Denominator.InternalGetHashCode());
            return hash.ToHashCode();
        }

        public override Operation WithArguments(ImmutableList<IExpression> newArgs)
        {
            return new Divide(newArgs[0], newArgs[1]);
        }
    }
}
