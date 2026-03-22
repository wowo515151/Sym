using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public sealed class ProperDivisorTests
{
    [TestMethod]
        [Timeout(10000)]
    public void TestSumProperDivisors_18_ShouldBe_21()
    {
        var fn = new Function("SumProperDivisors", new Number(18));
        var strategy = new FunctionSimplificationStrategy();
        var context = new SolveContext();
        var result = strategy.Solve(fn, context);
        
        Assert.IsTrue(result.IsSuccess);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Number));
        Assert.AreEqual(21m, ((Number)result.ResultExpression!).Value);
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestSumProperDivisors_198_ShouldBe_270()
    {
        var fn = new Function("SumProperDivisors", new Number(198));
        var strategy = new FunctionSimplificationStrategy();
        var context = new SolveContext();
        var result = strategy.Solve(fn, context);
        
        Assert.IsTrue(result.IsSuccess);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Number));
        Assert.AreEqual(270m, ((Number)result.ResultExpression!).Value);
    }
}
