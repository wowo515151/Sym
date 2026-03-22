// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.CSharpIO;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers.Tests;

[TestClass]
public class EquationSolverExtendedTests
{
    private static Symbol X => new("x");

    [TestMethod]
        [Timeout(10000)]
    public void PolynomialSolverFindsRationalRoot()
    {
        var eq = new Equality(
            new Add(new Power(X, new Number(2m)), new Multiply(new Number(-5m), X).Canonicalize(), new Number(6m)).Canonicalize(),
            new Number(0m));

        var context = new SolveContext(targetVariable: X, enableTracing: true);
        var result = new Sym.Core.Strategies.EquationSolverStrategy().Solve(eq, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);

        var formatted = CSharpIO.FormatExpr(result.ResultExpression!);
        Assert.IsTrue(formatted.Contains("x = 2") && formatted.Contains("x = 3"),
            $"Expected roots 2 and 3 but got {formatted}");
        Assert.IsNotNull(result.Trace);
        Assert.IsTrue(result.Trace!.Count >= 2);
    }

    [TestMethod]
        [Timeout(10000)]
    public void NumericSolverFindsSineRoot()
    {
        var eq = new Equality(
            new Function("sin", System.Collections.Immutable.ImmutableList.Create<IExpression>(X)),
            new Number(0m));

        var ctx = new SolveContext(
            targetVariable: X,
            additionalData: SolveContext.NormalizeAdditionalData(new Dictionary<string, object>
            {
                { "InitialGuess", 3 },
                { "NumericToleranceMicros", 100 }
            }),
            enableTracing: true);

        var result = new NewtonHybridStrategy().Solve(eq, ctx);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsInstanceOfType(result.ResultExpression, typeof(Equality));
        var numeric = (result.ResultExpression as Equality)!.RightOperand as Number;
        Assert.IsNotNull(numeric);
        Assert.IsTrue(decimal.Abs(numeric!.Value - 3.1415m) < 0.01m, $"Expected ~pi, got {numeric!.Value}");
    }
}
