//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class Add : Operation
    {
        public Add(ImmutableList<IExpression> arguments) : base(arguments)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Add"/> class with a variable number of arguments.
        /// </summary>
        /// <param name="arguments">The expressions to be added.</param>
        public Add(params IExpression[] arguments) : base(ImmutableList.Create(arguments))
        {
        }

        // Parameterless constructor added to support reflective instantiation in unit tests.
        public Add() : base(ImmutableList<IExpression>.Empty) { }

        public override Shape Shape
        {
            get
            {
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
                        return Shape.Error; // Incompatible shapes found during aggregation
                    }
                }
                return currentCombinedShape;
            }
        }

        public override IExpression Canonicalize()
        {
            
                // Flatten same-type children BEFORE recursing into their Canonicalize().
                // This prevents stack overflow on deep chains like Add(1, Add(1, ...)).
                ImmutableList<IExpression> preFlattened = ExpressionHelpers.FlattenArguments<Add>(Arguments);

                // Avoid noisy Console output during test runs (can cause apparent hangs in VS Test Explorer).
                // Opt-in by setting environment variable: SYM_TRACE_ADD_CANON=1
                if (Arguments.Count > 0 && Environment.GetEnvironmentVariable("SYM_TRACE_ADD_CANON") == "1")
                {
                    System.Console.WriteLine(
                        $"DEBUG: Add.Canonicalize preFlattened: {string.Join(", ", preFlattened.Select(a => a.ToDisplayString()))}");
                }
                ImmutableList<IExpression> canonicalArgs = preFlattened.Select(arg => arg.Canonicalize()).ToImmutableList();
                ImmutableList<IExpression> flattenedArgs = ExpressionHelpers.FlattenArguments<Add>(canonicalArgs);

                System.Decimal numericSum = 0m;
                ImmutableList<IExpression>.Builder nonNumericTermsBuilder = ImmutableList.CreateBuilder<IExpression>();

                foreach (IExpression arg in flattenedArgs)
                {
                    if (arg is Number num)
                    {
                        try
                        {
                            numericSum += num.Value;
                        }
                        catch (OverflowException)
                        {
                            // If sum overflows Decimal, keep this number as a separate term
                            nonNumericTermsBuilder.Add(num);
                        }
                    }
                    else
                    {
                        nonNumericTermsBuilder.Add(arg);
                    }
                }

                // Combine like terms (e.g., x + x -> 2*x)
                var termsByBase = new Dictionary<IExpression, decimal>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
                var overflowTerms = new List<IExpression>();

                foreach (var term in nonNumericTermsBuilder)
                {
                    IExpression baseExpr;
                    decimal coefficient;

                    if (term is Multiply mul && mul.Arguments.Count > 0 && mul.Arguments[0] is Number num)
                    {
                        coefficient = num.Value;
                        if (mul.Arguments.Count == 2)
                        {
                            baseExpr = mul.Arguments[1];
                        }
                        else
                        {
                            baseExpr = new Multiply(mul.Arguments.RemoveAt(0)).Canonicalize();
                        }
                    }
                    else
                    {
                        baseExpr = term;
                        coefficient = 1m;
                    }

                    if (termsByBase.TryGetValue(baseExpr, out var existingCoeff))
                    {
                        try
                        {
                            termsByBase[baseExpr] = existingCoeff + coefficient;
                        }
                        catch (OverflowException)
                        {
                            overflowTerms.Add(term);
                        }
                    }
                    else
                    {
                        termsByBase[baseExpr] = coefficient;
                    }
                }

                var combinedNonNumericTerms = new List<IExpression>();
                combinedNonNumericTerms.AddRange(overflowTerms);

                foreach (var kvp in termsByBase)
                {
                    var baseExpr = kvp.Key;
                    var coefficient = kvp.Value;

                    if (coefficient == 0m)
                    {
                        continue;
                    }
                    if (coefficient == 1m)
                    {
                        combinedNonNumericTerms.Add(baseExpr);
                    }
                    else
                    {
                        combinedNonNumericTerms.Add(new Multiply(new Number(coefficient), baseExpr).Canonicalize());
                    }
                }

                // If all arguments were numeric, return a single Number.
                if (combinedNonNumericTerms.Count == 0)
                {
                    return new Number(numericSum);
                }

                // If there are non-numeric terms, and the numeric sum is not zero, add it.
                if (numericSum != 0m)
                {
                    combinedNonNumericTerms.Add(new Number(numericSum));
                }

                ImmutableList<IExpression> sortedArgs = ExpressionHelpers.SortArguments(combinedNonNumericTerms.ToImmutableList());

                if (sortedArgs.Count == 1)
                {
                    return sortedArgs[0];
                }

                // This case should ideally not be reached if initial nonNumericTerms.IsEmpty handled
                // and numericSum != 0m adds the Number. But as a defensive check for empty list after processing.
                if (sortedArgs.Count == 0)
                {
                    return new Number(0m); // e.g. if Add() was called with no arguments
                }

                // Return a new Add operation with the canonicalized and sorted arguments.
                return new Add(sortedArgs);
            
        }

        public override string ToDisplayString()
        {
            return $"({string.Join(" + ", Arguments.Select(arg => arg.ToDisplayString()))})";
        }

        public override bool InternalEquals(IExpression other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is not Add otherAdd) return false;
            return ExpressionHelpers.SequencesInternalEquals(this.Arguments, otherAdd.Arguments);
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
            return new Add(newArgs);
        }
    }
}
