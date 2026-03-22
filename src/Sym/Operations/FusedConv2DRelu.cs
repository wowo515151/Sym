//Copyright Warren Harding 2025.
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class FusedConv2DRelu : Operation
    {
        public FusedConv2DRelu(ImmutableList<IExpression> arguments) : base(arguments) { }
        public FusedConv2DRelu(IExpression input, IExpression filter, IExpression stride, IExpression padding) 
            : base(ImmutableList.Create(input, filter, stride, padding)) { }

        public override Shape Shape => Arguments[0].Shape; // Placeholder

        public override IExpression Canonicalize()
        {
            var args = Arguments.Select(a => a.Canonicalize()).ToImmutableList();
            if (ExpressionHelpers.SequencesInternalEquals(args, Arguments)) return this;
            return new FusedConv2DRelu(args);
        }

        public override string ToDisplayString() => $"FusedConv2DRelu({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
        public override bool InternalEquals(IExpression other) => other is FusedConv2DRelu f && ExpressionHelpers.SequencesInternalEquals(Arguments, f.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new FusedConv2DRelu(newArgs);
    }
}
