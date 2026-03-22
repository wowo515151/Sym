//Copyright Warren Harding 2025.
using Sym.Core;
using System.Collections.Immutable;
using System;

namespace Sym.Operations
{
    public sealed class Kronecker : Operation
    {
        public Kronecker(ImmutableList<IExpression> arguments) : base(arguments) { }
        public Kronecker(IExpression left, IExpression right) : base(ImmutableList.Create(left, right)) { }

        public override Shape Shape
        {
            get
            {
                if (Arguments.Count != 2) return Shape.Error;
                var a = Arguments[0].Shape;
                var b = Arguments[1].Shape;
                if (!a.IsValid || !b.IsValid) return Shape.Error;

                var dims = new int[Math.Max(a.Dimensions.Length, b.Dimensions.Length)];
                for (int i = 0; i < dims.Length; i++)
                {
                    int da = i < a.Dimensions.Length ? a.Dimensions[a.Dimensions.Length - 1 - i] : 1;
                    int db = i < b.Dimensions.Length ? b.Dimensions[b.Dimensions.Length - 1 - i] : 1;
                    dims[dims.Length - 1 - i] = da * db;
                }
                return new Shape(dims.ToImmutableArray());
            }
        }

        public override IExpression Canonicalize()
        {
            var left = Arguments[0].Canonicalize();
            var right = Arguments[1].Canonicalize();
            if (ReferenceEquals(left, Arguments[0]) && ReferenceEquals(right, Arguments[1])) return this;
            return new Kronecker(left, right);
        }

        public override string ToDisplayString() => $"Kronecker({Arguments[0].ToDisplayString()}, {Arguments[1].ToDisplayString()})";
        
        public override bool InternalEquals(IExpression other) => other is Kronecker k && ExpressionHelpers.SequencesInternalEquals(Arguments, k.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Kronecker(newArgs);
    }
}
