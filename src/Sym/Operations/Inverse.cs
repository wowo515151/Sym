// Copyright Warren Harding 2026
using Sym.Core;
using System.Collections.Immutable;
using System;

namespace Sym.Operations
{
    public sealed class Inverse : Operation
    {
        public override string Head => "inverse";
        public Inverse(ImmutableList<IExpression> arguments) : base(arguments) { }
        public Inverse(IExpression target) : base(ImmutableList.Create(target)) { }

        public override Shape Shape
        {
            get
            {
                if (Arguments.Count != 1) return Shape.Error;
                var input = Arguments[0].Shape;
                if (!input.IsValid) return Shape.Error;
                // Matrix inverse preserves shape (must be square, but we don't strictly enforce here unless dimensions known)
                return input;
            }
        }

        public override IExpression Canonicalize()
        {
            var arg = Arguments[0].Canonicalize();
            if (arg is Inverse inv) return inv.Arguments[0]; // inverse(inverse(A)) -> A
            if (ReferenceEquals(arg, Arguments[0])) return this;
            return new Inverse(arg);
        }

        public override string ToDisplayString() => $"inverse({Arguments[0].ToDisplayString()})";
        
        public override bool InternalEquals(IExpression other) => other is Inverse inv && ExpressionHelpers.SequencesInternalEquals(Arguments, inv.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Inverse(newArgs);
    }
}
