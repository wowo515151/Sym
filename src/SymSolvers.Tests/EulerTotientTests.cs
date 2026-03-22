using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class EulerTotientTests
{
    [TestMethod]
        [Timeout(10000)]
    public void TestEulerTotient_Basic()
    {
        var strat = new NumberTheoryStrategy();
        var context = new SolveContext();

        // phi(12) = 4
        var res1 = strat.Solve(new Function("EulerTotient", new Number(12)), context);
        Assert.IsTrue(res1.IsSuccess);
        Assert.IsNotNull(res1.ResultExpression);
        Assert.AreEqual(4m, ((Number)res1.ResultExpression).Value);

        // phi(13) = 12
        var res2 = strat.Solve(new Function("phi", new Number(13)), context);
        Assert.IsTrue(res2.IsSuccess);
        Assert.IsNotNull(res2.ResultExpression);
        Assert.AreEqual(12m, ((Number)res2.ResultExpression).Value);

        // phi(1) = 1
        var res3 = strat.Solve(new Function("φ", new Number(1)), context);
        Assert.IsTrue(res3.IsSuccess);
        Assert.IsNotNull(res3.ResultExpression);
        Assert.AreEqual(1m, ((Number)res3.ResultExpression).Value);
    }
}
