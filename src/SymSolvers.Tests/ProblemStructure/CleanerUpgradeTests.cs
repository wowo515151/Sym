// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WordsToSym;

namespace SymSolvers.Tests.ProblemStructure;

[TestClass]
public class CleanerUpgradeTests
{
    [TestMethod]
        [Timeout(10000)]
    public void TestCoordinateTuplesToVectors()
    {
        var input = "P == (-3, 4);";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("P == Vector(-3, 4)"), $"Expected P == Vector(-3, 4), got {cleaned}");

        input = "Q == (1, 2, 3);";
        cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("Q == Vector(1, 2, 3)"), $"Expected Q == Vector(1, 2, 3), got {cleaned}");
        
        input = "(x, y) == (1, 2);";
        cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("Vector(x, y) == Vector(1, 2)"), $"Expected Vector(x, y) == Vector(1, 2), got {cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestDotCoordinateAccess()
    {
        var input = "result == Q.x + Q.y + Q.z;";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("Q[1]"), $"Expected Q[1], got {cleaned}");
        Assert.IsTrue(cleaned.Contains("Q[2]"), $"Expected Q[2], got {cleaned}");
        Assert.IsTrue(cleaned.Contains("Q[3]"), $"Expected Q[3], got {cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestPiecewiseRepair()
    {
        var input = "f == Piecewise(; x < 0; 1; x >= 0; 2);";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("Piecewise(x < 0, 1, x >= 0, 2)"), $"Expected Piecewise(x < 0, 1, x >= 0, 2), got {cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestPerfectSquarePhrasing()
    {
        var input = "x == a perfect square;";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("issquare(x)"), $"Expected issquare(x), got {cleaned}");

        input = "y == perfect square;";
        cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("issquare(y)"), $"Expected issquare(y), got {cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestPowWrapperNormalization()
    {
        var input = "a == absPow(x + 1, 2);";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("Pow(Abs(x + 1), 2)"), $"Expected absPow rewrite, got {cleaned}");

        input = "b == floorPow(n / 2, 2);";
        cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("Pow(floor(n / 2), 2)"), $"Expected floorPow rewrite, got {cleaned}");

        input = "c == ceilPow(n / 2, 3);";
        cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("Pow(ceil(n / 2), 3)"), $"Expected ceilPow rewrite, got {cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestMismatchedBrackets()
    {
        var input = "range[1 );";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("range[1]"), $"Expected range[1], got {cleaned}");

        input = "rangeinterval(1 ];";
        cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsTrue(cleaned.Contains("range[1]"), $"Expected range[1], got {cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestUnmatchedBracketsDropping()
    {
        var input = "invalid == (1 + 2;";
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input);
        Assert.IsFalse(cleaned.Contains("invalid"), "Expected line to be dropped due to unmatched brackets");
    }
}
