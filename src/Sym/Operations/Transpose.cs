// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class Transpose : Operation
    {
        public Transpose(ImmutableList<IExpression> arguments) : base(arguments) { }
        public Transpose(IExpression target) : base(ImmutableList.Create(target)) { }
        public Transpose(IExpression target, IExpression permutation) : base(ImmutableList.Create(target, permutation)) { }

        public override Shape Shape
        {
            get
            {
                var input = Arguments[0].Shape;
                if (!input.IsValid) return Shape.Error;
                if (input.IsScalar) return input;

                if (Arguments.Count == 2 && Arguments[1] is Vector v)
                {
                    // Permutation-aware shape
                    var dims = input.Dimensions;
                    var newDims = new int[dims.Length];
                    for (int i = 0; i < dims.Length; i++)
                    {
                        if (v.Arguments[i] is Number n)
                        {
                            int idx = (int)n.Value;
                            if (idx >= 0 && idx < dims.Length) newDims[i] = dims[idx];
                            else return Shape.Error;
                        }
                        else return Shape.Wildcard; // Cannot infer if permutation is symbolic
                    }
                    return new Shape(newDims.ToImmutableArray());
                }

                // Default: reverse dimensions
                return new Shape(input.Dimensions.Reverse().ToImmutableArray());
            }
        }

        public override IExpression Canonicalize()
        {
            var arg = Arguments[0].Canonicalize();
            if (Arguments.Count == 1)
            {
                if (arg is Transpose t && t.Arguments.Count == 1) return t.Arguments[0]; // Transpose(Transpose(A)) -> A
            }
            
            bool changed = !ReferenceEquals(arg, Arguments[0]);
            var newArgs = new List<IExpression> { arg };
            if (Arguments.Count == 2)
            {
                var perm = Arguments[1].Canonicalize();
                newArgs.Add(perm);
                if (!ReferenceEquals(perm, Arguments[1])) changed = true;
            }

            if (!changed) return this;
            return new Transpose(newArgs.ToImmutableList());
        }

        public override string ToDisplayString()
        {
            if (Arguments.Count == 2) return $"Transpose({Arguments[0].ToDisplayString()}, {Arguments[1].ToDisplayString()})";
            return $"Transpose({Arguments[0].ToDisplayString()})";
        }
        
        public override bool InternalEquals(IExpression other) => other is Transpose tr && ExpressionHelpers.SequencesInternalEquals(Arguments, tr.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Transpose(newArgs);
    }
}
