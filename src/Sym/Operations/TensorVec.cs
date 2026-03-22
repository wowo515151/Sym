//Copyright Warren Harding 2025.
using Sym.Core;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class TensorVec : Operation
    {
        public TensorVec(ImmutableList<IExpression> arguments) : base(arguments) { }
        public TensorVec(IExpression target) : base(ImmutableList.Create(target)) { }

        public override Shape Shape
        {
            get
            {
                if (Arguments.Count == 0) return Shape.Error;
                var input = Arguments[0].Shape;
                if (!input.IsValid) return Shape.Error;
                if (input.IsScalar) return input;
                long total = 1;
                foreach(var d in input.Dimensions) total *= d;
                return new Shape(ImmutableArray.Create((int)total));
            }
        }

        public override IExpression Canonicalize()
        {
            var arg = Arguments[0].Canonicalize();
            if (ReferenceEquals(arg, Arguments[0])) return this;
            return new TensorVec(arg);
        }

        public override string ToDisplayString() => $"vec({Arguments[0].ToDisplayString()})";
        
        public override bool InternalEquals(IExpression other) => other is TensorVec v && ExpressionHelpers.SequencesInternalEquals(Arguments, v.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new TensorVec(newArgs);
    }
}
