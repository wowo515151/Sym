using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;

namespace Sym.Operations;

/// <summary>
/// Represents a definite integral Integral(f, x, a, b).
/// </summary>
public sealed class DefiniteIntegral : Operation
{
    public IExpression TargetExpression { get; }
    public Symbol Variable { get; }
    public IExpression LowerBound { get; }
    public IExpression UpperBound { get; }

    public DefiniteIntegral(IExpression targetExpression, Symbol variable, IExpression lowerBound, IExpression upperBound)
        : base(ImmutableList.Create(targetExpression, variable, lowerBound, upperBound))
    {
        TargetExpression = targetExpression;
        Variable = variable;
        LowerBound = lowerBound;
        UpperBound = upperBound;
    }

    public override Shape Shape => TargetExpression.Shape;

    public override Operation WithArguments(ImmutableList<IExpression> newArgs)
    {
        return new DefiniteIntegral(newArgs[0], (Symbol)newArgs[1], newArgs[2], newArgs[3]);
    }

    public override IExpression Canonicalize()
    {
        var t = TargetExpression.Canonicalize();
        var v = (Symbol)Variable.Canonicalize();
        var l = LowerBound.Canonicalize();
        var u = UpperBound.Canonicalize();
        if (!ReferenceEquals(t, TargetExpression) || !ReferenceEquals(v, Variable) || !ReferenceEquals(l, LowerBound) || !ReferenceEquals(u, UpperBound))
        {
            return new DefiniteIntegral(t, v, l, u);
        }
        return this;
    }

    public override string ToDisplayString()
    {
        return $"DefiniteIntegral({TargetExpression.ToDisplayString()}, {Variable.ToDisplayString()}, {LowerBound.ToDisplayString()}, {UpperBound.ToDisplayString()})";
    }

    public override bool InternalEquals(IExpression other)
    {
        if (other is not DefiniteIntegral di) return false;
        return TargetExpression.InternalEquals(di.TargetExpression)
            && Variable.InternalEquals(di.Variable)
            && LowerBound.InternalEquals(di.LowerBound)
            && UpperBound.InternalEquals(di.UpperBound);
    }

    public override int InternalGetHashCode()
    {
        return HashCode.Combine(GetType(), TargetExpression, Variable, LowerBound, UpperBound);
    }
}
