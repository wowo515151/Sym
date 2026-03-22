using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class MathGuardTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_GuardedDivision_IsSuppressedByProver()
        {
            var source = @"
using System.Numerics;
class Test {
    public T Divide<T>(T x, T y) where T : INumber<T> {
        if (y != T.Zero) {
            return x / y;
        }
        return T.Zero;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH023");
            Assert.IsNull(finding, "Division by zero should be suppressed by the guard prover.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_UnguardedDivision_IsNotSuppressed()
        {
            var source = @"
using System.Numerics;
class Test {
    public T Divide<T>(T x, T y) where T : INumber<T> {
        return x / y;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH023");
            if (finding == null)
            {
                var allIds = string.Join(", ", result.Findings.Select(f => f.BugId));
                Assert.IsNotNull(finding, $"Unguarded division by zero should be reported. Found: {allIds}");
            }
        }
    }
}
