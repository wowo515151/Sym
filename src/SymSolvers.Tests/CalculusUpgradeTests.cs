// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class CalculusUpgradeTests
{
    private static readonly Symbol X = new("x");

    [TestMethod]
        [Timeout(10000)]
    public void Integration_InverseHyperbolicLinear()
    {
        var integrand = new Divide(
            new Number(1m),
            new Power(new Add(new Power(X, new Number(2m)).Canonicalize(), new Number(4m)).Canonicalize(), new Number(0.5m)).Canonicalize()).Canonicalize();
        var result = new IntegrationStrategy().Solve(new Integral(integrand, X), new SolveContext(targetVariable: X));

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = new Function("asinh", ImmutableList.Create<IExpression>(new Divide(X, new Number(2m)).Canonicalize())).Canonicalize();
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
    }

    [TestMethod]
        [Timeout(10000)]
    public void Integration_ExpTrigLaplacePair()
    {
        var expArg = new Multiply(new Number(-2m), X).Canonicalize();
        var trigArg = new Multiply(new Number(3m), X).Canonicalize();
        var integrand = new Multiply(
            new Function("exp", ImmutableList.Create<IExpression>(expArg)).Canonicalize(),
            new Function("sin", ImmutableList.Create<IExpression>(trigArg)).Canonicalize()).Canonicalize();

        var result = new IntegrationStrategy().Solve(new Integral(integrand, X), new SolveContext(targetVariable: X));

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expTerm = new Function("exp", ImmutableList.Create<IExpression>(expArg)).Canonicalize();
        var inner = new Add(
            new Multiply(new Number(-2m), new Function("sin", ImmutableList.Create<IExpression>(trigArg)).Canonicalize()).Canonicalize(),
            new Multiply(new Number(-3m), new Function("cos", ImmutableList.Create<IExpression>(trigArg)).Canonicalize()).Canonicalize()).Canonicalize();
        var expected = new Divide(new Multiply(expTerm, inner).Canonicalize(), new Number(13m)).Canonicalize();
        Assert.IsTrue(result.ResultExpression!.InternalEquals(expected));
    }

    [TestMethod]
        [Timeout(10000)]
    public void Limit_UsesHopitalForExpSeries()
    {
        var numerator = new Add(
            new Function("exp", ImmutableList.Create<IExpression>(X)).Canonicalize(),
            new Add(new Number(-1m), new Multiply(new Number(-1m), X).Canonicalize()).Canonicalize()).Canonicalize();
        var expr = new Divide(numerator, new Power(X, new Number(2m)).Canonicalize()).Canonicalize();
        var limit = new Limit(expr, X, new Number(0m));

        var result = new LimitStrategy().Solve(limit, new SolveContext(targetVariable: X));

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsTrue(result.ResultExpression is Number n && Math.Abs((double)(n.Value - 0.5m)) < 1e-6);
    }

    [TestMethod]
        [Timeout(10000)]
    public void Limit_SeriesResummationForCosine()
    {
        var numerator = new Add(new Number(1m), new Multiply(new Number(-1m), new Function("cos", ImmutableList.Create<IExpression>(X)).Canonicalize()).Canonicalize()).Canonicalize();
        var expr = new Divide(numerator, new Power(X, new Number(2m)).Canonicalize()).Canonicalize();
        var limit = new Limit(expr, X, new Number(0m));

        var result = new LimitStrategy().Solve(limit, new SolveContext(targetVariable: X));

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsTrue(result.ResultExpression is Number num && Math.Abs((double)(num.Value - 0.5m)) < 1e-6);
    }

    [TestMethod]
        [Timeout(10000)]
    public void SeriesExpansion_RebuildsSinOverX()
    {
        var series = new SeriesExpansion(
            new Divide(new Function("sin", ImmutableList.Create<IExpression>(X)).Canonicalize(), X).Canonicalize(),
            X,
            new Number(0m),
            5);

        var result = new SeriesExpansionStrategy().Solve(series, new SolveContext(targetVariable: X));

        Assert.IsTrue(result.IsSuccess, result.Message);
        var eval = NumericEvaluator.TryEvaluate(result.ResultExpression!, new Dictionary<string, decimal> { { X.Name, 0m } }, out var value, out _);
        Assert.IsTrue(eval && Math.Abs((double)(value - 1m)) < 1e-6);
    }
}
