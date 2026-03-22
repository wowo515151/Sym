// Copyright Warren Harding 2026
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class CSharpRuleCoverageUnitTests
    {
        private CSharpMathBugAnalyzer _analyzer = new CSharpMathBugAnalyzer();

        private void AssertHasFinding(CSharpMathBugAnalysisResult result, string bugId, string description)
        {
            if (!result.Findings.Any(f => f.BugId == bugId))
            {
                var diags = string.Join(Environment.NewLine, result.Diagnostics.Take(10));
                var findings = string.Join(", ", result.Findings.Select(f => f.BugId));
                Assert.Fail($"Should find {bugId} ({description}). Findings found: [{findings}]. Lowered: {result.LoweredExpressionCount}. Diagnostics: {diags}");
            }
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH002_ModuloByZero()
        {
            var source = @"class P { void M(int x) { int y = x % 0; } }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSMATH002", "Modulo by zero");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH007_IntegerDivision()
        {
            var source = @"class P { int M(int a, int b) { return a / b; } }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSMATH007", "Integer division truncation");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH009_XorAsPower()
        {
            var source = @"class P { int M(int a, int b) { return a ^ b; } }";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions { ConfidenceThreshold = 0 });
            AssertHasFinding(result, "CSMATH009", "XOR used as power");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH010_GenericIntDiv()
        {
            var source = @"
using System.Numerics;
public class C<T> where T : INumber<T> {
    public T M(T x) => T.One / x;
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions { ConfidenceThreshold = 0 });
            AssertHasFinding(result, "CSMATH010", "Generic integer division");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH011_GenericUnderflow()
        {
            var source = @"
using System.Numerics;
public class C<T> where T : INumber<T> {
    public T M(T x) => T.Zero - x;
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions { ConfidenceThreshold = 0 });
            AssertHasFinding(result, "CSMATH011", "Generic underflow");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH012_Recursion()
        {
            var source = @"class P { int M(int x) => x <= 0 ? 0 : M(x-1); }";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions { ConfidenceThreshold = 0 });
            AssertHasFinding(result, "CSMATH012", "Recursion");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC002_BinaryFormatter()
        {
            var source = @"using System.Runtime.Serialization.Formatters.Binary; class P { void M() { var b = new BinaryFormatter(); } }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC002", "BinaryFormatter");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC003_CommandInjection()
        {
            var source = @"using System.Diagnostics; class P { void M(string cmd) { Process.Start(cmd); } }";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.SinkOnly));
            AssertHasFinding(result, "CSSEC003", "Command injection");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC004_PathTraversal()
        {
            var source = @"using System.IO; class P { void M(string p) { File.ReadAllText(p); } }";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                ConfidenceThreshold: 0,
                SecurityFlowMode: CSharpSecurityFlowMode.SinkOnly));
            AssertHasFinding(result, "CSSEC004", "Path traversal");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC005_ConsoleOutput()
        {
            var source = @"class P { void M() { System.Console.WriteLine(""hi""); } }";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions { ConfidenceThreshold = 0 });
            AssertHasFinding(result, "CSSEC005", "Console output");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC006_WeakHash()
        {
            var source = @"using System.Security.Cryptography; class P { void M() { MD5.Create(); } }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC006", "Weak hash");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC007_UnsafeCode()
        {
            var source = @"class P { unsafe void M() { } }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC007", "Unsafe code");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC008_InsecureComparison()
        {
            var source = @"class P { bool M(string tokenA, string tokenB) => tokenA == tokenB; }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC008", "Insecure comparison");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC009_StaticMutableState()
        {
            var source = @"class P { static int x; void M() { x = 1; } }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC009", "Static mutable state");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH016_AccumulatorOverflow()
        {
            var source = @"
using System.Numerics;
public class C<T> where T : INumber<T> {
    public T M(T s, T a, T b) => s + a * b;
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSMATH016", "Accumulator overflow");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH019_FloatEquality()
        {
            var source = @"class P { bool M(double x) => x == 0.0; }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSMATH019", "Float equality");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC012_InfinityPropagation()
        {
            var source = @"
using System.Numerics;
public class C<T> where T : INumber<T> {
    public T M(T a) => a / T.Zero;
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC012", "Infinity propagation");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSSEC014_InsecureRandomSeed()
        {
            var source = @"
using System;
class P {
    void M() {
        long Ticks = DateTime.Now.Ticks;
        var rng = new Random((int)Ticks);
    }
}";
            // This should pass with default threshold; the rule is deterministic (time-based seed).
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC014", "Insecure random seed");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH024_SqrtNegative()
        {
            var source = @"class P { void M() { System.Math.Sqrt(-1.0); } }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSMATH024", "Sqrt negative");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH025_AdditionOverflow()
        {
            var source = @"
using System.Numerics;
public class C<T> where T : INumber<T> {
    public T M(T a, T b) => a + b;
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSMATH025", "Addition overflow");
        }

        [TestMethod] [Timeout(10000)]
        public void Coverage_CSMATH029_LogNegative()
        {
            var source = @"class P { void M() { System.Math.Log(0.0); } }";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSMATH029", "Log non-positive");
        }
    }
}
