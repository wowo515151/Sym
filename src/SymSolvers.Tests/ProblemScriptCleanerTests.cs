using Microsoft.VisualStudio.TestTools.UnitTesting;
using WordsToSym;

namespace SymSolvers.Tests;

[TestClass]
public sealed class ProblemScriptCleanerTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Normalizes_Leading_Equality_Typos()
    {
        var script = @"
total_spent == 120;
= total_spent > 100;
== bonus == 250;
";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(script);
        
        Assert.IsTrue(cleaned.Contains("total_spent > 100;"), $"Expected 'total_spent > 100;', got:\n{cleaned}");
        Assert.IsTrue(cleaned.Contains("bonus == 250;"), $"Expected 'bonus == 250;', got:\n{cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeLine_PositiveRealNumber_Cleaned()
    {
        // Original failing case with extra space
        var input = "x == positive real  number";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        // Expect "x > 0;" (NormalizeProblemScript adds semicolon)
        Assert.AreEqual("x > 0;", cleaned.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeLine_PositiveRealNumber_Standard_Cleaned()
    {
        var input = "x == positive real number";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.AreEqual("x > 0;", cleaned.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeLine_PositiveRealNumber_WithSemicolon()
    {
        var input = "x == positive real number;";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.AreEqual("x > 0;", cleaned.Trim());
    }
}
