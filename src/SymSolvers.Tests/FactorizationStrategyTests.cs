// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class FactorizationStrategyTests
{
    private static Symbol S(string name) => new(name);

    [TestMethod]
        [Timeout(10000)]
    public void FactorsCommonSymbol()
    {
        var expr = new Add(
            new Multiply(S("a"), S("x")).Canonicalize(),
            new Multiply(S("a"), S("y")).Canonicalize()).Canonicalize();

        var strat = new FactorizationStrategy();
        var result = strat.Solve(expr, new SolveContext());

        var expected = new Multiply(
            S("a"),
            new Add(S("x"), S("y")).Canonicalize()).Canonicalize();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
    }

    [TestMethod]
        [Timeout(10000)]
    public void ReturnsOriginalWhenNoCommonFactor()
    {
        var expr = new Add(S("x"), S("y")).Canonicalize();
        var strat = new FactorizationStrategy();

        var result = strat.Solve(expr, new SolveContext());

        Assert.IsTrue(result.ResultExpression!.InternalEquals(expr));
        Assert.AreEqual("No changes performed.", result.Message);
    }
}
