// Copyright Warren Harding 2026
using Sym.Atoms;
using Sym.Core;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    /// <summary>
    /// Reduces a tensor to a scalar by summing all elements.
    /// </summary>
    public sealed class Sum : Operation
    {
        public Sum(ImmutableList<IExpression> arguments) : base(arguments) { }
        public Sum(IExpression argument) : base(ImmutableList.Create(argument)) { }

        public override Shape Shape => Shape.Scalar;

        public override IExpression Canonicalize()
        {
            if (Arguments.Count != 1) return this;
            var arg = Arguments[0].Canonicalize();
            if (ReferenceEquals(arg, Arguments[0])) return this;
            return new Sum(arg);
        }

        public override string ToDisplayString() => $"Sum({Arguments[0].ToDisplayString()})";
        
        public override bool InternalEquals(IExpression other) => other is Sum s && ExpressionHelpers.SequencesInternalEquals(Arguments, s.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Sum(newArgs);
    }
}
