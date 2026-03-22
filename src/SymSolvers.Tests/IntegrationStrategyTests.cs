// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Calculus;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class IntegrationStrategyTests
{
    private static readonly Symbol X = new("x");
    private static readonly Symbol Y = new("y");

    [TestMethod]
        [Timeout(10000)]
    public void NullContext_ReturnsFailure()
    {
        var integral = new Integral(new Number(1m), X);
        var result = new IntegrationStrategy().Solve(integral, null!);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message, "SolveContext cannot be null");
    }

    [TestMethod]
        [Timeout(10000)]
    public void NonIntegralInput_ReturnsFailure()
    {
        var result = new IntegrationStrategy().Solve(X, new SolveContext());

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message, "requires an Integral");
    }

    [TestMethod]
        [Timeout(10000)]
    public void VariableMustBeSymbol()
    {
        var integral = new Integral(new Number(2m), new Number(3m));
        var result = new IntegrationStrategy().Solve(integral, new SolveContext());

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message, "Integration variable must be a symbol");
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegratesSumWithConstants()
    {
        var integral = new Integral(new Add(new Multiply(new Number(3m), X), new Number(5m)), X);
        var result = new IntegrationStrategy().Solve(integral, new SolveContext());

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Add(
            new Multiply(new Number(1.5m), new Power(X, new Number(2m))),
            new Multiply(new Number(5m), X)).Canonicalize();
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegratesSymbolicConstantFactor()
    {
        var integral = new Integral(new Multiply(Y, X), X);
        var result = new IntegrationStrategy().Solve(integral, new SolveContext());

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Multiply(
            Y,
            new Multiply(new Power(X, new Number(2m)), new Number(0.5m))).Canonicalize();
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegratesPolynomialPowerViaRules()
    {
        var integral = new Integral(new Power(X, new Number(3m)), X);
        var result = new IntegrationStrategy().Solve(integral, new SolveContext());

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Multiply(new Number(0.25m), new Power(X, new Number(4m))).Canonicalize();
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IntegratesBasicTrigAndExp()
    {
        var sinIntegral = new Integral(new Function("sin", ImmutableList.Create<IExpression>(X)), X);
        var sinResult = new IntegrationStrategy().Solve(sinIntegral, new SolveContext());
        var expectedSin = new Multiply(new Number(-1m), new Function("cos", ImmutableList.Create<IExpression>(X))).Canonicalize();
        Assert.IsTrue(sinResult.IsSuccess, sinResult.Message);
        Assert.IsTrue(sinResult.ResultExpression!.InternalEquals(expectedSin));

        var expIntegral = new Integral(new Function("exp", ImmutableList.Create<IExpression>(X)), X);
        var expResult = new IntegrationStrategy().Solve(expIntegral, new SolveContext());
        var expectedExp = new Function("exp", ImmutableList.Create<IExpression>(X));
        Assert.IsTrue(expResult.IsSuccess, expResult.Message);
        Assert.IsTrue(expResult.ResultExpression!.InternalEquals(expectedExp));
    }
}
