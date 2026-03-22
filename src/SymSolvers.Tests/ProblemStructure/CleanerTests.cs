using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WordsToSym;

namespace SymSolvers.Tests.ProblemStructure;

[TestClass]
public class CleanerTests
{
    [TestMethod]
        [Timeout(10000)]
    public void TestImplicitMultiplication_NoSpace()
    {
        var input = "y == 2x + 3y + 1i;";
        var options = new ProblemScriptCleanupOptions { NormalizeImplicitMultiplication = true };
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input, options);
        
        Assert.IsTrue(cleaned.Contains("2 * x"), $"Expected 2 * x, got {cleaned}");
        Assert.IsTrue(cleaned.Contains("3 * y"), $"Expected 3 * y, got {cleaned}");
        Assert.IsTrue(cleaned.Contains("1 * i"), $"Expected 1 * i, got {cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestImplicitMultiplication_WithSpace()
    {
        var input = "root1 == 1  i;";
        var options = new ProblemScriptCleanupOptions { NormalizeImplicitMultiplication = true };
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input, options);
        
        Assert.IsTrue(cleaned.Contains("1 * i"), $"Expected 1 * i, got {cleaned}");
    }

    [TestMethod]
        [Timeout(10000)]
    public void TestNotEqual()
    {
        var input = "a != 0;";
        var options = new ProblemScriptCleanupOptions { NormalizeNotEqualConstraints = true };
        var cleaned = ProblemScriptCleaner.NormalizeProblemScript(input, options);
        
        Assert.IsTrue(cleaned.Contains("ne(a, 0)"), $"Expected ne(a, 0), got {cleaned}");
    }
}
