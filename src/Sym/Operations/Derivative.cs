//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    /// <summary>
    /// Represents a symbolic differentiation operation: Derivative(expression, variable).
    /// </summary>
    public sealed class Derivative : Operation
    {
        /// <summary>
        /// The expression to be differentiated.
        /// </summary>
        public IExpression TargetExpression { get; init; }
        /// <summary>
        /// The variable with respect to which the differentiation is performed.
        /// </summary>
        public IExpression Variable { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Derivative"/> class.
        /// </summary>
        /// <param name="targetExpression">The expression to be differentiated.</param>
        /// <param name="variable">The variable of differentiation.</param>
        public Derivative(IExpression targetExpression, IExpression variable)
            : base(ImmutableList.Create(targetExpression, variable))
        {
            TargetExpression = targetExpression;
            Variable = variable;
        }

        // Parameterless constructor added to support reflective instantiation in unit tests.
        public Derivative() : base(ImmutableList.Create<IExpression>(new Sym.Atoms.Symbol(), new Sym.Atoms.Symbol()))
        {
            TargetExpression = new Sym.Atoms.Symbol();
            Variable = new Sym.Atoms.Symbol();
        }

        public override Shape Shape => TargetExpression.Shape; // The shape of the derivative is the same as the original expression

        /// <summary>
        /// Canonicalizes the derivative expression by canonicalizing its target expression and variable.
        /// Also applies basic differentiation identities like the sum and product rules.
        /// </summary>
        /// <returns>A canonicalized expression.</returns>
        public override IExpression Canonicalize()
        {
            IExpression canonicalTarget = TargetExpression.Canonicalize();
            IExpression canonicalVariable = Variable.Canonicalize();

            if (canonicalTarget is Add add)
            {
                // Sum Rule: d/dx (f + g + ...) = d/dx(f) + d/dx(g) + ...
                var differentiatedArgs = add.Arguments
                    .Select(arg => new Derivative(arg, canonicalVariable).Canonicalize())
                    .ToImmutableList();
                return new Add(differentiatedArgs).Canonicalize();
            }

            if (canonicalTarget is Multiply mul && mul.Arguments.Count > 0)
            {
                // Product Rule: d/dx (f * g * h) = f'gh + fg'h + fgh'
                // Implemented recursively: d/dx (f * rest) = f' * rest + f * d/dx(rest)
                var f = mul.Arguments[0];
                var rest = mul.Arguments.Count == 1 ? null : new Multiply(mul.Arguments.RemoveAt(0)).Canonicalize();

                if (rest == null)
                {
                    return new Derivative(f, canonicalVariable).Canonicalize();
                }

                var term1 = new Multiply(new Derivative(f, canonicalVariable).Canonicalize(), rest).Canonicalize();
                var term2 = new Multiply(f, new Derivative(rest, canonicalVariable).Canonicalize()).Canonicalize();
                return new Add(term1, term2).Canonicalize();
            }

            if (!ReferenceEquals(canonicalTarget, TargetExpression) || !ReferenceEquals(canonicalVariable, Variable))
            {
                return new Derivative(canonicalTarget, canonicalVariable);
            }
            return this;
        }

        /// <summary>
        /// Provides a string representation of the derivative operation.
        /// </summary>
        /// <returns>A string representing the derivative.</returns>
        public override string ToDisplayString()
        {
            return $"Derivative({TargetExpression.ToDisplayString()}, {Variable.ToDisplayString()})";
        }

        /// <summary>
        /// Determines whether this derivative expression is internally equal to another expression.
        /// </summary>
        /// <param name="other">The other expression to compare with.</param>
        /// <returns>True if expressions are internally equal, false otherwise.</马克
        public override bool InternalEquals(IExpression other)
        {
            if (other is not Derivative otherDerivative)
            {
                return false;
            }
            return TargetExpression.InternalEquals(otherDerivative.TargetExpression) &&
                   Variable.InternalEquals(otherDerivative.Variable);
        }

        /// <summary>
        /// Returns the internal hash code for this derivative expression.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int InternalGetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(GetType());
            hash.Add(TargetExpression.InternalGetHashCode());
            hash.Add(Variable.InternalGetHashCode());
            return hash.ToHashCode();
        }

        public override Operation WithArguments(ImmutableList<IExpression> newArgs)
        {
            return new Derivative(newArgs[0], newArgs[1]);
        }
    }
}


