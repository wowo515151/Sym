// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class MatMul : Operation
    {
        public MatMul(ImmutableList<IExpression> arguments) : base(arguments) { }
        public MatMul(IExpression left, IExpression right) : base(ImmutableList.Create(left, right)) { }

        public override Shape Shape
        {
            get
            {
                if (Arguments.Count != 2) return Shape.Error;
                var a = Arguments[0].Shape;
                var b = Arguments[1].Shape;
                if (!a.IsValid || !b.IsValid || a.IsWildcardShape || b.IsWildcardShape) return Shape.Error;

                if (a.IsScalar || b.IsScalar) return Shape.Error;

                var dimsA = a.Dimensions;
                var dimsB = b.Dimensions;

                // Handle Matrix-Matrix [..., M, K] x [..., K, N] -> [..., M, N]
                if (a.Dimensions.Length >= 2 && b.Dimensions.Length >= 2)
                {
                    int kA = dimsA[dimsA.Length - 1];
                    int kB = dimsB[dimsB.Length - 2];
                    if (kA != kB) return Shape.Error;

                    // Compute batch shape broadcasting
                    var batchA = new Shape(dimsA.Take(dimsA.Length - 2).ToImmutableArray());
                    var batchB = new Shape(dimsB.Take(dimsB.Length - 2).ToImmutableArray());
                    var batchResult = batchA.CombineForElementWise(batchB);
                    if (!batchResult.IsValid) return Shape.Error;

                    return new Shape(batchResult.Dimensions.Add(dimsA[dimsA.Length - 2]).Add(dimsB[dimsB.Length - 1]));
                }
                
                // Handle Matrix-Vector [..., M, K] x [..., K] -> [..., M]
                if (a.Dimensions.Length >= 2 && b.Dimensions.Length == 1)
                {
                    if (dimsA[dimsA.Length - 1] != dimsB[0]) return Shape.Error;
                    return new Shape(dimsA.Take(dimsA.Length - 1).ToImmutableArray());
                }

                // Handle Vector-Matrix/Tensor [K] x [K, ...] -> [...]
                if (a.Dimensions.Length == 1 && b.Dimensions.Length >= 2)
                {
                    if (dimsA[0] != dimsB[0]) return Shape.Error;
                    // result is [...]
                    return new Shape(dimsB.Skip(1).ToImmutableArray());
                }

                // Handle Vector-Vector [K] x [K] -> Scalar
                if (a.Dimensions.Length == 1 && b.Dimensions.Length == 1)
                {
                    if (dimsA[0] != dimsB[0]) return Shape.Error;
                    return Shape.Scalar;
                }

                return Shape.Error;
            }
        }

        public override IExpression Canonicalize()
        {
            var left = Arguments[0].Canonicalize();
            var right = Arguments[1].Canonicalize();
            if (ReferenceEquals(left, Arguments[0]) && ReferenceEquals(right, Arguments[1])) return this;
            return new MatMul(left, right);
        }

        public override string ToDisplayString() => $"MatMul({Arguments[0].ToDisplayString()}, {Arguments[1].ToDisplayString()})";
        
        public override bool InternalEquals(IExpression other) => other is MatMul m && ExpressionHelpers.SequencesInternalEquals(Arguments, m.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new MatMul(newArgs);
    }
}
