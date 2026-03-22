// Copyright Warren Harding 2026
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WordsToSym;

namespace SymSolvers.Tests.ProblemStructure;

[TestClass]
public sealed class ProblemScriptCleanerTests
{
    [TestMethod]
        [Timeout(10000)]
    public void NormalizeProblemScript_UnwrapsLabeledComparisons()
    {
        var script = string.Join(System.Environment.NewLine, new[]
        {
            "Eq1 = (w + x + y == -2);",
            "Eq2 = w + x + z == 4;",
            "Target = w * x + y * z;"
        });

        var normalized = ProblemScriptCleaner.NormalizeProblemScript(script);
        var lines = normalized.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToList();

        CollectionAssert.Contains(lines, "w + x + y == -2;");
        CollectionAssert.Contains(lines, "w + x + z == 4;");
        CollectionAssert.Contains(lines, "Target = w * x + y * z;");
        Assert.IsFalse(lines.Any(l => l.StartsWith("Eq1", System.StringComparison.Ordinal)), "Labeled equality should be unwrapped.");
    }
}
