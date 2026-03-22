using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;
using System.Collections.Generic;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class FalsePositivesTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void CSSEC001_WeakRNG_IgnoredInTests()
        {
            var source = @"
using System;
namespace Tests {
    public class TestGenerators {
        public void GenerateRandomData() {
            var rnd = new Random();
        }
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeProject(new[] { (source, "TestGenerators.cs") });
            
            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC001");
            Assert.IsNull(bug, "Should ignore Weak RNG in TestGenerators.cs");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSMATH017_AbsOverflow_DifferentiatesTypes()
        {
            var source = @"
using System;
class Program {
    void Main() {
        double d = -10.5;
        double resD = Math.Abs(d); // Should NOT flag

        int i = -10;
        int resI = Math.Abs(i); // Should flag
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);
            
            var bugI = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH017" && f.Expression.Contains("(i)"));
            var bugD = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH017" && f.Expression.Contains("(d)"));
            
            Assert.IsNotNull(bugI, "Should flag Abs(int)");
            Assert.IsNull(bugD, "Should NOT flag Abs(double)");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSMATH022_IncOverflow_NotReported()
        {
            var source = @"
using System;
class Program {
    void Main() {
        for (int i = 0; i < 10; i++) { } // Should NOT flag i++
        
        int x = 100;
        x++; // Should NOT flag
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);
            
            var any = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH022");
            Assert.IsNull(any, "CSMATH022 should not be reported.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSMATH023_VariableDiv_HasLowConfidence()
        {
            // Note: we want a division where the divisor is a generic type parameter.
            // The simplest compilable way in this repo is to constrain T to INumber<T>.
            var sourceFinal = @"
using System;
public class Calc<T> where T : System.Numerics.INumber<T> {
    public T Div(T a, T b) {
        return a / b;
    }
}
";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };
            var result = analyzer.AnalyzeText(sourceFinal, options);
            
            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH023");
            Assert.IsNotNull(bug, "Should find CSMATH023");
            Assert.AreEqual(CSharpMathBugConfidence.Low, bug.Confidence, "Variable division should have Low confidence");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSMATH023_DivisionWithZeroGuard_IsSuppressed()
        {
            var source = @"
using System;
public class Calc<T> where T : System.Numerics.INumber<T> {
    public bool TryInv(T value, out T inv) {
        inv = T.Zero;
        if (T.Zero.Equals(value)) return false;
        inv = T.One / value;
        return true;
    }
}
";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };
            var result = analyzer.AnalyzeText(source, options);

            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH023");
            Assert.IsNull(bug, "Guarded division should not be flagged as potential division-by-zero.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSMATH007_LengthHalving_IsSuppressedByDefaultThreshold()
        {
            var source = @"
public class P {
    public int Midpoint(string[] events) {
        return events.Length / 2;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);

            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH007");
            Assert.IsNull(bug, "Length/count halving should not be reported as truncation by default.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSMATH009_HashMixXor_IsSuppressedByDefaultThreshold()
        {
            var source = @"
using System;
public class P {
    public int Seed(int attempt) {
        return unchecked(Environment.TickCount * 397) ^ attempt;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);

            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH009");
            Assert.IsNull(bug, "Hash-mix XOR should not be reported by default.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSMATH012_DivideAndConquerRecursion_IsSuppressedByDefaultThreshold()
        {
            var source = @"
using System.Linq;
public class P {
    public int[] Probe(int[] values) {
        if (values.Length <= 1) return values;
        int mid = values.Length / 2;
        var left = Probe(values.Take(mid).ToArray());
        var right = Probe(values.Skip(mid).ToArray());
        return left.Concat(right).ToArray();
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source);

            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSMATH012");
            Assert.IsNull(bug, "Intentional divide-and-conquer recursion should not be reported by default.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSSEC003_CommandInjection_NoSource_SuppressedInFlowMode()
        {
            var source = @"
using System.Diagnostics;
public class Test {
    public void Run() {
        Process.Start(""calc.exe"");
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural };
            var result = analyzer.AnalyzeText(source, options);

            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(bug, "Command injection with hardcoded string should be suppressed in flow mode.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void CSSEC004_PathTraversal_NoSource_SuppressedInFlowMode()
        {
            var source = @"
using System.IO;
public class Test {
    public void Run() {
        File.ReadAllText(""C:\\config.txt"");
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural };
            var result = analyzer.AnalyzeText(source, options);

            var bug = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC004");
            Assert.IsNull(bug, "Path traversal with hardcoded string should be suppressed in flow mode.");
        }
    }
}
