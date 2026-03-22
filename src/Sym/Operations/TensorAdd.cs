//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class TensorAdd : Operation
    {
        public TensorAdd(ImmutableList<IExpression> arguments) : base(arguments) { }
        public TensorAdd(IExpression left, IExpression right) : base(ImmutableList.Create(left, right)) { }

        public override Shape Shape
        {
            get
            {
                if (Arguments.Count == 0) return Shape.Scalar;
                Shape result = Arguments[0].Shape;
                for (int i = 1; i < Arguments.Count; i++)
                {
                    result = result.CombineForElementWise(Arguments[i].Shape);
                    if (!result.IsValid) return Shape.Error;
                }
                return result;
            }
        }

        public override IExpression Canonicalize()
        {
            if (Arguments.Count != 2) return this;

            var left = Arguments[0].Canonicalize();
            var right = Arguments[1].Canonicalize();

            if (left is Number n1 && n1.Value == 0) return right;
            if (right is Number n2 && n2.Value == 0) return left;
            
            // Commutative sort
            if (string.CompareOrdinal(left.ToDisplayString(), right.ToDisplayString()) > 0)
            {
                var tmp = left; left = right; right = tmp;
            }

            if (left.InternalEquals(right))
            {
                return new TensorMul(left, new Number(2m)).Canonicalize();
            }

            if (ReferenceEquals(left, Arguments[0]) && ReferenceEquals(right, Arguments[1])) return this;
            return new TensorAdd(left, right);
        }

        public override string ToDisplayString() 
        {
            if (Arguments.Count == 2)
                return $"TensorAdd({Arguments[0].ToDisplayString()}, {Arguments[1].ToDisplayString()})";
            return $"TensorAdd({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
        }
        
        public override bool InternalEquals(IExpression other) => other is TensorAdd t && ExpressionHelpers.SequencesInternalEquals(Arguments, t.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new TensorAdd(newArgs);
    }
}
