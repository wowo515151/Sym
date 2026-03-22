// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class Relu : Operation
    {
        public Relu(ImmutableList<IExpression> arguments) : base(arguments) { }
        public Relu(IExpression target) : base(ImmutableList.Create(target)) { }

        public override Shape Shape => Arguments[0].Shape;

        public override IExpression Canonicalize()
        {
            var arg = Arguments[0].Canonicalize();
            if (ReferenceEquals(arg, Arguments[0])) return this;
            return new Relu(arg);
        }

        public override string ToDisplayString() => $"Relu({Arguments[0].ToDisplayString()})";
        
        public override bool InternalEquals(IExpression other) => other is Relu r && ExpressionHelpers.SequencesInternalEquals(Arguments, r.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Relu(newArgs);
    }
}
