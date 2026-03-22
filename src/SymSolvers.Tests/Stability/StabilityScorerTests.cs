// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Sym.CSharpIO;
using SymSolvers.Numerics;
using SymSolvers.Stability;

namespace SymSolvers.Tests.Stability;

[TestClass]
public sealed class StabilityScorerTests
{
    [TestMethod]
        [Timeout(10000)]
    public void StabilityScorer_Prefers_Stable_Form()
    {
        var models = new IFloatingPointModel[] { new Float16Model(), new BFloat16Model(), new Float64Model() };
        var scorer = new StabilityScorer(models, sampleCount: 32);

        var naive = CSharpIO.ParseExpressions("(exp(x) - 1) / x")[0];
        var stable = CSharpIO.ParseExpressions("expm1(x) / x")[0];

        var naiveScore = scorer.Score(naive);
        var stableScore = scorer.Score(stable);

        Assert.AreEqual(models.Length, naiveScore.Metrics.Count, "Metrics should cover all models.");
        Assert.AreEqual(models.Length, stableScore.Metrics.Count, "Metrics should cover all models.");
        Assert.IsTrue(stableScore.Score <= naiveScore.Score + 1e-6, $"Stable form should rank at least as good. Stable={stableScore.Score} Naive={naiveScore.Score}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void StabilityScorer_Penalties_Reflect_NaNs()
    {
        var models = new IFloatingPointModel[] { new Float16Model(), new Float64Model() };
        var scorer = new StabilityScorer(models, sampleCount: 8);

        var bad = CSharpIO.ParseExpressions("exp(12 * x)")[0];
        var good = CSharpIO.ParseExpressions("exp(0.5 * x)")[0];

        var badScore = scorer.Score(bad);
        var goodScore = scorer.Score(good);

        Assert.IsTrue(badScore.Score > goodScore.Score, "Expressions producing NaN/Inf should be penalized.");
        Assert.IsTrue(badScore.Metrics.Any(m => m.NaNCount + m.InfCount > 0), "NaN/Inf counts should be reported when the reference is finite.");
    }

    [TestMethod]
    [Timeout(2000)]

    public void StabilityScorer_Detects_Underflow_To_Zero()
    {
        // Deterministic underflow check: Half.MinSubnormal is ~5.96e-8, so this should round to 0 in FP16.
        // NOTE: In this CAS, `1e-12` is parsed as 1 * 10^-12 (not a floating literal).
        // Use an explicit decimal literal to represent the intended tiny value.
        var expr = CSharpIO.ParseExpressions("x * 0.000000000001")[0];
        var assignments = new Dictionary<string, double> { { "x", 1.0 } };

        var ok16 = PrecisionExpressionEvaluator.TryEvaluate(expr, assignments, new Float16Model(), out var v16, out var err16);
        var ok64 = PrecisionExpressionEvaluator.TryEvaluate(expr, assignments, new Float64Model(), out var v64, out var err64);

        Assert.IsTrue(ok16, err16);
        Assert.IsTrue(ok64, err64);
        Assert.AreNotEqual(0.0, v64, "FP64 should keep non-zero tiny values.");
        Assert.AreEqual(0.0, v16, "FP16 should underflow the tiny value to 0.");
    }
}
