// Copyright Warren Harding 2026
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class CSharpMathBugAnalyzerTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_DivByZero_FindsBug()
        {
            var source = @"
using System;
class Program {
    void Main() {
        int x = 10;
        int y = x / 0;
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);
            
            if (result.Findings.Count == 0)
            {
                Console.WriteLine("Diagnostics: " + string.Join(Environment.NewLine, result.Diagnostics));
                Console.WriteLine("Lowered Expressions: " + result.LoweredExpressionCount);
            }

            Assert.IsNotNull(result.Findings);
            Assert.IsTrue(result.Findings.Count > 0, "No bugs found. See console output for diagnostics.");
            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH001");
            Assert.IsNotNull(bug, "Should find CSMATH001");
            StringAssert.Contains(bug.Expression, "/ 0");
            Assert.AreEqual(CSharpMathBugConfidence.Confirmed, bug.Confidence);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Log1p_FindsBug()
        {
            var source = @"
using System;
class Program {
    void Main() {
        double x = 0.0001;
        double y = Math.Log(1 + x);
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);
            
            if (result.Findings.Count == 0)
            {
                Console.WriteLine("Diagnostics: " + string.Join(Environment.NewLine, result.Diagnostics));
                Console.WriteLine("Lowered Expressions: " + result.LoweredExpressionCount);
            }

            Assert.IsNotNull(result.Findings);
            Assert.IsTrue(result.Findings.Count > 0);
            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH006");
            Assert.IsNotNull(bug, "Should find CSMATH006");
            StringAssert.Contains(bug.Expression, "Math.Log");
            Assert.AreEqual(CSharpMathBugConfidence.Medium, bug.Confidence);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_IntDivTruncation_FindsBug()
        {
            var source = @"
using System;
class Program {
    void Main() {
        int a = 3;
        int b = 2;
        float f = (float)(a / b);
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);
            
            if (result.Findings.Count == 0)
            {
                Console.WriteLine("Diagnostics: " + string.Join(Environment.NewLine, result.Diagnostics));
                Console.WriteLine("Lowered Expressions: " + result.LoweredExpressionCount);
            }
            
            Assert.IsNotNull(result.Findings);
            Assert.IsTrue(result.Findings.Count > 0);
            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH005");
            Assert.IsNotNull(bug, "Should find CSMATH005");
            StringAssert.Contains(bug.Expression, "(float)(a / b)");
            
            Assert.AreEqual(CSharpMathBugConfidence.High, bug.Confidence);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_AsinOutOfRange_FindsBug()
        {
            var source = @"
using System;
class Program {
    void Main() {
        double y = Math.Asin(1.5);
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);
            
            Assert.IsNotNull(result.Findings);
            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH030");
            Assert.IsNotNull(bug, "Should find CSMATH030");
            StringAssert.Contains(bug.Expression, "Math.Asin(1.5)");
            Assert.AreEqual(CSharpMathBugConfidence.Confirmed, bug.Confidence);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_CancelledToken_ReturnsIncompleteResult()
        {
            var source = @"
using System;
class Program {
    void Main() {
        int x = 10;
        int y = x / 0;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default;
            var cancelledToken = new System.Threading.CancellationToken(canceled: true);
            var result = analyzer.AnalyzeText(source, options, cancelledToken);

            Assert.IsFalse(result.IsComplete, "Cancelled analysis should return IsComplete=false.");
            Assert.IsTrue(result.Diagnostics.Count > 0, "Cancelled analysis should include diagnostics.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void AnalyzeProject_DiagnosticFlood_IsCappedPerCompilerCode()
        {
            var files = Enumerable.Range(0, 40)
                .Select(i => ($"class C{i} {{ MissingType{i} Field; }}", $"f{i}.cs"));

            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeProject(files);

            int missingTypeDiagnostics = result.Diagnostics.Count(d => d.Contains("CS0246", StringComparison.Ordinal));
            Assert.IsTrue(missingTypeDiagnostics > 0, "Expected unresolved type diagnostics for this input.");
            Assert.IsTrue(missingTypeDiagnostics <= 6, $"Expected CS0246 diagnostics to be capped. Actual: {missingTypeDiagnostics}.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Options_Normalize_ClampsSecurityFlowSettings()
        {
            var options = new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: (CSharpSecurityFlowMode)999,
                SecurityMaxTraceSteps: 1);

            var normalized = options.Normalize();

            Assert.AreEqual(CSharpSecurityFlowMode.IntraProcedural, normalized.SecurityFlowMode);
            Assert.AreEqual(2, normalized.SecurityMaxTraceSteps);
        }
    }
}
