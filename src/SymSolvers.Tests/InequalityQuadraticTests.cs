// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public sealed class InequalityQuadraticTests
{
    [TestMethod]
        [Timeout(10000)]
    public void SolvesQuadraticLessThanZeroToInterval()
    {
        var x = new Symbol("x");
        var expr = new Function("lt", ImmutableList.Create<IExpression>(new Add(new Power(x, new Number(2m)), new Number(-1m)), new Number(0m)));
        var context = new SolveContext(x, ImmutableList<Rule>.Empty, maxIterations: 16, enableTracing: true);
        var strategy = new InequalitySolveStrategy();

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, $"Failed: {result.Message}");
        Assert.IsNotNull(result.ResultExpression);
        var interval = result.ResultExpression as Function;
        Assert.IsNotNull(interval);
        Assert.AreEqual("interval", interval!.Name.ToLowerInvariant());
        Assert.AreEqual(new Number(-1m).ToDisplayString(), interval.Arguments[0].ToDisplayString());
        Assert.AreEqual(new Number(1m).ToDisplayString(), interval.Arguments[1].ToDisplayString());
    }

    [TestMethod]
        [Timeout(10000)]
    public void SolvesQuadraticGreaterThanZeroToUnion()
    {
        var x = new Symbol("x");
        var expr = new Function("ge", ImmutableList.Create<IExpression>(new Add(new Power(x, new Number(2m)), new Number(-4m)), new Number(0m)));
        var context = new SolveContext(x, ImmutableList<Rule>.Empty, maxIterations: 16, enableTracing: true);
        var strategy = new InequalitySolveStrategy();

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, $"Failed: {result.Message}");
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Function));
        var or = (Function)result.ResultExpression!;
        Assert.AreEqual("or", or.Name.ToLowerInvariant());
        Assert.AreEqual(2, or.Arguments.Count);
        Assert.IsTrue(or.Arguments[0] is Function);
        Assert.IsTrue(or.Arguments[1] is Function);
    }
}
