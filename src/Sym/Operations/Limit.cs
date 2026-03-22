// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;

namespace Sym.Operations;

/// <summary>
/// Represents Limit(expression, variable, approachValue).
/// </summary>
public sealed class Limit : Operation
{
    public IExpression TargetExpression { get; }
    public Symbol Variable { get; }
    public IExpression Approach { get; }

    public Limit(IExpression targetExpression, Symbol variable, IExpression approach)
        : base(ImmutableList.Create(targetExpression, variable, approach))
    {
        TargetExpression = targetExpression;
        Variable = variable;
        Approach = approach;
    }

    public override Shape Shape => TargetExpression.Shape;

    public override Operation WithArguments(ImmutableList<IExpression> newArgs)
    {
        return new Limit(newArgs[0], (Symbol)newArgs[1], newArgs[2]);
    }

    public override IExpression Canonicalize()
    {
        var t = TargetExpression.Canonicalize();
        var v = (Symbol)Variable.Canonicalize();
        var a = Approach.Canonicalize();
        if (!ReferenceEquals(t, TargetExpression) || !ReferenceEquals(v, Variable) || !ReferenceEquals(a, Approach))
        {
            return new Limit(t, v, a);
        }
        return this;
    }

    public override string ToDisplayString()
    {
        return $"Limit({TargetExpression.ToDisplayString()}, {Variable.ToDisplayString()} -> {Approach.ToDisplayString()})";
    }

    public override bool InternalEquals(IExpression other)
    {
        if (other is not Limit l) return false;
        return TargetExpression.InternalEquals(l.TargetExpression)
            && Variable.InternalEquals(l.Variable)
            && Approach.InternalEquals(l.Approach);
    }

    public override int InternalGetHashCode()
    {
        return HashCode.Combine(GetType(), TargetExpression, Variable, Approach);
    }
}
