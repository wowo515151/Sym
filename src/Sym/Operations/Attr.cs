using System.Collections.Immutable;
using System.Linq;
using Sym.Core;

namespace Sym.Operations
{
    /// <summary>
    /// Accesses a metadata attribute of an expression.
    /// Usage: Attr(Target, AttributeName)
    /// </summary>
    public sealed class Attr : Operation
    {
        public IExpression Target => Arguments[0];
        public IExpression AttributeName => Arguments[1];

        public Attr(ImmutableList<IExpression> arguments) : base(arguments) { }

        public Attr(IExpression target, IExpression attributeName)
            : base(ImmutableList.Create(target, attributeName)) { }

        public Attr(IExpression target, string attributeName)
            : base(ImmutableList.Create(target, new Atoms.Symbol(attributeName))) { }

        public override string Head => "Attr";

        public override string ToDisplayString()
        {
            return $"Attr({Target.ToDisplayString()}, {AttributeName.ToDisplayString()})";
        }

        public override bool InternalEquals(IExpression other)
        {
            return other is Attr a && ExpressionHelpers.SequencesInternalEquals(Arguments, a.Arguments);
        }

        public override Operation WithArguments(ImmutableList<IExpression> newArgs)
        {
            return new Attr(newArgs);
        }

        public override IExpression Canonicalize()
        {
            return new Attr(Target.Canonicalize(), AttributeName.Canonicalize());
        }

        public override Shape Shape => Shape.Scalar;

        public override int InternalGetHashCode()
        {
            return System.HashCode.Combine(Head, Target, AttributeName);
        }
    }
}
