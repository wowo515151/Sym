//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;
using SymCore;
using System.Numerics;
namespace Sym.Operations
{
    public sealed class Power : Operation
    {
        public override string Head => "Pow";
        private static readonly bool EnableDebugCanonicalize =
            string.Equals(Environment.GetEnvironmentVariable("SYM_DEBUG_CANONICALIZE"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("SYM_DEBUG_CANONICALIZE"), "true", StringComparison.OrdinalIgnoreCase);

        public IExpression Base { get; init; }
        public IExpression Exponent { get; init; }

        public Power(IExpression @base, IExpression exponent) : base(ImmutableList.Create(@base, exponent))
        {
            Base = @base;
            Exponent = exponent;
        }

        public override Shape Shape
        {
            get
            {
                // The shape of A**B:
                // 1. If A is Scalar, Result is Scalar.
                // 2. If B is Scalar, Result shape is Base.Shape (element-wise scalar exponentiation).
                // 3. If B is not Scalar and B's shape doesn't match A's shape, it's an error for now.
                //    (e.g., matrix exponentiation where Exp is also a matrix, not typically element-wise,
                //     or other complex tensor power rules are not yet implemented beyond scalar exponentiation).

                if (!Base.Shape.IsValid || !Exponent.Shape.IsValid)
                {
                    return Shape.Error;
                }

                if (Base.Shape.IsScalar)
                {
                    return Shape.Scalar;
                }

                if (Exponent.Shape.IsScalar)
                {
                    return Base.Shape; // Element-wise scalar exponentiation
                }

                // If exponent is not scalar, and shapes don't match, or it's a tensor power rule not implemented
                if (!Base.Shape.Dimensions.SequenceEqual(Exponent.Shape.Dimensions))
                {
                    return Shape.Error; // Incompatible shapes for non-scalar exponentiation
                }

                // If shapes are identical and non-scalar (e.g., element-wise vector power by vector exponent)
                return Base.Shape;
            }
        }

                public override IExpression Canonicalize()
                {
                    
                        if (EnableDebugCanonicalize)
                        {
                            Console.WriteLine("DEBUG: Power.Canonicalize start");
                        }
                        try
                        {
                            IExpression canonicalBase = Base.Canonicalize();
                            IExpression canonicalExponent = Exponent.Canonicalize();
                            if (EnableDebugCanonicalize && canonicalBase is Multiply)
                            {
                                Console.WriteLine("DEBUG: Power.Canonicalize canonicalBase is Multiply!");
                            }
                            // Console.WriteLine("DEBUG: Power.Canonicalize bases done");
        
                        // Handle X^0 = 1 including 0^0 = 1 per hint
                        if (canonicalExponent is Number expNum && expNum.Value == 0m)
                        {
                            return new Number(1m);
                        }
        
                        if (canonicalExponent is Number expNumOne && expNumOne.Value == 1m)
                        {
                            return canonicalBase;
                        }
        
                        if (canonicalBase is Number baseNumZero && baseNumZero.Value == 0m)
                        {
                            if (canonicalExponent is Number expNumPositive && expNumPositive.Value > 0m)
                            {
                                // 0^positive = 0
                                return new Number(0m);
                            }
                            // Case 0^negative is undefined (division by zero), leave as Power for now.
                        }
        
                        if (canonicalBase is Number baseNumOne && baseNumOne.Value == 1m)
                        {
                            return new Number(1m);
                        }
        
                        if (canonicalBase is Power nestedPower)
                        {
                            // (a^b)^c = a^(b*c)
                            IExpression newExponent = new Multiply(ImmutableList.Create(nestedPower.Exponent, canonicalExponent)).Canonicalize();
                            
                            // Safety Check: (x^even)^fraction -> x^odd is invalid for negative reals.
                            // e.g. (x^2)^0.5 -> x^1. (-2)^2=4, 4^0.5=2. But (-2)^1=-2.
                            // If inner exponent is an even integer, the result must effectively be an even power to preserve positivity
                            // (or we must know the base is positive, which we don't here).
                            bool unsafeSimplification = false;
                            if (nestedPower.Exponent is Number nExp && nExp.Value % 2 == 0) // b is even
                            {
                                if (newExponent is Number resExp)
                                {
                                    // if b*c is NOT even (i.e. odd or fractional), it's unsafe.
                                    if (resExp.Value % 2 != 0) 
                                    {
                                        unsafeSimplification = true;
                                    }
                                }
                                else
                                {
                                    // If result exponent is symbolic, we can't be sure it's even.
                                    // Conservative: don't flatten if b was even.
                                    unsafeSimplification = true;
                                }
                            }
                            
                            // Console.WriteLine($"DEBUG: Power Flatten Check: b={nestedPower.Exponent} c={canonicalExponent} new={newExponent} unsafe={unsafeSimplification}");

                            if (!unsafeSimplification)
                            {
                                if (newExponent is Number combinedExpNum && nestedPower.Base is Number nestedBaseNum) // Check nestedPower.Base as Number
                            {
                                // Special case: Keep non-perfect square radicals symbolic
                                if (combinedExpNum.Value == 0.5m || combinedExpNum.Value == -0.5m)
                                {
                                    var v = (double)nestedBaseNum.Value;
                                    if (v > 0)
                                    {
                                        var s = Math.Sqrt(v);
                                        if (Math.Abs(s - Math.Round(s)) > 1e-10)
                                        {
                                            return new Power(nestedPower.Base, newExponent).Canonicalize();
                                        }
                                    }
                                }
        
                                // Handle numerical evaluation of (number^number)^number
                                try
                                {
                                    double doubleBase = (double)nestedBaseNum.Value;
                                    double doubleExp = (double)combinedExpNum.Value;
                                    double resDouble = System.Math.Pow(doubleBase, doubleExp);

                                    if (double.IsNaN(resDouble) || double.IsInfinity(resDouble))
                                    {
                                        return new Power(nestedPower.Base, newExponent).Canonicalize();
                                    }

                                    return new Number((System.Decimal)resDouble);
                                }
                                catch (System.Exception) // Catch overflow and any other unexpected conversion/calc issues
                                {
                                    return new Power(nestedPower.Base, newExponent).Canonicalize();
                                }
                            }

                            return new Power(nestedPower.Base, newExponent).Canonicalize();
                            }
                        }
        
                        if (canonicalBase is Multiply multiply)
                        {
                            // (a * b * c)^n = a^n * b^n * c^n
                            var newArgs = multiply.Arguments.Select(arg => new Power(arg, canonicalExponent).Canonicalize()).ToImmutableList();
                            return new Multiply(newArgs).Canonicalize();
                        }
        
                        if (canonicalBase is Function f && f.Name.ToLowerInvariant() == "sqrt" && canonicalExponent is Number e && e.Value == 2m)
                        {
                            return f.Arguments[0].Canonicalize();
                        }
        
                        // Matrix exponentiation: Pow(Matrix, n) for non-negative integer n.
                        // Additional safety: ensure matrix has valid numeric entries before attempting multiplication.
                        if (canonicalBase is Matrix matrixBase && canonicalExponent is Number matrixExpNum)
                        {
                            if (matrixExpNum.Value % 1m == 0m && matrixExpNum.Value >= 0m &&
                                matrixBase.Shape.IsMatrix &&
                                matrixBase.Shape.Dimensions[0] == matrixBase.Shape.Dimensions[1])
                            {
                                var n = (long)matrixExpNum.Value;
                                var dims = matrixBase.Shape.Dimensions;
                                int size = dims[0];
                                // Build identity
                                static Matrix Identity(int nn)
                                {
                                    var dimsId = ImmutableArray.Create(nn, nn);
                                    var comps = ImmutableList.CreateBuilder<IExpression>();
                                    for (int r = 0; r < nn; r++)
                                    {
                                        for (int c = 0; c < nn; c++)
                                        {
                                            comps.Add(new Number(r == c ? 1m : 0m));
                                        }
                                    }
                                    return new Matrix(dimsId, comps.ToImmutable());
                                }
if (n == 0) return Identity(size).Canonicalize();
                                if (n == 1) return matrixBase.Canonicalize();
// Compute power by exponentiation by squaring but perform actual numeric matrix multiplication
                                // when matrix elements are numeric; otherwise fall back to MatrixMultiply to keep symbolic entries.
                                // Determine if all matrix entries are (or can be evaluated to) numeric values.
                                decimal[,] mat = new decimal[size, size];
                                bool allNumeric = true;
                                bool TryEvalSimple(IExpression e, out decimal val)
                                {
                                    if (e is Number nn) { val = nn.Value; return true; }
                                    if (e is Add add)
                                    {
                                        decimal s = 0m;
                                        foreach (var a in add.Arguments)
                                        {
                                            if (!TryEvalSimple(a, out var v)) { val = 0m; return false; }
                                            s += v;
                                        }
                                        val = s; return true;
                                    }
                                    if (e is Multiply mul)
                                    {
                                        decimal p = 1m;
                                        foreach (var a in mul.Arguments)
                                        {
                                            if (!TryEvalSimple(a, out var v)) { val = 0m; return false; }
                                            p = checked(p * v);
                                        }
                                        val = p; return true;
                                    }
                                    if (e is Power pw)
                                    {
                                        if (!TryEvalSimple(pw.Base, out var b) || !TryEvalSimple(pw.Exponent, out var ex)) { val = 0m; return false; }
                                        // integer exponent fast-path
                                        if ((double)ex == Math.Floor((double)ex) && ex >= -1000m && ex <= 1000m)
                                        {
                                            long ie = (long)ex;
                                            try
                                            {
                                                if (ie >= 0)
                                                {
                                                    decimal r = 1m;
                                                    for (long i = 0; i < ie; i++) r = checked(r * b);
                                                    val = r; return true;
                                                }
                                                else
                                                {
                                                    if (b == 0m) { val = 0m; return false; }
                                                    decimal r = 1m;
                                                    for (long i = 0; i < -ie; i++) r = checked(r * b);
                                                    val = 1m / r; return true;
                                                }
                                            }
                                            catch { val = 0m; return false; }
                                        }
                                        try
                                        {
                                            double dbl = Math.Pow((double)b, (double)ex);
                                            if (double.IsNaN(dbl) || double.IsInfinity(dbl)) { val = 0m; return false; }
                                            if (dbl > (double)decimal.MaxValue || dbl < (double)decimal.MinValue) { val = 0m; return false; }
                                            val = (decimal)dbl; return true;
                                        }
                                        catch { val = 0m; return false; }
                                    }
                                    val = 0m; return false;
                                }

                                for (int r = 0; r < size && allNumeric; r++)
                                {
                                    for (int c = 0; c < size; c++)
                                    {
                                        var arg = matrixBase.Arguments[r * size + c];
                                        if (arg is Number nm)
                                        {
                                            mat[r, c] = nm.Value;
                                        }
                                        else if (TryEvalSimple(arg, out var vv))
                                        {
                                            mat[r, c] = vv;
                                        }
                                        else
                                        {
                                            allNumeric = false; break;
                                        }
                                    }
                                }

                                if (allNumeric)
                                {
                                    decimal[,] MultiplyDec(decimal[,] A, decimal[,] B)
                                    {
                                        var R = new decimal[size, size];
                                        for (int i = 0; i < size; i++)
                                        {
                                            for (int j = 0; j < size; j++)
                                            {
                                                decimal s = 0m;
                                                for (int k = 0; k < size; k++)
                                                {
                                                    s += A[i, k] * B[k, j];
                                                }
                                                R[i, j] = s;
                                            }
                                        }
                                        return R;
                                    }

                                    decimal[,] res = new decimal[size, size];
                                    // build identity numeric
                                    for (int i = 0; i < size; i++) for (int j = 0; j < size; j++) res[i, j] = i == j ? 1m : 0m;

                                    decimal[,] baseMat = mat;
                                    long exp = n;
                                    while (exp > 0)
                                    {
                                        if ((exp & 1) == 1)
                                        {
                                            res = MultiplyDec(res, baseMat);
                                        }
                                        exp >>= 1;
                                        if (exp > 0)
                                        {
                                            baseMat = MultiplyDec(baseMat, baseMat);
                                        }
                                    }

                                    // Convert back to Matrix
                                    var compBuilder = ImmutableList.CreateBuilder<IExpression>();
                                    for (int i = 0; i < size; i++)
                                    {
                                        for (int j = 0; j < size; j++)
                                        {
                                            compBuilder.Add(new Number(res[i, j]));
                                        }
                                    }
                                    return new Matrix(ImmutableArray.Create(size, size), compBuilder.ToImmutable()).Canonicalize();
                                }
                                else
                                {
                                    // Fallback to symbolic multiplication via MatrixMultiply as before
                                    
                                    IExpression result = Identity(size);
                                    IExpression powBase = matrixBase;
                                    long exp = n;
                                    while (exp > 0)
                                    {
                                        if ((exp & 1) == 1)
                                        {
                                            result = new MatrixMultiply(result, powBase).Canonicalize();
                                        }
                                        exp >>= 1;
                                        if (exp > 0)
                                        {
                                            powBase = new MatrixMultiply(powBase, powBase).Canonicalize();
                                        }
                                    }
                                    return result.Canonicalize();
                                }
                            }
                        }
        
                        // Exact integer n-th roots: Pow(m, Pow(n, -1)) => k when k^n == m.
                        if (canonicalBase is Number intBase && intBase.Value % 1m == 0m && canonicalExponent is Power reciprocalPow)
                        {
                            if (reciprocalPow.Base is Number nBase && nBase.Value % 1m == 0m && nBase.Value > 1m &&
                                reciprocalPow.Exponent is Number nExp && nExp.Value == -1m)
                            {
                                long n;
                                if (nBase.Value <= long.MaxValue && nBase.Value >= long.MinValue)
                                {
                                    n = (long)nBase.Value;
                                    if (n > 1 && intBase.Value <= long.MaxValue && intBase.Value >= long.MinValue)
                                    {
                                        long m = (long)intBase.Value;
        
                                        // Negative base only has an integer root for odd n.
                                        if (m < 0 && (n % 2) == 0)
                                        {
                                            return new Power(canonicalBase, canonicalExponent);
                                        }
        
                                        static BigInteger PowBig(BigInteger b, long e)
                                        {
                                            BigInteger acc = BigInteger.One;
                                            for (long i = 0; i < e; i++) acc *= b;
                                            return acc;
                                        }
        
                                        var absM = System.Math.Abs((double)m);
                                        var approx = System.Math.Pow(absM, 1.0 / n);
                                        long cand = (long)System.Math.Round(approx);
                                        for (long r = cand - 1; r <= cand + 1; r++)
                                        {
                                            if (r == 0 && m != 0) continue;
                                            var rr = m < 0 ? -System.Math.Abs(r) : System.Math.Abs(r);
                                            var big = PowBig(new BigInteger(rr), n);
                                            if (big == new BigInteger(m))
                                            {
                                                return new Number((decimal)rr);
                                            }
                                        }
                                    }
                                }
                            }
                        }
        
                        // Numerical evaluation if both base and exponent are numbers
                        if (canonicalBase is Number baseVal && canonicalExponent is Number expVal)
                        {
                            // Handle 0^negative case: undefined (division by zero), return symbolic Power.
                            // 0^0 already handled above to be 1.
                            if (baseVal.Value == 0m && expVal.Value < 0m)
                            {
                                return this;
                            }
        
                            long integerExponent;
                            if (expVal.Value == System.Math.Floor(expVal.Value) && expVal.Value >= long.MinValue && expVal.Value <= long.MaxValue)
                            {
                                integerExponent = (long)expVal.Value;
                                if (integerExponent >= 0)
                                {
                                    System.Decimal result = 1m;
                                    System.Decimal currentBase = baseVal.Value;
                                    try
                                    {
                                        for (long i = 0; i < integerExponent; i++)
                                        {
                                            result = checked(result * currentBase);
                                        }
                                        return new Number(result);
                                    }
                                    catch (OverflowException)
                                    {
                                        return this; // Keep symbolic on overflow
                                    }
                                }
                                else // Negative integer exponent: 1 / base^(-exp)
                                {
                                    if (baseVal.Value == 0m)
                                    {
                                        return this; // Already handled 0^negative.
                                    }
        
                                    // If exponent is -1 and base is an integer whose reciprocal is a non-terminating decimal,
                                    // keep it symbolic (as Power) to allow later fraction reduction (e.g., 88 * 7744^-1 -> 1/88).
                                    if (integerExponent == -1 && baseVal.Value % 1m == 0m)
                                    {
                                        var absBase = System.Math.Abs(baseVal.Value);
                                        if (absBase <= long.MaxValue)
                                        {
                                            long d = (long)absBase;
                                            // Strip 2s and 5s; if anything remains, decimal expansion is non-terminating.
                                            while (d != 0 && d % 2 == 0) d /= 2;
                                            while (d != 0 && d % 5 == 0) d /= 5;
                                            if (d != 0 && d != 1)
                                            {
                                                return new Power(canonicalBase, canonicalExponent);
                                            }
                                        }
                                    }
        
                                    System.Decimal result = 1m;
                                    System.Decimal currentBase = baseVal.Value;
                                    for (long i = 0; i < -integerExponent; i++)
                                    {
                                        // Check for potential overflow
                                        if (currentBase != 0m && System.Math.Abs(result) > System.Decimal.MaxValue / System.Math.Abs(currentBase) && currentBase != 1m && currentBase != -1m) return this;
                                        result *= currentBase;
                                    }
                                    if (result == 0m)
                                    {
                                         // Should not happen unless currentBase was 0 originally (handled above).
                                         return this;
                                    }
                                    try
                                    {
                                        return new Number(1m / result);
                                    }
                                    catch (System.OverflowException)
                                    {
                                        return this; // Cannot represent reciprocal in Decimal safely
                                    }
                                }
                            }
                            else
                            {
                                // Fallback to double if exponent is not integer for power calculation
                                // This might lose precision, but fulfills the numeric evaluation
                                // System.Math.Pow can handle NaN, Infinity for doubles, which Decimal can't.
                                // If result from Math.Pow is NaN/Infinity, we return original expression.
                                
                                // Special case: Keep Pow(N, 0.5) symbolic if N is not a perfect square
                                if (expVal.Value == 0.5m || expVal.Value == -0.5m)
                                {
                                    var v = (double)baseVal.Value;
                                    if (v > 0)
                                    {
                                        var s = Math.Sqrt(v);
                                        if (Math.Abs(s - Math.Round(s)) < 1e-10)
                                        {
                                            return new Number(SymCore.NumericConvert.SafeToDecimal(Math.Pow(v, (double)expVal.Value)));
                                        }
                                        
                                        // Perfect square factor extraction: Sqrt(k^2 * M) -> k * Sqrt(M)
                                        if (expVal.Value == 0.5m && v % 1 == 0)
                                        {
                                            long longV = (long)v;
                                            for (long k = (long)Math.Sqrt(longV); k > 1; k--)
                                            {
                                                if (longV % (k * k) == 0)
                                                {
                                                    return new Multiply(new Number(k), new Power(new Number(longV / (k * k)), new Number(0.5m))).Canonicalize();
                                                }
                                            }
                                        }
                                        
                                        // If it's a decimal, try to represent as a fraction for better symbolic matching
                                        if (baseVal.Value % 1 != 0)
                                        {
                                            try 
                                            {
                                                var rationalBase = Rational.FromDecimal(baseVal.Value);
                                                if (rationalBase.Denominator < 1000)
                                                {
                                                    return new Power(rationalBase.ToExpression(), canonicalExponent);
                                                }
                                            }
                                            catch {}
                                        }
        
                                        return this; // Keep symbolic
                                    }
                                }
        
                                double doubleBase = (double)baseVal.Value;
                                double doubleExp = (double)expVal.Value;
                                double resDouble = System.Math.Pow(doubleBase, doubleExp);
        
                                if (double.IsNaN(resDouble) || double.IsInfinity(resDouble))
                                {
                                    // Cannot represent in decimal/symbolic for now, but return with canonicalized children.
                                    return new Power(canonicalBase, canonicalExponent);
                                }
        
                                try
                                {
                                    if (resDouble > (double)decimal.MaxValue || resDouble < (double)decimal.MinValue)
                                    {
                                        return new Power(canonicalBase, canonicalExponent);
                                    }
                                    return new Number((decimal)resDouble);
                                }
                                catch (System.Exception) // Catch any other unexpected conversion/calc issues (e.g., negative base with non-integer exponent)
                                {
                                    return new Power(canonicalBase, canonicalExponent); // Return original symbolic if exception occurs
                                }
                            }
                        }
        
        
                            if (!ReferenceEquals(canonicalBase, Base) || !ReferenceEquals(canonicalExponent, Exponent))
                            {
                                var res = new Power(canonicalBase, canonicalExponent);
                                // Console.WriteLine($"DEBUG: Power.Canonicalize returning NEW power: {res.GetType().Name}");
                                return res;
                            }
        
                            // Console.WriteLine("DEBUG: Power.Canonicalize returning THIS");
                            return this;
                        }
                        catch (System.OverflowException)
                        {
                            return this;
                        }
                    
                }
        public override string ToDisplayString()
        {
            return $"Pow({Base.ToDisplayString()}, {Exponent.ToDisplayString()})";
        }

        public override int InternalGetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(GetType());
            hash.Add(Base.InternalGetHashCode());
            hash.Add(Exponent.InternalGetHashCode());
            return hash.ToHashCode();
        }

        public override Operation WithArguments(ImmutableList<IExpression> newArgs)
        {
            return new Power(newArgs[0], newArgs[1]);
        }
    }
}


