// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.CSharpIO;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests;

[TestClass]
public class ExpressionPropertyValidatorTests
{
    private static Symbol X => new("x");

    [TestMethod]
        [Timeout(10000)]
    public void ValidatesTrueEquality()
    {
        var expr = CSharpIO.ParseExpressions("x + x = 2*x")[0];
        var result = ExpressionPropertyValidator.ValidateEquality(expr, X);

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
        [Timeout(10000)]
    public void DetectsCounterexample()
    {
        var expr = CSharpIO.ParseExpressions("x^2 = x")[0];
        var result = ExpressionPropertyValidator.ValidateEquality(expr, X);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "Counterexample");
    }

    [TestMethod]
        [Timeout(10000)]
    public void DetectsFractionalCounterexampleWithExpandedSamples()
    {
        var pi = new Number((decimal)System.Math.PI);
        var expr = new Equality(
            new Function("sin", ImmutableList.Create<IExpression>(new Multiply(pi, X).Canonicalize())).Canonicalize(),
            new Number(0m)).Canonicalize();

        var result = ExpressionPropertyValidator.ValidateEquality(expr, X);

        Assert.IsFalse(result.Success, "Expanded sampling should catch non-integer counterexample.");
    }
}
