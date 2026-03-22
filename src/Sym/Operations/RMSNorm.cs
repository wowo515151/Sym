// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace Sym.Operations
{
    public sealed class RMSNorm : Operation
    {
        public RMSNorm(ImmutableList<IExpression> arguments) : base(arguments) { }
        public RMSNorm(IExpression input, IExpression weight) : base(ImmutableList.Create(input, weight)) { }
        public RMSNorm(IExpression input) : base(ImmutableList.Create(input)) { }

        public override Shape Shape => Arguments[0].Shape;

        public override IExpression Canonicalize()
        {
            var newArgs = Arguments.Select(a => a.Canonicalize()).ToImmutableList();
            if (ExpressionHelpers.SequencesInternalEquals(Arguments, newArgs)) return this;
            return new RMSNorm(newArgs);
        }

        public override string ToDisplayString()
        {
            if (Arguments.Count == 1) return $"RMSNorm({Arguments[0].ToDisplayString()})";
            return $"RMSNorm({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
        }
        
        public override bool InternalEquals(IExpression other) => other is RMSNorm r && ExpressionHelpers.SequencesInternalEquals(Arguments, r.Arguments);
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new RMSNorm(newArgs);
    }
}
