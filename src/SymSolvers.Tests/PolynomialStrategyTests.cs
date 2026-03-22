// Copyright Warren Harding 2026
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.CSharpIO;
using Sym.Core;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class PolynomialStrategyTests
{
    private static Symbol X => new("x");

    [TestMethod]
        [Timeout(10000)]
    public void FactorsQuadraticIntoLinearTerms()
    {
        var expr = CSharpIO.ParseExpressions("x^2 - 5*x + 6")[0];
        var context = new SolveContext(
            targetVariable: X,
            enableTracing: true,
            additionalData: SolveContext.NormalizeAdditionalData(new Dictionary<string, object>
            {
                { SolverOptionKeys.EnableLinearFactorization, true }
            }));

        var result = new FactorizationStrategy().Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);
        var expected = CSharpIO.ParseExpressions("(x - 2)*(x - 3)")[0].Canonicalize();
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
        Assert.IsNotNull(result.Trace);
        Assert.AreEqual(2, result.Trace!.Count);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ExpandsDistinctLinearPartialFractions()
    {
        var expr = CSharpIO.ParseExpressions("2/(x^2 - 1)")[0];
        var result = new PartialFractionStrategy().Solve(expr, new SolveContext(targetVariable: X));

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);
        var expected = CSharpIO.ParseExpressions("1/(x - 1) - 1/(x + 1)")[0].Canonicalize();
        Assert.IsTrue(result.ResultExpression!.Canonicalize().InternalEquals(expected));
    }
}
