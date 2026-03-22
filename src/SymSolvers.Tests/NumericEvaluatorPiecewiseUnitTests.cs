// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers.Tests;

[TestClass]
public sealed class NumericEvaluatorPiecewiseUnitTests
{
    [TestMethod]
    [Timeout(2000)]
    public void NumericEvaluator_EvaluatesPiecewise_ValueConditionPairs()
    {
        var assignments = ImmutableDictionary<string, decimal>.Empty;
        var expr = new Piecewise(
            new Number(-2.4m),
            new Equality(new Number(-2.4m), new Number(-3m)),
            new Number(-2m),
            new Function("gt", new Number(-2.4m), new Number(-3m))
        ).Canonicalize();

        Assert.IsTrue(NumericEvaluator.TryEvaluate(expr, assignments, out var result, out var error), error);
        Assert.AreEqual(-2m, result);
    }

    [TestMethod]
    [Timeout(2000)]
    public void NumericEvaluator_EvaluatesPiecewise_ModCondition()
    {
        var assignments = ImmutableDictionary<string, decimal>.Empty.Add("x", 4m);
        var expr = new Piecewise(
            new Divide(new Symbol("x"), new Number(2m)),
            new Equality(new Function("Mod", new Symbol("x"), new Number(2m)), new Number(0m)),
            new Add(new Number(1m), new Multiply(new Number(3m), new Symbol("x"))),
            new Equality(new Function("Mod", new Symbol("x"), new Number(2m)), new Number(1m))
        ).Canonicalize();

        Assert.IsTrue(NumericEvaluator.TryEvaluate(expr, assignments, out var result, out var error), error);
        Assert.AreEqual(2m, result);
    }
}
