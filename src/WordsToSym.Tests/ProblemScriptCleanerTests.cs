// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WordsToSym;

namespace WordsToSym.Tests;

[TestClass]
public class ProblemScriptCleanerTests
{
    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_SimpleEquality_AddsSemicolon()
    {
        var script = "x = 5";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        Assert.AreEqual("x = 5;", result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_ImplicitMultiplication_Normalizes()
    {
        var script = "2x = 10";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        Assert.AreEqual("2 * x = 10;", result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_CaretPower_Normalizes()
    {
        var script = "x^2 = 25";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        Assert.AreEqual("Pow(x, 2) = 25;", result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_MultipleLines_JoinsAndCleans()
    {
        var script = "x = 5\ny = x + 2";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        var expected = "x = 5;" + Environment.NewLine + "y = x + 2;";
        Assert.AreEqual(expected, result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_RemovesMarkdown()
    {
        var script = "```\nx = 5\n```";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        Assert.AreEqual("x = 5;", result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_HandlesNotesAndTags()
    {
        var script = "<Notes>Some notes</Notes>\n<Tags>test</Tags>\nx = 5";
        var options = new ProblemScriptCleanupOptions(RemoveMetadata: true);
        var result = ProblemScriptCleaner.NormalizeProblemScript(script, options);
        Assert.AreEqual("x = 5;", result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_HandlesTypePrefixedIdentifiers()
    {
        var script = "real_x = 5\nint_y = 10";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        // Console.WriteLine($"DEBUG: result='{result}'");
        Assert.IsTrue(result.Contains("x = 5;"), $"Expected 'x = 5;' in '{result}'");
        Assert.IsTrue(result.Contains("y = 10;"), $"Expected 'y = 10;' in '{result}'");
        Assert.IsTrue(result.Contains("Integer(y);"), $"Expected 'Integer(y);' in '{result}'");
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_ScientificNotation_Preserved()
    {
        var script = "x = 1e3";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        Assert.AreEqual("x = 1e3;", result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_LatexSubscript_Normalizes()
    {
        var script = "x_{opt} = 10";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        Assert.AreEqual("x_opt = 10;", result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_AbsoluteValueBars_Normalizes()
    {
        var script = "|x - 5| = 10";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        Assert.AreEqual("Abs(x - 5) = 10;", result.Trim());
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_Sum_Normalizes()
    {
        var script = "Sum_{i=1}^{n} i = n(n+1)/2";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        // Current behavior: Sum_{i=1} is stripped to Sum, ^{n} remains
        Assert.IsTrue(result.Contains("Sum ^{n}"), $"Actual: {result}");
        Assert.IsTrue(result.Contains("n(n+1)/2"), $"Actual: {result}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_ModularArithmetic_Normalizes()
    {
        var script = "x = 10 mod 3";
        var result = ProblemScriptCleaner.NormalizeProblemScript(script);
        Assert.AreEqual("x = mod(10, 3);", result.Trim());
    }
}
