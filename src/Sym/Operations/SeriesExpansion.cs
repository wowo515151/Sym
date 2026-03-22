using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;

namespace Sym.Operations;

/// <summary>
/// Represents a truncated series expansion at a point.
/// </summary>
public sealed class SeriesExpansion : Operation
{
    public IExpression TargetExpression { get; }
    public Symbol Variable { get; }
    public IExpression Center { get; }
    public int Order { get; }

    public SeriesExpansion(IExpression targetExpression, Symbol variable, IExpression center, int order)
        : base(ImmutableList.Create(targetExpression, variable, center, new Number(order)))
    {
        TargetExpression = targetExpression;
        Variable = variable;
        Center = center;
        Order = order;
    }

    public override Shape Shape => TargetExpression.Shape;

    public override Operation WithArguments(ImmutableList<IExpression> newArgs)
    {
        var ord = newArgs[3] is Number n ? (int)n.Value : Order;
        return new SeriesExpansion(newArgs[0], (Symbol)newArgs[1], newArgs[2], ord);
    }

    public override IExpression Canonicalize()
    {
        var t = TargetExpression.Canonicalize();
        var v = (Symbol)Variable.Canonicalize();
        var c = Center.Canonicalize();
        if (!ReferenceEquals(t, TargetExpression) || !ReferenceEquals(v, Variable) || !ReferenceEquals(c, Center))
        {
            return new SeriesExpansion(t, v, c, Order);
        }
        return this;
    }

    public override string ToDisplayString()
    {
        return $"Series({TargetExpression.ToDisplayString()}, {Variable.ToDisplayString()} around {Center.ToDisplayString()}, order {Order})";
    }

    public override bool InternalEquals(IExpression other)
    {
        if (other is not SeriesExpansion s) return false;
        return TargetExpression.InternalEquals(s.TargetExpression)
            && Variable.InternalEquals(s.Variable)
            && Center.InternalEquals(s.Center)
            && Order == s.Order;
    }

    public override int InternalGetHashCode()
    {
        return HashCode.Combine(GetType(), TargetExpression, Variable, Center, Order);
    }
}
