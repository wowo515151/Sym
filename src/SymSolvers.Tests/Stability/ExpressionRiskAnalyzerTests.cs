// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.CSharpIO;
using SymSolvers.Stability;

namespace SymSolvers.Tests.Stability;

[TestClass]
public sealed class ExpressionRiskAnalyzerTests
{
    [TestMethod]
        [Timeout(10000)]
    public void StablePatterns_HaveHigherRewardThanRiskyOnes()
    {
        var risky = CSharpIO.ParseExpressions("log(exp(x))")[0];
        var stable = CSharpIO.ParseExpressions("logsumexp(x, 0) + log1p(y) + softplus(z)")[0];

        var riskyScore = ExpressionRiskAnalyzer.Score(risky);
        var stableScore = ExpressionRiskAnalyzer.Score(stable);

        Assert.IsTrue(stableScore.Net > riskyScore.Net, $"Expected stable patterns to have higher net score. Stable={stableScore.Net}, Risky={riskyScore.Net}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void LogDiffExp_PenalizedComparedToStableRewrite()
    {
        var unstable = CSharpIO.ParseExpressions("log(exp(a) - exp(b))")[0];
        var stable = CSharpIO.ParseExpressions("b + log(expm1(a - b))")[0];

        var unstableScore = ExpressionRiskAnalyzer.Score(unstable);
        var stableScore = ExpressionRiskAnalyzer.Score(stable);

        Assert.IsTrue(stableScore.Net > unstableScore.Net, $"Stable rewrite should score better. Stable={stableScore.Net}, Unstable={unstableScore.Net}");
    }
}
