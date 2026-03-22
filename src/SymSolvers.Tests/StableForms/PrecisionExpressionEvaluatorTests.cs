using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Sym.CSharpIO;
using SymSolvers.Numerics;
using SymSolvers.Stability;

namespace SymSolvers.Tests.StableForms;

[TestClass]
public sealed class PrecisionExpressionEvaluatorTests
{
    [TestMethod]
        [Timeout(10000)]
    public void LogSumExp_DoesNotOverflow_Float16()
    {
        var expr = CSharpIO.ParseExpressions("logsumexp(1000, 1001)")[0];
        var fp16 = new Float16Model();
        var fp64 = new Float64Model();

        var ok16 = PrecisionExpressionEvaluator.TryEvaluate(expr, new Dictionary<string, double>(), fp16, out var v16, out var err16);
        var ok64 = PrecisionExpressionEvaluator.TryEvaluate(expr, new Dictionary<string, double>(), fp64, out var v64, out var err64);

        Assert.IsTrue(ok16, err16);
        Assert.IsTrue(ok64, err64);
        Assert.IsFalse(double.IsInfinity(v16), "Float16 logsumexp should stay finite.");
        Assert.IsTrue(Math.Abs(v16 - v64) < 1.0, "Stable logsumexp should be close to FP64 reference.");
    }

    [TestMethod]
        [Timeout(10000)]

    public void BFloat16_Rounding_IsDeterministic()
    {
        var bf16 = new BFloat16Model();
        var value = 1.234567;
        var rounded1 = bf16.Round(value);
        var rounded2 = bf16.Round(rounded1);
        Assert.AreEqual(rounded1, rounded2, 1e-9, "BF16 rounding should be idempotent.");

        var expr = CSharpIO.ParseExpressions("log1p(x) + expm1(x)")[0];
        var assignments = new Dictionary<string, double> { { "x", 0.125 } };
        var ok = PrecisionExpressionEvaluator.TryEvaluate(expr, assignments, bf16, out var v, out var err);
        Assert.IsTrue(ok, err);
        Assert.IsFalse(double.IsNaN(v));
    }

    [TestMethod]
        [Timeout(10000)]
    public void Softplus_Remains_Finite_For_Large_Negative()
    {
        var expr = CSharpIO.ParseExpressions("softplus(-10)")[0];
        var fp16 = new Float16Model();
        var ok = PrecisionExpressionEvaluator.TryEvaluate(expr, new Dictionary<string, double>(), fp16, out var v, out var err);

        Assert.IsTrue(ok, err);
        Assert.IsFalse(double.IsInfinity(v), "Softplus should stabilize extreme negative inputs.");
        Assert.IsTrue(v > 0, "Softplus should be positive even for large negative x.");
    }
}
