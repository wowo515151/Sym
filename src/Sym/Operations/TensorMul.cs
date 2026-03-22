//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class TensorMul : Operation
    {
        public TensorMul(ImmutableList<IExpression> arguments) : base(arguments) { }
        public TensorMul(IExpression left, IExpression right) : base(ImmutableList.Create(left, right)) { }

        public override Shape Shape
        {
            get
            {
                if (Arguments.Count != 2) return Shape.Error;
                return Arguments[0].Shape.CombineForElementWise(Arguments[1].Shape);
            }
        }

        public override IExpression Canonicalize()
        {
            if (Arguments.Count != 2) return this;

            var left = Arguments[0].Canonicalize();
            var right = Arguments[1].Canonicalize();

            if (left is Number n1 && right is Number n2)
            {
                return new Number(n1.Value * n2.Value);
            }

            if (left is Number lNum)
            {
                if (lNum.Value == 0) return new Number(0);
                if (lNum.Value == 1) return right;
            }
            if (right is Number rNum)
            {
                if (rNum.Value == 0) return new Number(0);
                if (rNum.Value == 1) return left;
            }
            
            // Commutative sort
            if (string.CompareOrdinal(left.ToDisplayString(), right.ToDisplayString()) > 0)
            {
                var tmp = left; left = right; right = tmp;
            }

            if (ReferenceEquals(left, Arguments[0]) && ReferenceEquals(right, Arguments[1])) return this;
            return new TensorMul(left, right);
        }

        public override string ToDisplayString() 
        {
            if (Arguments.Count == 2)
                return $"TensorMul({Arguments[0].ToDisplayString()}, {Arguments[1].ToDisplayString()})";
            return $"TensorMul({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
        }
        
        public override bool InternalEquals(IExpression other) => other is TensorMul t && ExpressionHelpers.SequencesInternalEquals(Arguments, t.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new TensorMul(newArgs);
    }
}
