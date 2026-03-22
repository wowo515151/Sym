//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class Conv2D : Operation
    {
        // Arguments: Input, Filter, Stride, Padding
        public Conv2D(ImmutableList<IExpression> arguments) : base(arguments) { }
        public Conv2D(IExpression input, IExpression filter, IExpression stride, IExpression padding) 
            : base(ImmutableList.Create(input, filter, stride, padding)) { }

        public override Shape Shape
        {
            get
            {
                // Placeholder logic: Output H/W depends on formula.
                // Assuming standard convolution shape transformation.
                var inputShape = Arguments[0].Shape;
                if (!inputShape.IsValid || inputShape.Dimensions.Length < 3) return Shape.Error;
                // Return same rank as input for now
                return inputShape; 
            }
        }

        public override IExpression Canonicalize()
        {
            var args = Arguments.Select(a => a.Canonicalize()).ToImmutableList();
            if (ExpressionHelpers.SequencesInternalEquals(args, Arguments)) return this;
            return new Conv2D(args);
        }

        public override string ToDisplayString() => $"Conv2D({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
        
        public override bool InternalEquals(IExpression other) => other is Conv2D c && ExpressionHelpers.SequencesInternalEquals(Arguments, c.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Conv2D(newArgs);
    }
}
