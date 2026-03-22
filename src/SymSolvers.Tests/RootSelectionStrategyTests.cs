using System.Collections.Immutable;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.CSharpIO;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public sealed class RootSelectionStrategyTests
{
    [TestMethod]
        [Timeout(10000)]
    public void SelectsMaximumRootWithPositiveConstraint()
    {
        var eq = CSharpIO.ParseExpressionsStrict("8 * Pow(x, 2) - 65 * x + 8 == 0").First();
        var gt = CSharpIO.ParseExpressionsStrict("x > 0").First();
        var system = new Vector(ImmutableList.Create(eq, gt)).Canonicalize();
        var options = ImmutableDictionary<string, object>.Empty.Add(SolverOptionKeys.RootSelectionMode, "max");
        var context = new SolveContext(targetVariable: new Symbol("x"), additionalData: options);

        var result = new RootSelectionStrategy().Solve(system, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Equality));

        var equality = (Equality)result.ResultExpression!;
        Assert.IsTrue(equality.LeftOperand.InternalEquals(new Symbol("x")));
        Assert.IsInstanceOfType(equality.RightOperand, typeof(Number));
        Assert.AreEqual(8m, ((Number)equality.RightOperand).Value);
    }

    [TestMethod]
        [Timeout(10000)]
    public void SelectsMinimumRootWhenRequested()
    {
        var eq = CSharpIO.ParseExpressionsStrict("8 * Pow(x, 2) - 65 * x + 8 == 0").First();
        var system = eq;
        var options = ImmutableDictionary<string, object>.Empty.Add(SolverOptionKeys.RootSelectionMode, "min");
        var context = new SolveContext(targetVariable: new Symbol("x"), additionalData: options);

        var result = new RootSelectionStrategy().Solve(system, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Equality));

        var equality = (Equality)result.ResultExpression!;
        Assert.IsTrue(equality.LeftOperand.InternalEquals(new Symbol("x")));
        Assert.IsInstanceOfType(equality.RightOperand, typeof(Number));
        Assert.AreEqual(0.125m, ((Number)equality.RightOperand).Value);
    }
}
