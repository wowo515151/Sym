// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class FusedMatMulAdd : Operation
    {
        public FusedMatMulAdd(ImmutableList<IExpression> arguments) : base(arguments) { }
        public FusedMatMulAdd(IExpression a, IExpression b, IExpression c) : base(ImmutableList.Create(a, b, c)) { }

        public override Shape Shape
        {
            get
            {
                var a = Arguments[0].Shape;
                var b = Arguments[1].Shape;
                var c = Arguments[2].Shape;

                if (!a.IsValid || !b.IsValid || !c.IsValid) return Shape.Error;

                // Compute MatMul(A, B) shape
                Shape mulShape = Shape.Error;
                if (a.IsMatrix && b.IsMatrix && a.Dimensions[1] == b.Dimensions[0])
                {
                    mulShape = new Shape(ImmutableArray.Create(a.Dimensions[0], b.Dimensions[1]));
                }
                else if (a.IsScalar) mulShape = b;
                else if (b.IsScalar) mulShape = a;
                // Add more cases (Vector-Matrix) as needed matching MatMul logic

                if (!mulShape.IsValid) return Shape.Error;

                return mulShape.CombineForElementWise(c);
            }
        }

        public override IExpression Canonicalize()
        {
            var args = Arguments.Select(a => a.Canonicalize()).ToImmutableList();
            if (ExpressionHelpers.SequencesInternalEquals(args, Arguments)) return this;
            return new FusedMatMulAdd(args);
        }

        public override string ToDisplayString() => $"FusedMatMulAdd({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
        public override bool InternalEquals(IExpression other) => other is FusedMatMulAdd f && ExpressionHelpers.SequencesInternalEquals(Arguments, f.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new FusedMatMulAdd(newArgs);
    }
}
