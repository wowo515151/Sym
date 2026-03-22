// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class ParallelismTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Options_Normalize_DefaultParallelism_IsHalfCores()
        {
            var expected = Math.Max(1, Environment.ProcessorCount / 2);
            var normalized = CSharpMathBugAnalyzerOptions.Default.Normalize();
            Assert.AreEqual(expected, normalized.MaxDegreeOfParallelism);
        }

        [TestMethod]
        [Timeout(10000)]
        public void AnalyzeProject_ParallelAndSingleThreaded_ProduceSameFindings()
        {
            var files = new List<(string Content, string FilePath)>
            {
                (@"
using System;
public class A {
  public int Div0(int x) {
    return x / 0;
  }
}
", "A.cs"),
                (@"
public class B {
  public uint Under(uint len, uint off) {
    return len - off;
  }
}
", "B.cs")
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var baseOptions = CSharpMathBugAnalyzerOptions.Default with
            {
                ConfidenceThreshold = 0.0,
                MaxFindings = 200,
                AnalysisTimeoutSeconds = 30,
                SaturationTimeoutSeconds = 8
            };

            var single = analyzer.AnalyzeProject(files, baseOptions with { MaxDegreeOfParallelism = 1 });
            var parallel = analyzer.AnalyzeProject(files, baseOptions with { MaxDegreeOfParallelism = 4 });

            Assert.IsTrue(single.IsComplete, "Single-threaded analysis should complete.");
            Assert.IsTrue(parallel.IsComplete, "Parallel analysis should complete.");

            // Ensure we actually got meaningful findings from both files.
            Assert.IsTrue(single.Findings.Any(f => f.BugId == "CSMATH001"), "Expected divide-by-zero finding.");
            Assert.IsTrue(single.Findings.Any(f => f.BugId == "CSSEC021"), "Expected unsigned underflow finding.");

            // The parallelization change must not affect semantic results.
            var singleKeys = single.Findings.Select(ToKey).ToHashSet(StringComparer.Ordinal);
            var parallelKeys = parallel.Findings.Select(ToKey).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEquivalent(singleKeys.ToList(), parallelKeys.ToList(),
                "Parallel per-tree scanning should produce the same set of findings as single-threaded scanning.");

            Assert.AreEqual(single.CandidateCount, parallel.CandidateCount, "CandidateCount should be deterministic.");
            Assert.AreEqual(single.LoweredExpressionCount, parallel.LoweredExpressionCount, "LoweredExpressionCount should be deterministic.");
        }

        private static string ToKey(CSharpMathBugFinding finding)
        {
            var span = finding.SourceSpan;
            var location = span is null
                ? "<nosrc>"
                : $"{span.FilePath}:{span.StartLine}:{span.StartColumn}";
            return $"{finding.BugId}|{location}|{finding.Expression}";
        }
    }
}
