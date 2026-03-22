using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core.Rewriters;
using SymRules;
using Sym.CSharpIO;

namespace SymSolvers.Tests.StableForms;

[TestClass]
public sealed class StabilityRuleLibraryTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Rules_Rewrite_Logsumexp_And_Log1p()
    {
        var expr = CSharpIO.ParseExpressions("log(exp(a) + exp(b)) + log(1 + x)")[0];

        var rewritten = Rewriter.RewriteFully(expr, StabilityRuleLibrary.Rules, maxInternalIterations: 4);

        var text = rewritten.RewrittenExpression.ToDisplayString();
        StringAssert.Contains(text, "logsumexp");
        StringAssert.Contains(text, "log1p");
    }

    [TestMethod]
        [Timeout(10000)]
    public void Rules_Rewrite_LogExpDifference()
    {
        var expr = CSharpIO.ParseExpressions("log(exp(a) - exp(b))")[0].Canonicalize();

        var pattern = StabilityRuleLibrary.Rules[^1].Pattern.ToDisplayString();
        var exprText = expr.ToDisplayString();
        var match = Rewriter.TryMatch(expr, StabilityRuleLibrary.Rules[^1].Pattern);
        Assert.IsTrue(match.Success, $"Log(exp(a) - exp(b)) should match stability rule pattern. Pattern={pattern}; Expr={exprText}");

        var rewritten = Rewriter.RewriteFully(expr, StabilityRuleLibrary.Rules, maxInternalIterations: 6);

        var text = rewritten.RewrittenExpression.ToDisplayString().ToLowerInvariant();
        StringAssert.Contains(text, "expm1");
        StringAssert.Contains(text, "log");
    }
}
