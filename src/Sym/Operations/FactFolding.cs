// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Sym.Core;

namespace Sym.Operations
{
    public sealed class ListOp : Operation
    {
        public ListOp(ImmutableList<IExpression> arguments) : base(arguments) { }
        public ListOp(params IExpression[] arguments) : base(ImmutableList.Create(arguments)) { }

        public override string Head => "List";
        public override Shape Shape => Shape.Scalar;

        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new ListOp(newArgs);
        public override int InternalGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Head);
            foreach (var arg in Arguments) hash.Add(arg.InternalGetHashCode());
            return hash.ToHashCode();
        }

        public override IExpression Canonicalize() => new ListOp(Arguments.Select(a => a.Canonicalize()).ToImmutableList());
        public override string ToDisplayString() => $"List({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
    }

    public sealed class Summary : Operation
    {
        public Summary(ImmutableList<IExpression> arguments) : base(arguments) { }
        public Summary(params IExpression[] arguments) : base(ImmutableList.Create(arguments)) { }

        public override Shape Shape => Shape.Scalar;

        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Summary(newArgs);
        
        public override int InternalGetHashCode() => ExpressionHelpers.GetSequenceHashCode(GetType(), Arguments);

        public override IExpression Canonicalize()
        {
            var newArgs = Arguments.Select(a => a.Canonicalize()).ToImmutableList();
            var flattened = ExpressionHelpers.FlattenArguments<Summary>(newArgs);
            var sorted = ExpressionHelpers.SortArguments(flattened);

            if (ExpressionHelpers.SequencesInternalEquals(Arguments, sorted)) return this;
            return new Summary(sorted);
        }

        public override string ToDisplayString() => $"Summary({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
    }
}