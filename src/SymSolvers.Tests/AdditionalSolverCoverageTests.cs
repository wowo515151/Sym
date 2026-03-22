// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;
using Sym.CSharpIO;

namespace SymSolvers.Tests;

[TestClass]
public class AdditionalSolverCoverageTests
{
    [TestMethod]
        [Timeout(10000)]
    public void PartialFractionStrategy_DecomposesRepeatedLinearFactors()
    {
        var strategy = new PartialFractionStrategy();
        var x = new Symbol("x");
        var program = CSharpIO.ParseProgram("1/(x*(x-1)^2)");
        Assert.IsFalse(program.HasErrors,
            "Parse errors: " + string.Join(" | ", program.Diagnostics.Select(d => $"{d.Severity}: {d.Message}")));
        Assert.AreEqual(1, program.Expressions.Count,
            "Expected exactly one parsed expression but got " + program.Expressions.Count);

        var expr = program.Expressions[0];

        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);

        var expected = new Add(ImmutableList.Create<IExpression>(
            new Divide(new Number(1m), x).Canonicalize(),
            new Divide(new Number(-1m), new Add(x, new Number(-1m)).Canonicalize()).Canonicalize(),
            new Divide(new Number(1m), new Power(new Add(x, new Number(-1m)).Canonicalize(), new Number(2m)).Canonicalize()).Canonicalize()
        )).Canonicalize();

        Assert.IsTrue(result.ResultExpression.InternalEquals(expected),
            $"Expected partial fraction decomposition to match {CSharpIO.FormatExpr(expected)} but got {CSharpIO.FormatExpr(result.ResultExpression)}");
        Assert.IsTrue(result.Trace?.Count > 0, "Trace should include decomposition steps.");
    }

    [TestMethod]
        [Timeout(10000)]
    public void NewtonHybridStrategy_SolvesLinearEquation()
    {
        var strategy = new NewtonHybridStrategy();
        var x = new Symbol("x");
        var equation = new Equality(x, new Number(5m)).Canonicalize();

        var additional = ImmutableDictionary<string, object>.Empty
            .Add("InitialGuess", 1)
            .Add("NumericToleranceMicros", 10);
        var context = new SolveContext(targetVariable: x, enableTracing: true, additionalData: additional);

        var result = strategy.Solve(equation, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);

        var expected = new Equality(x, new Number(5m)).Canonicalize();
        Assert.IsTrue(result.ResultExpression.InternalEquals(expected),
            $"Expected numeric solution x=5 but got {CSharpIO.FormatExpr(result.ResultExpression)}");
        Assert.IsTrue(result.Trace?.Count > 0, "Trace should include the numeric solve steps.");
    }

    [TestMethod]
        [Timeout(10000)]
    public void EquationSolverStrategy_IsolatesPolynomialRoot()
    {
        var strategy = new Sym.Core.Strategies.EquationSolverStrategy();
        var x = new Symbol("x");
        var equation = new Equality(
            new Power(new Add(x, new Number(-3m)).Canonicalize(), new Number(2m)).Canonicalize(),
            new Number(0m)).Canonicalize();

        var context = new SolveContext(targetVariable: x, enableTracing: true);
        var result = strategy.Solve(equation, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);

        var expected = new Equality(x, new Number(3m)).Canonicalize();
        Assert.IsTrue(result.ResultExpression.InternalEquals(expected),
            $"Expected polynomial root x=3 but got {CSharpIO.FormatExpr(result.ResultExpression)}");
        Assert.IsTrue(result.Trace?.Count > 0, "Trace should reflect polynomial solve path.");
    }
}
