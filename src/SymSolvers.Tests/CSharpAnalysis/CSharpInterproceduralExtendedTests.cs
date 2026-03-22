using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class CSharpInterproceduralExtendedTests
    {
        private CSharpMathBugAnalyzer _analyzer = new CSharpMathBugAnalyzer();

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_Generics_FindsBug()
        {
            var source = @"
using System.Diagnostics;

public class Wrapper<T> {
    public T Value { get; set; }
    public T GetValue(T val) { return val; }
}

public class Test {
    public void Run(string input) {
        var w = new Wrapper<string>();
        var val = w.GetValue(input);
        Process.Start(val);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected CSSEC003 from generic method flow.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_ParamsArray_FindsBug()
        {
            var source = @"
using System.Diagnostics;

public class Helper {
    public string Join(params string[] args) {
        return string.Concat(args);
    }
}

public class Test {
    public void Run(string input) {
        var h = new Helper();
        var data = h.Join(""prefix"", input, ""suffix"");
        Process.Start(data);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            // Params array creation should aggregate taint from 'input'
            // Concat(args) should propagate taint from array elements
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected CSSEC003 from params array flow.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_LambdaBody_FindsBug()
        {
            var source = @"
using System.Diagnostics;
using System;

public class Test {
    public void Run(string input) {
        Action a = () => {
            Process.Start(input);
        };
        a();
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            // The lambda body is visited as part of Run's operation tree or as a separate symbol?
            // If it's an anonymous function operation, the walker visits it.
            // If it's a separate symbol (LocalFunction), DiscoverMethods finds it.
            // Lambdas are usually operations within the method.
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected CSSEC003 from lambda body.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_LocalFunction_FindsBug()
        {
            var source = @"
using System.Diagnostics;

public class Test {
    public void Run(string input) {
        void LocalSink(string s) {
            Process.Start(s);
        }
        LocalSink(input);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected CSSEC003 from local function.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_OutParameter_CheckSupport()
        {
            var source = @"
using System.Diagnostics;

public class Helper {
    public void Fill(string input, out string output) {
        output = input;
    }
}

public class Test {
    public void Run(string input) {
        var h = new Helper();
        string data;
        h.Fill(input, out data);
        Process.Start(data);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            // Current implementation does not support out params.
            // Explicitly assert this limitation to document behavior.
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Out parameter flow is currently not supported (should be null).");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_MaxTraceSteps_Truncates()
        {
            var source = @"
using System.Diagnostics;

public class Chain {
    public string Id(string s) => s;
}

public class Test {
    public void Run(string input) {
        var c = new Chain();
        var d1 = c.Id(input);
        var d2 = c.Id(d1);
        var d3 = c.Id(d2);
        var d4 = c.Id(d3);
        Process.Start(d4);
    }
}";
            // MaxSteps = 3. 
            // Trace: Param -> Id -> Id -> Id -> Id -> Sink. (6 steps)
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                SecurityMaxTraceSteps: 3));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding);
            Assert.IsTrue(finding.Evidence.Any(e => e.Contains("truncated")), "Evidence should be truncated.");
            Assert.IsTrue(finding.Evidence.Count <= 4, $"Evidence count ({finding.Evidence.Count}) should respect limit (3) + truncation message (1)."); 
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_Properties_CheckSupport()
        {
            var source = @"
using System.Diagnostics;

public class Data {
    public string Value { get; set; }
}

public class Test {
    public void Run(string input) {
        var d = new Data();
        d.Value = input;
        Process.Start(d.Value);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds));

            // Current implementation does not track property backing fields.
            // We expect this to FAIL to find the bug.
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Property flow is currently not supported (should be null).");
        }
    }
}
