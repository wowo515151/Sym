// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    /// <summary>
    /// Concatenates tensors along a specified axis.
    /// Arguments: [axis, tensor1, tensor2, ...]
    /// </summary>
    public sealed class Concat : Operation
    {
        public Concat(ImmutableList<IExpression> arguments) : base(arguments) { }

        public override Shape Shape
        {
            get
            {
                if (Arguments.Count < 2) return Shape.Error;
                if (Arguments[0] is not Number axisNum) return Shape.Error;
                int axis = (int)axisNum.Value;

                var tensors = Arguments.Skip(1).ToList();
                if (tensors.Any(t => !t.Shape.IsValid || t.Shape.IsWildcardShape)) return Shape.Error;

                var firstShape = tensors[0].Shape;
                int rank = firstShape.Dimensions.Length;
                if (axis < 0) axis += rank;
                if (axis < 0 || axis >= rank) return Shape.Error;

                int concatDim = 0;
                foreach (var t in tensors)
                {
                    var s = t.Shape;
                    if (s.Dimensions.Length != rank) return Shape.Error;
                    for (int i = 0; i < rank; i++)
                    {
                        if (i == axis) concatDim += s.Dimensions[i];
                        else if (s.Dimensions[i] != firstShape.Dimensions[i]) return Shape.Error;
                    }
                }

                var resultDims = firstShape.Dimensions.ToBuilder();
                resultDims[axis] = concatDim;
                return new Shape(resultDims.ToImmutable());
            }
        }

        public override IExpression Canonicalize()
        {
            if (Arguments.Count < 2) return this;
            var newArgs = Arguments.Select(a => a.Canonicalize()).ToImmutableList();
            if (ReferenceEquals(newArgs, Arguments)) return this;
            return new Concat(newArgs);
        }

        public override string ToDisplayString() => $"Concat({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
        
        public override bool InternalEquals(IExpression other) => other is Concat c && ExpressionHelpers.SequencesInternalEquals(Arguments, c.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Concat(newArgs);
    }
}
