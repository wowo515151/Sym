//Copyright Warren Harding 2025.
using System.Collections.Immutable;
using Sym.Core;

namespace Sym.Operations
{
    public sealed class Edge : Operation
    {
        public Edge(IExpression source, IExpression target) : base(ImmutableList.Create(source, target)) { }
        public Edge(ImmutableList<IExpression> arguments) : base(arguments) { }

        public override Shape Shape => Shape.Scalar;
        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new Edge(newArgs);
        public override int InternalGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Head);
            foreach (var arg in Arguments) hash.Add(arg.InternalGetHashCode());
            return hash.ToHashCode();
        }

        public override IExpression Canonicalize() => new Edge(Arguments.Select(a => a.Canonicalize()).ToImmutableList());
        public override string ToDisplayString() => $"Edge({Arguments[0].ToDisplayString()} -> {Arguments[1].ToDisplayString()})";
    }

    public sealed class GraphOp : Operation
    {
        public GraphOp(ImmutableList<IExpression> edges) : base(edges) { }
        public GraphOp(params IExpression[] edges) : base(ImmutableList.Create(edges)) { }

        public override string Head => "Graph";
        public override Shape Shape => Shape.Scalar;

        public override Operation WithArguments(ImmutableList<IExpression> newArgs) => new GraphOp(newArgs);
        public override int InternalGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Head);
            foreach (var arg in Arguments) hash.Add(arg.InternalGetHashCode());
            return hash.ToHashCode();
        }

        public override IExpression Canonicalize() => new GraphOp(Arguments.Select(a => a.Canonicalize()).ToImmutableList());
        public override string ToDisplayString() => $"Graph({string.Join(", ", Arguments.Select(a => a.ToDisplayString()))})";
    }
}
