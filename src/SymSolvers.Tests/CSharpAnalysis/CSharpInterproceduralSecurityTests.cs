using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class CSharpInterproceduralSecurityTests
    {
        private CSharpMathBugAnalyzer _analyzer = new CSharpMathBugAnalyzer();

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_ArgumentPropagation_FindsBug()
        {
            var source = @"
using System.Diagnostics;

public class Helper {
    public void Execute(string cmd) {
        Process.Start(cmd); // Sink
    }
}

public class Test {
    public void Run(string input) {
        var h = new Helper();
        h.Execute(input); // Source 'input' flows to 'cmd'
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003" && f.Evidence.Any(e => e.Contains("Execute")));
            Assert.IsNotNull(finding, "Expected CSSEC003 in 'Run' (calling Execute) from interprocedural flow.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_ReturnPropagation_FindsBug()
        {
            var source = @"
using System.Diagnostics;

public class Helper {
    public string GetBadData(string input) {
        return input; // Echoes taint
    }
}

public class Test {
    public void Run(string input) {
        var h = new Helper();
        var data = h.GetBadData(input);
        Process.Start(data); // Sink
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected CSSEC003 from return value flow.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_DeepCallChain_FindsBug()
        {
            var source = @"
using System.Diagnostics;

public class C {
    public void DoIt(string s) => Process.Start(s);
}
public class B {
    public void Forward(string s) => new C().DoIt(s);
}
public class A {
    public void Start(string input) => new B().Forward(input);
}
";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003" && f.Evidence.Any(e => e.Contains("Forward")));
            Assert.IsNotNull(finding, "Expected CSSEC003 in 'A.Start' (calling Forward) from deep call chain.");
            Assert.IsTrue(finding.Evidence.Count >= 3, "Evidence should show full chain.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_Recursion_Terminates()
        {
            var source = @"
using System.Diagnostics;

public class Test {
    public void Recursive(string s) {
        Recursive(s); // Infinite loop
        Process.Start(s);
    }
}";
            // Should not hang/crash
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                MaxSaturationIterations: 10)); // Force bound

            // It might or might not find a bug depending on saturation, but it must return.
            Assert.IsTrue(result.IsComplete || result.Diagnostics.Any(d => d.Contains("iteration budget")), "Analysis should complete or report budget exhaustion.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_Sanitizer_BlocksFlow()
        {
             var source = @"
using System.IO;
using System.Diagnostics;

public class Security {
    public string Sanitize(string input) {
        return Path.GetFileName(input); // Known sanitizer
    }
}

public class Test {
    public void Run(string input) {
        var sec = new Security();
        var safe = sec.Sanitize(input);
        Process.Start(safe);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Sanitizer in helper method should block flow.");
        }
        
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_CrossFile_FindsBug()
        {
             // AnalyzeProject accepts multiple files
             var files = new[] 
             {
                 (@"
public class Helper {
    public static void Sink(string data) {
        System.Diagnostics.Process.Start(data);
    }
}
", "Helper.cs"),
                 (@"
public class Entry {
    public void Run(string input) {
        Helper.Sink(input);
    }
}
", "Entry.cs")
             };

            var result = _analyzer.AnalyzeProject(files, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected CSSEC003 from cross-file flow.");
             Assert.IsTrue(finding.Evidence.Any(e => e.Contains("Entry.cs") || e.Contains("Run")), "Evidence should trace back to Entry.cs.");
        }
    }
}
