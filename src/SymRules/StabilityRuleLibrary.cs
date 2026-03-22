using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

using CoreRule = Sym.Core.Rule;

namespace SymRules;

/// <summary>
/// Stability-focused rewrite rules kept separate from algebraic simplification.
/// Currently used by the stable-loss synthesis strategy; not included in default packs to preserve behavior.
/// </summary>
public static class StabilityRuleLibrary
{
    public static ImmutableList<CoreRule> Rules { get; } = ImmutableList.Create<CoreRule>(
        // log(1 + x) -> log1p(x)
        new CoreRule(
            new Function("log", ImmutableList.Create<IExpression>(
                new Add(new Number(1m), new Wild("x")).Canonicalize())),
            new Function("log1p", ImmutableList.Create<IExpression>(new Wild("x")))),

        // exp(x) - 1 -> expm1(x)
        new CoreRule(
            new Add(
                new Function("exp", ImmutableList.Create<IExpression>(new Wild("x"))),
                new Number(-1m)).Canonicalize(),
            new Function("expm1", ImmutableList.Create<IExpression>(new Wild("x")))),

        // exp(x) + (-1) may canonicalize to (-1) + exp(x); include commuted pattern explicitly.
        new CoreRule(
            new Add(
                new Number(-1m),
                new Function("exp", ImmutableList.Create<IExpression>(new Wild("x")))
            ).Canonicalize(),
            new Function("expm1", ImmutableList.Create<IExpression>(new Wild("x")))),

        // (exp(x) - 1) / x -> expm1(x) / x
        new CoreRule(
            new Divide(
                new Add(
                    new Function("exp", ImmutableList.Create<IExpression>(new Wild("x"))),
                    new Number(-1m)
                ).Canonicalize(),
                new Wild("x")
            ).Canonicalize(),
            new Divide(
                new Function("expm1", ImmutableList.Create<IExpression>(new Wild("x"))),
                new Wild("x")
            ).Canonicalize()),

        // Canonicalized form: (-1 + exp(x)) * x^-1 -> expm1(x) * x^-1
        new CoreRule(
            new Multiply(
                new Add(
                    new Number(-1m),
                    new Function("exp", ImmutableList.Create<IExpression>(new Wild("x")))
                ).Canonicalize(),
                new Power(new Wild("x"), new Number(-1m)).Canonicalize()
            ).Canonicalize(),
            new Multiply(
                new Function("expm1", ImmutableList.Create<IExpression>(new Wild("x"))),
                new Power(new Wild("x"), new Number(-1m)).Canonicalize()
            ).Canonicalize()),

        // Distributed canonical form: exp(x) * x^-1 + (-1) * x^-1 -> expm1(x) * x^-1
        new CoreRule(
            new Add(
                new Multiply(
                    new Function("exp", ImmutableList.Create<IExpression>(new Wild("x"))),
                    new Power(new Wild("x"), new Number(-1m)).Canonicalize()
                ).Canonicalize(),
                new Multiply(
                    new Number(-1m),
                    new Power(new Wild("x"), new Number(-1m)).Canonicalize()
                ).Canonicalize()
            ).Canonicalize(),
            new Multiply(
                new Function("expm1", ImmutableList.Create<IExpression>(new Wild("x"))),
                new Power(new Wild("x"), new Number(-1m)).Canonicalize()
            ).Canonicalize()),

        // log(exp(a) + exp(b)) -> logsumexp(a, b)
        new CoreRule(
            new Function("log", ImmutableList.Create<IExpression>(
                new Add(
                    new Function("exp", ImmutableList.Create<IExpression>(new Wild("a"))),
                    new Function("exp", ImmutableList.Create<IExpression>(new Wild("b")))
                ).Canonicalize())),
            new Function("logsumexp", ImmutableList.Create<IExpression>(new Wild("a"), new Wild("b")))),

        // log(1 + exp(x)) -> softplus(x)
        new CoreRule(
            new Function("log", ImmutableList.Create<IExpression>(
                new Add(
                    new Number(1m),
                    new Function("exp", ImmutableList.Create<IExpression>(new Wild("x")))
                ).Canonicalize())),
            new Function("softplus", ImmutableList.Create<IExpression>(new Wild("x")))),

        // log(exp(a) - exp(b)) -> b + log(expm1(a - b)) (stable for a > b)
        new CoreRule(
            new Function("log", ImmutableList.Create<IExpression>(
                new Add(
                    new Function("exp", ImmutableList.Create<IExpression>(new Wild("a"))),
                    new Multiply(
                        new Number(-1m),
                        new Function("exp", ImmutableList.Create<IExpression>(new Wild("b")))).Canonicalize()
                ).Canonicalize())),
            new Add(
                new Wild("b"),
                new Function("log", ImmutableList.Create<IExpression>(
                    new Function("expm1", ImmutableList.Create<IExpression>(
                        new Add(
                            new Wild("a"),
                            new Multiply(new Number(-1m), new Wild("b")).Canonicalize()
                        ).Canonicalize()))
                )))
            .Canonicalize())
    );
}
