// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class Multiply : Operation
    {
        public override string Head => "Mul";
        public Multiply(ImmutableList<IExpression> arguments) : base(arguments)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Multiply"/> class with a variable number of arguments.
        /// </summary>
        /// <param name="arguments">The expressions to be multiplied.</param>
        public Multiply(params IExpression[] arguments) : base(ImmutableList.Create(arguments))
        {
        }

        // Parameterless constructor added to support reflective instantiation in unit tests.
        public Multiply() : base(ImmutableList<IExpression>.Empty) { }

        public override Shape Shape
        {
            get
            {
                // This Shape property now assumes an element-wise product if it remains a Multiply.
                // Canonicalization will transform to DotProduct or MatrixMultiply if appropriate.
                Shape currentCombinedShape = Shape.Scalar;
                foreach (IExpression arg in Arguments)
                {
                    if (!arg.Shape.IsValid)
                    {
                        return Shape.Error;
                    }

                    currentCombinedShape = currentCombinedShape.CombineForElementWise(arg.Shape);
                    if (!currentCombinedShape.IsValid)
                    {
                        return Shape.Error; // Incompatible shapes found during aggregation for element-wise product
                    }
                }
                return currentCombinedShape;
            }
        }

        public override IExpression Canonicalize()
        {
            
                // Flatten same-type children BEFORE recursing into their Canonicalize().
                // This prevents stack overflow on deep chains like Multiply(1, Multiply(1, ...)).
                ImmutableList<IExpression> preFlattened = ExpressionHelpers.FlattenArguments<Multiply>(Arguments);
                ImmutableList<IExpression> canonicalArgs = preFlattened.Select(arg => arg.Canonicalize()).ToImmutableList();
                ImmutableList<IExpression> flattenedArgs = ExpressionHelpers.FlattenArguments<Multiply>(canonicalArgs);

                System.Decimal numericProduct = 1m;
                ImmutableList<IExpression>.Builder nonNumericTermsBuilder = ImmutableList.CreateBuilder<IExpression>();
                var overflowFactors = new List<IExpression>();

                foreach (IExpression arg in flattenedArgs)
                {
                    if (arg is Number num)
                    {
                        if (num.Value == 0m)
                        {
                            return new Number(0m); // Short-circuit: if any factor is 0, the product is 0.
                        }
                        
                        try
                        {
                            numericProduct *= num.Value;
                        }
                        catch (OverflowException)
                        {
                            // If product overflows Decimal, keep this number as a separate factor
                            overflowFactors.Add(num);
                        }
                    }
                    else
                    {
                        nonNumericTermsBuilder.Add(arg);
                    }
                }
                
                nonNumericTermsBuilder.AddRange(overflowFactors);

                // Combine terms with same base into powers (Scalars only)
                var termsByBase = new Dictionary<IExpression, IExpression>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
                var otherNonNumericTerms = new List<IExpression>();

                foreach (var term in nonNumericTermsBuilder)
                {
                    if (!term.Shape.IsScalar)
                    {
                        otherNonNumericTerms.Add(term);
                        continue;
                    }

                    IExpression baseExpr;
                    IExpression exponent;
                    if (term is Power p)
                    {
                        baseExpr = p.Base;
                        exponent = p.Exponent;
                    }
                    else
                    {
                        baseExpr = term;
                        exponent = new Number(1m);
                    }

                    if (termsByBase.TryGetValue(baseExpr, out var existingExp))
                    {
                        try
                        {
                            termsByBase[baseExpr] = new Add(existingExp, exponent).Canonicalize();
                        }
                        catch (OverflowException)
                        {
                            // If exponent combination overflows (e.g. very large powers), 
                            // keep them as separate factors in otherNonNumericTerms.
                            otherNonNumericTerms.Add(term);
                        }
                    }
                    else
                    {
                        termsByBase[baseExpr] = exponent;
                    }
                }

                var combinedNonNumericTerms = new List<IExpression>();
                foreach (var kvp in termsByBase)
                {
                    var baseExpr = kvp.Key;
                    var exponent = kvp.Value;

                    if (exponent is Number n && n.Value == 0m)
                    {
                        // base^0 = 1, ignore
                        continue;
                    }
                    if (exponent is Number n1 && n1.Value == 1m)
                    {
                        combinedNonNumericTerms.Add(baseExpr);
                    }
                    else
                    {
                        combinedNonNumericTerms.Add(new Power(baseExpr, exponent).Canonicalize());
                    }
                }
                combinedNonNumericTerms.AddRange(otherNonNumericTerms);

                ImmutableList<IExpression> nonNumericTerms = combinedNonNumericTerms.ToImmutableList();

                // Attempt to absorb reciprocal numeric powers into the numeric product
                var nonNumericMutable = nonNumericTerms.ToBuilder();
                for (int i = nonNumericMutable.Count - 1; i >= 0; i--)
                {
                    if (nonNumericMutable[i] is Power p && p.Base is Number baseNum && p.Exponent is Number expNum && expNum.Value == -1m)
                    {
                        try
                        {
                            if (baseNum.Value == 0m) continue; // skip division by zero
                            decimal candidate;

                            // Reduce integer factors before dividing to improve rounding consistency.
                            if (numericProduct % 1m == 0m && baseNum.Value % 1m == 0m)
                            {
                                var n = numericProduct;
                                var d = baseNum.Value;

                                var absN = Math.Abs(n);
                                var absD = Math.Abs(d);

                                if (absN <= long.MaxValue && absD <= long.MaxValue)
                                {
                                    long ln = (long)absN;
                                    long ld = (long)absD;

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

                                    if (ln != 0 && ld != 0)
                                    {
                                        var g = Gcd(ln, ld);
                                        if (g > 1)
                                        {
                                            n /= g;
                                            d /= g;
                                        }
                                    }
                                }

                                candidate = n / d;
                            }
                            else
                            {
                                candidate = numericProduct / baseNum.Value;
                            }
                            // If division succeeds without overflow, adopt it and remove the reciprocal power term
                            numericProduct = candidate;
                            nonNumericMutable.RemoveAt(i);
                        }
                        catch (System.OverflowException)
                        {
                            // If decimal division cannot represent the result, keep the symbolic reciprocal
                            continue;
                        }
                    }
                }

                nonNumericTerms = nonNumericMutable.ToImmutable();

                // If all arguments were numeric (nonNumericTerms is empty), return a single Number.
                if (nonNumericTerms.IsEmpty)
                {
                    return new Number(numericProduct);
                }

                // Rebuild the collected args from the *post-absorption* term list.
                // (The earlier builder still contains pre-absorption terms.)
                var collectedBuilder = nonNumericTerms.ToBuilder();
                // If there are non-numeric terms, and the numeric product is not 1, add it.
                // If numericProduct is 1 and there are other terms, Number(1) is redundant as a factor.
                // Use a small tolerance for the 1.0 check to handle decimal precision noise.
                if (System.Math.Abs((double)(numericProduct - 1m)) > 1e-15)
                {
                    collectedBuilder.Add(new Number(numericProduct));
                }
                else if (nonNumericTerms.IsEmpty)
                {
                    // If it was exactly 1.0 (within tolerance) but there are no other terms, we must keep the 1.
                    collectedBuilder.Add(new Number(numericProduct));
                }

                ImmutableList<IExpression> collectedArgs = collectedBuilder.ToImmutable();
                ImmutableList<IExpression> sortedArgs = ExpressionHelpers.SortArguments(collectedArgs);

                // If after canonicalization and combining, only one argument remains, return it.
                if (sortedArgs.Count == 1)
                {
                    return sortedArgs[0];
                }

                // If sortedArgs is empty, it means no arguments, or all arguments simplified to factors of 1.
                // In multiplication, an empty product is typically 1 (multiplicative identity).
                if (sortedArgs.Count == 0)
                {
                    return new Number(1m);
                }

                // Distribution: If any argument is an Add, distribute others over it.
                // We do this if there is at least one Add and the product is scalar.
                Shape currentShape = Shape.Scalar;
                foreach (var arg in sortedArgs) currentShape = currentShape.CombineForElementWise(arg.Shape);

                if (currentShape.IsScalar)
                {
                    var firstAdd = sortedArgs.FirstOrDefault(a => a is Add) as Add;
                    if (firstAdd != null)
                    {
                        var others = sortedArgs.Remove(firstAdd);
                        IExpression factor = others.Count == 0 ? new Number(1m) : (others.Count == 1 ? others[0] : new Multiply(others).Canonicalize());
                        
                        var newTerms = firstAdd.Arguments.Select(term => new Multiply(term, factor).Canonicalize()).ToImmutableList();
                        return new Add(newTerms).Canonicalize();
                    }
                }

                // Handle special products (DotProduct, MatrixMultiply) only if exactly two sorted arguments remain.
                if (sortedArgs.Count == 2)
                {
                    IExpression operand1 = sortedArgs[0];
                    IExpression operand2 = sortedArgs[1];

                    // Attempt to promote to DotProduct: Vector . Vector or (1xN)Matrix . (Nx1)Matrix
                    bool isVectorDotProduct = operand1.Shape.IsVector && operand2.Shape.IsVector && operand1.Shape.Dimensions[0] == operand2.Shape.Dimensions[0];
                    bool isMatrixDotProductEquivalent = operand1.Shape.IsMatrix && operand2.Shape.IsMatrix &&
                                                        operand1.Shape.Dimensions.Length == 2 && operand2.Shape.Dimensions.Length == 2 &&
                                                        operand1.Shape.Dimensions[0] == 1 && operand1.Shape.Dimensions[1] == operand2.Shape.Dimensions[0] &&
                                                        operand2.Shape.Dimensions[1] == 1;

                    if (isVectorDotProduct || isMatrixDotProductEquivalent)
                    {
                        DotProduct result = new DotProduct(operand1, operand2);
                        if (!result.Shape.Equals(Shape.Error)) // Ensure the constructed DotProduct's shape is valid
                        {
                            return result.Canonicalize(); // Recursively canonicalize the new DotProduct
                        }
                    }

                    // Attempt to promote to MatrixMultiply: Matrix * Matrix OR Matrix * Vector OR Vector * Matrix
                    bool isMatrixMatrixMultiply = operand1.Shape.IsMatrix && operand2.Shape.IsMatrix;
                    bool isMatrixVectorMultiply = operand1.Shape.IsMatrix && operand2.Shape.IsVector;
                    bool isVectorMatrixMultiply = operand1.Shape.IsVector && operand2.Shape.IsMatrix;

                    if (isMatrixMatrixMultiply || isMatrixVectorMultiply || isVectorMatrixMultiply)
                    {
                        MatrixMultiply result = new MatrixMultiply(operand1, operand2);
                        if (!result.Shape.Equals(Shape.Error)) // Ensure the constructed MatrixMultiply's shape is valid
                        {
                            return result.Canonicalize(); // Recursively canonicalize the new MatrixMultiply
                        }
                    }
                }

                // If no special product applies, or if more than two arguments,
                // return a new Multiply operation with the sorted arguments.
                return new Multiply(sortedArgs);
            
        }

        public override string ToDisplayString()
        {
            return $"({string.Join(" * ", Arguments.Select(arg => arg.ToDisplayString()))})";
        }

        public override bool InternalEquals(IExpression other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is not Multiply otherMultiply) return false;
            return ExpressionHelpers.SequencesInternalEquals(this.Arguments, otherMultiply.Arguments);
        }

        public override int InternalGetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(GetType());
            foreach (IExpression arg in Arguments)
            {
                hash.Add(arg.InternalGetHashCode());
            }

            return hash.ToHashCode();
        }

        public override Operation WithArguments(ImmutableList<IExpression> newArgs)
        {
            return new Multiply(newArgs);
        }
    }
}
