// Copyright Warren Harding 2026
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class UserSourceAwareTests
    {
        private CSharpMathBugAnalyzer _analyzer = new CSharpMathBugAnalyzer();

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_UserSourceOnly_BlocksParameterFlow()
        {
            var source = @"
using System.Diagnostics;
public class Test {
    public void Run(string input) {
        Process.Start(input);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                PrioritizeUserSources: true));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Finding rooted in parameter should be blocked by PrioritizeUserSources: true.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_UserSourceOnly_AllowsConsoleReadLine()
        {
            var source = @"
using System;
using System.Diagnostics;
public class Test {
    public void Run() {
        var input = Console.ReadLine();
        Process.Start(input);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                PrioritizeUserSources: true));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Finding rooted in Console.ReadLine should be allowed by PrioritizeUserSources: true.");
            Assert.AreEqual(CSharpMathBugConfidence.Confirmed, finding.Confidence, "User source finding should have Confirmed confidence.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_UserSourceOnly_AllowsInterproceduralUserSource()
        {
            var source = @"
using System;
using System.Diagnostics;

public class SourceHelper {
    public static string GetData() => Console.ReadLine();
}

public class Test {
    public void Run() {
        var input = SourceHelper.GetData();
        Process.Start(input);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                PrioritizeUserSources: true));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Interprocedural user source should be allowed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_UserSourceOnly_AllowsEnvironmentVariable()
        {
            var source = @"
using System;
using System.Diagnostics;
public class Test {
    public void Run() {
        var input = Environment.GetEnvironmentVariable(""PATH"");
        Process.Start(input);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                PrioritizeUserSources: true));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Environment variable should be treated as a UserSource.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_MixedSources_PrioritizesUserSource()
        {
            var source = @"
using System;
using System.Diagnostics;
public class Test {
    public void Run(string safeParam) {
        var untrusted = Console.ReadLine();
        Process.Start(untrusted + safeParam);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                PrioritizeUserSources: true));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Finding should be allowed if it contains AT LEAST ONE user source.");
            Assert.AreEqual(CSharpMathBugConfidence.Confirmed, finding.Confidence, "Confirmed confidence should be preserved in mixed flows.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_IntraProcedural_UserSourceAwareness()
        {
            var source = @"
using System;
using System.Diagnostics;
public class Test {
    public void Run() {
        var input = Console.ReadLine();
        Process.Start(input);
    }
}";
            // Test filtering in Intra mode
            var resultFiltered = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.IntraProcedural,
                PrioritizeUserSources: true));
            Assert.IsNotNull(resultFiltered.Findings.FirstOrDefault(f => f.BugId == "CSSEC003"), "Intra mode should allow UserSource.");

            var sourceParam = @"
using System.Diagnostics;
public class Test {
    public void Run(string p) {
        Process.Start(p);
    }
}";
            var resultFilteredParam = _analyzer.AnalyzeText(sourceParam, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.IntraProcedural,
                PrioritizeUserSources: true));
            Assert.IsNull(resultFilteredParam.Findings.FirstOrDefault(f => f.BugId == "CSSEC003"), "Intra mode should block PublicParameter when prioritized.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_SanitizedUserSource_IsStillBlocked()
        {
            var source = @"
using System;
using System.IO;
using System.Diagnostics;
public class Test {
    public void Run() {
        var input = Console.ReadLine();
        var safe = Path.GetFileName(input);
        Process.Start(safe);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                PrioritizeUserSources: true));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Sanitizer should block flow even if it originated from a UserSource.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_StandardMode_AllowsParameterFlowWithMediumConfidence()
        {
            var source = @"
using System.Diagnostics;
public class Test {
    public void Run(string input) {
        Process.Start(input);
    }
}";
            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                SecurityFlowMode: CSharpSecurityFlowMode.InterproceduralIfds,
                PrioritizeUserSources: false));

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Parameter flow should be allowed in default mode.");
            Assert.AreEqual(CSharpMathBugConfidence.Medium, finding.Confidence, "Parameter-based finding should have Medium confidence.");
        }
    }
}
