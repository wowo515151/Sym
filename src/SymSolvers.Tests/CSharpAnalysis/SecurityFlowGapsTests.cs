#nullable enable
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class SecurityFlowGapsTests
    {
        private CSharpMathBugAnalyzer _analyzer = null!;
        private CSharpMathBugAnalyzerOptions _options = null!;

        [TestInitialize]
        public void Setup()
        {
            _analyzer = new CSharpMathBugAnalyzer();
            _options = CSharpMathBugAnalyzerOptions.Default with 
            { 
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0 
            };
        }

        private CSharpMathBugFinding? AnalyzeAndGetFinding(string source, string bugId)
        {
            var result = _analyzer.AnalyzeText(source, _options);
            return result.Findings.FirstOrDefault(f => f.BugId == bugId);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_InterpolatedString_PropagatesTaint()
        {
            var source = @"
using System;
using System.Diagnostics;

public class Test {
    public void Run(string input) {
        var cmd = $""echo {input}"";
        Process.Start(cmd);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC003");
            Assert.IsNotNull(finding, "Interpolated string should propagate taint.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_StringFormat_PropagatesTaint()
        {
            var source = @"
using System;
using System.Diagnostics;

public class Test {
    public void Run(string input) {
        var cmd = string.Format(""echo {0}"", input);
        Process.Start(cmd);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC003");
            Assert.IsNotNull(finding, "String.Format should propagate taint.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_StringConcat_Method_PropagatesTaint()
        {
            var source = @"
using System;
using System.Diagnostics;

public class Test {
    public void Run(string input) {
        var cmd = string.Concat(""echo "", input);
        Process.Start(cmd);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC003");
            Assert.IsNotNull(finding, "String.Concat should propagate taint.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Ternary_PropagatesTaint()
        {
            var source = @"
using System;
using System.Diagnostics;

public class Test {
    public void Run(string input, bool condition) {
        var cmd = condition ? input : ""safe"";
        Process.Start(cmd);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC003");
            Assert.IsNotNull(finding, "Ternary operator should propagate taint from true branch.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NullCoalescing_PropagatesTaint()
        {
            var source = @"
using System;
using System.Diagnostics;

public class Test {
    public void Run(string input) {
        var cmd = input ?? ""safe"";
        Process.Start(cmd);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC003");
            Assert.IsNotNull(finding, "Null coalescing operator should propagate taint.");
        }
    }
}
