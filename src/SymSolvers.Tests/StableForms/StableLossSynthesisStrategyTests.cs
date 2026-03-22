using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core.Rewriters;
using Sym.CSharpIO;
using SymRules;
using SymSolvers.Numerics;
using SymSolvers.StableForms;
using SymSolvers.Stability;
using SymSolvers.Validation;

namespace SymSolvers.Tests.StableForms;

[TestClass]
public sealed class StableLossSynthesisStrategyTests
{
    [TestMethod]
        [Timeout(10000)]
    public void StableLoss_Rewrites_Log1p()
    {
        var expr = CSharpIO.ParseExpressions("log(1 + x)")[0];
        var strategy = new StableLossSynthesisStrategy();
        var context = new Sym.Core.SolveContext(additionalData: ImmutableDictionary<string, object>.Empty);

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        StringAssert.Contains(result.ResultExpression?.ToDisplayString() ?? string.Empty, "log1p");
    }

    [TestMethod]
        [Timeout(10000)]
    public void StableLoss_Rewrites_LogExpDifference()
    {
        var expr = CSharpIO.ParseExpressions("log(exp(a) - exp(b))")[0].Canonicalize();
        var strategy = new StableLossSynthesisStrategy();
        var context = new Sym.Core.SolveContext(additionalData: ImmutableDictionary<string, object>.Empty);

        var rewritten = Rewriter.RewriteFully(expr, StabilityRuleLibrary.Rules, maxInternalIterations: 6).RewrittenExpression.Canonicalize();
        var rewriteDisplay = rewritten.ToDisplayString();
        StringAssert.Contains(rewriteDisplay.ToLowerInvariant(), "expm1");

        var symbols = new List<Symbol> { new("a"), new("b") };
        var ce = new CounterexampleFinder(new Float64Model(), tolerance: 1e-6).Find(expr, rewritten, symbols, maxSamples: 32);
        Assert.IsNull(ce, $"Rewritten form should be equivalent; counterexample: {ce?.Message}");

        var scorer = new StabilityScorer(new IFloatingPointModel[] { new Float16Model(), new Float32Model(), new Float64Model() }, sampleCount: 32);
        var originalScore = scorer.Score(expr).Score;
        var stableScore = scorer.Score(rewritten).Score;
        Assert.IsTrue(stableScore <= originalScore, $"Stable rewrite should not score worse. Stable={stableScore}, Original={originalScore}");

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);
        var stable = result.ResultExpression!;
        var display = stable.ToDisplayString();
        Assert.IsTrue(stable.Canonicalize().InternalEquals(rewritten), $"Stable rewrite should match stability rules. Expected: {rewriteDisplay}, Got: {display}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void StableLoss_Rewrites_LogExpDifference_WithStableOptions()
    {
        var expr = CSharpIO.ParseExpressions("log(exp(a) - exp(b))")[0].Canonicalize();
        var strategy = new StableLossSynthesisStrategy();
        var additional = ImmutableDictionary<string, object>.Empty
            .Add("StableLossEnabled", true)
            .Add("StableLossCandidateBudget", 20)
            .Add("StableLossSampleBudget", 48)
            .Add("StableLossReturnTopK", 2);
        var context = new Sym.Core.SolveContext(additionalData: additional);

        var result = strategy.Solve(expr, context);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var expected = CSharpIO.ParseExpressions("b + log(expm1(a - b))")[0].Canonicalize();
        Assert.IsNotNull(result.ResultExpression);
        Assert.IsTrue(result.ResultExpression!.Canonicalize().InternalEquals(expected), $"Stable rewrite with options should match expected form. Got: {result.ResultExpression}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void CounterexampleFinder_FindsMismatch()
    {
        var finder = new CounterexampleFinder(new Float64Model(), tolerance: 1e-6);
        var symbols = new List<Symbol> { new("x") };
        var left = CSharpIO.ParseExpressions("x^2")[0];
        var right = CSharpIO.ParseExpressions("x")[0];

        var ce = finder.Find(left, right, symbols, maxSamples: 32);

        Assert.IsNotNull(ce, "Expected counterexample for x^2 vs x.");
    }

    [TestMethod]
        [Timeout(10000)]
    public void StabilityScorer_Prefers_Expm1_Form()
    {
        var models = new IFloatingPointModel[] { new Float16Model(), new Float64Model() };
        var scorer = new StabilityScorer(models, sampleCount: 12);

        var naive = CSharpIO.ParseExpressions("(exp(x) - 1) / x")[0];
        var stable = CSharpIO.ParseExpressions("expm1(x) / x")[0];

        var naiveScore = scorer.Score(naive).Score;
        var stableScore = scorer.Score(stable).Score;

        Assert.IsTrue(stableScore <= naiveScore + 1e-6, $"Stable form should not score worse. Stable={stableScore} Naive={naiveScore}");
    }
}
