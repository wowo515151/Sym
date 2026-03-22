using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public sealed class PiecewiseInequalityTests
{
    [TestMethod]
        [Timeout(10000)]
    public void SolvesPiecewiseBranchesIndependently()
    {
        var x = new Symbol("x");
        var branch1 = new Function("lt", ImmutableList.Create<IExpression>(
            new Add(new Multiply(new Number(2m), x), new Number(-4m)), new Number(0m)));
        var guard1 = new Function("gt", ImmutableList.Create<IExpression>(x, new Number(0m)));
        var branch2 = new Function("lt", ImmutableList.Create<IExpression>(
            new Add(x, new Number(-1m)), new Number(0m)));
        var guard2 = new Function("lt", ImmutableList.Create<IExpression>(x, new Number(0m)));
        var piecewise = new Piecewise(branch1, guard1, branch2, guard2);

        var context = new SolveContext(x, ImmutableList<Rule>.Empty, maxIterations: 32, enableTracing: true);
        var strat = new InequalitySolveStrategy();
        var result = strat.Solve(piecewise, context);

        Assert.IsTrue(result.IsSuccess, $"Piecewise solve failed: {result.Message}");
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Piecewise));
        var solved = (Piecewise)result.ResultExpression!;
        Assert.AreEqual(4, solved.Arguments.Count);
        Assert.IsTrue(solved.Arguments[0] is Function);
        Assert.IsTrue(solved.Arguments[2] is Function);
    }

    [TestMethod]
        [Timeout(10000)]
    public void MonotoneLogTransformAddsGuard()
    {
        var x = new Symbol("x");
        var inequality = new Function("lt", ImmutableList.Create<IExpression>(
            new Function("log", ImmutableList.Create<IExpression>(x)), new Number(0m)));

        var context = new SolveContext(x, ImmutableList<Rule>.Empty, maxIterations: 32, enableTracing: true);
        var strat = new InequalitySolveStrategy();
        var result = strat.Solve(inequality, context);

        Assert.IsTrue(result.IsSuccess, $"Monotone solve failed: {result.Message}");
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Function));
        var andFn = (Function)result.ResultExpression!;
        Assert.AreEqual("and", andFn.Name.ToLowerInvariant());
        Assert.AreEqual(2, andFn.Arguments.Count);
    }
}
