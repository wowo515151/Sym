//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    /// <summary>
    /// Stacks tensors along a NEW specified axis.
    /// Arguments: [axis, tensor1, tensor2, ...]
    /// </summary>
    public sealed class Stack : Operation
    {
        public Stack(ImmutableList<IExpression> arguments) : base(arguments) { }

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
                
                // New rank will be rank + 1
                if (axis < 0) axis += rank + 1;
                if (axis < 0 || axis > rank) return Shape.Error;

                foreach (var t in tensors)
                {
                    if (!t.Shape.Equals(firstShape)) return Shape.Error;
                }

                var resultDims = firstShape.Dimensions.ToBuilder();
                resultDims.Insert(axis, tensors.Count);
                return new Shape(resultDims.ToImmutable());
            }
        }

        public override IExpression Canonicalize()
        {
            if (Arguments.Count < 2) return this;
            var newArgs = Arguments.Select(a => a.Canonicalize()).ToImmutableList();
            if (ReferenceEquals(newArgs, Arguments)) return this;
            return new Stack(newArgs);
        }

        public override string ToDisplayString() => $"Stack({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
        
        public override bool InternalEquals(IExpression other) => other is Stack s && ExpressionHelpers.SequencesInternalEquals(Arguments, s.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Stack(newArgs);
    }
}
