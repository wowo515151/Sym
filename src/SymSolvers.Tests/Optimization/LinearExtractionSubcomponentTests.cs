// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers.Tests.Optimization;

[TestClass]
public sealed class LinearExtractionSubcomponentTests
{
    [TestMethod]
    [Timeout(5000)]
    public void TryExtractLinear_Extracts_Coefficients_And_Constant_From_Objective()
    {
        var m = new Symbol("m");
        var n = new Symbol("n");

        var expr = new Add(
            new Multiply(new Number(2002m), m),
            new Multiply(new Number(44444m), n)
        ).Canonicalize();

        var ok = LinearExtraction.TryExtractLinear(expr, new IExpression[] { m, n }, out var coeffs, out var constant);

        Assert.IsTrue(ok, expr.ToDisplayString());
        Assert.AreEqual(2, coeffs.Length);
        Assert.AreEqual(2002m, coeffs[0]);
        Assert.AreEqual(44444m, coeffs[1]);
        Assert.AreEqual(0m, constant);
    }

    [TestMethod]
    [Timeout(5000)]
    public void TryExtractLinear_Handles_Constant_Offset()
    {
        var m = new Symbol("m");
        var n = new Symbol("n");

        var expr = new Add(
            new Multiply(new Number(2002m), m),
            new Multiply(new Number(44444m), n),
            new Number(7m)
        ).Canonicalize();

        var ok = LinearExtraction.TryExtractLinear(expr, new IExpression[] { m, n }, out var coeffs, out var constant);

        Assert.IsTrue(ok, expr.ToDisplayString());
        Assert.AreEqual(2, coeffs.Length);
        Assert.AreEqual(2002m, coeffs[0]);
        Assert.AreEqual(44444m, coeffs[1]);
        Assert.AreEqual(7m, constant);
    }
}
