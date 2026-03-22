using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;
using System.Collections.Immutable;

namespace SymSolvers.Tests;

[TestClass]
public class NumericApproximationStrategyTests
{
    [TestMethod]
        [Timeout(10000)]
    public void ReturnsSuccessForCanonicalizableNumericExpression()
    {
        var strategy = new NumericApproximationStrategy();
        IExpression expr = new Add(new Number(1m), new Number(2m)); // Not yet canonicalized
        var context = new SolveContext();

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression is Number);
        Assert.IsTrue(result.ResultExpression.InternalEquals(new Number(3m)));
    }

    [TestMethod]
        [Timeout(10000)]
    public void ReturnsSuccessForAlreadyNumericExpression()
    {
        var strategy = new NumericApproximationStrategy();
        IExpression expr = new Number(5m);
        var context = new SolveContext();

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression is Number);
        Assert.IsTrue(result.ResultExpression.InternalEquals(new Number(5m)));
    }

    [TestMethod]
        [Timeout(10000)]
    public void ReturnsSuccessForVectorOfNumbers()
    {
        var strategy = new NumericApproximationStrategy();
        IExpression expr = new Vector(ImmutableList.Create<IExpression>(new Number(1m), new Number(2m), new Number(3m)));
        var context = new SolveContext();

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Vector));
    }
}
