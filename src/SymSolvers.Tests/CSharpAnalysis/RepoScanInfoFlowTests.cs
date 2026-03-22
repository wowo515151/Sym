// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class RepoScanInfoFlowTests
    {
        private readonly CSharpMathBugAnalyzer _analyzer = new();

        private static CSharpMathBugAnalyzerOptions ExternalOnlyOptions =>
            CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                PrioritizeUserSources = true,
                ConfidenceThreshold = 0.0
            };

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_CustomSourceAndSink_FromRepoScanInfo_Detected()
        {
            var source = @"
namespace Contoso.Security {
    public sealed class PromptGateway {
        public string FetchPrompt(string prompt) => prompt;
    }

    public sealed class CommandShell {
        public void Exec(string command) { }
    }
}

public sealed class Runner {
    private readonly Contoso.Security.PromptGateway _gateway = new();
    private readonly Contoso.Security.CommandShell _shell = new();

    public void Run() {
        var cmd = _gateway.FetchPrompt(""do thing"");
        _shell.Exec(cmd);
    }
}";

            var baseline = _analyzer.AnalyzeText(source, ExternalOnlyOptions);
            Assert.IsNull(baseline.Findings.FirstOrDefault(f => f.BugId == "CSSEC003"),
                "Without RepoScanInfo, unknown sink/source should not be reported.");

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|append
source|AiSource|Contoso.Security.PromptGateway|FetchPrompt|method
sink|CommandInjection|Contoso.Security.CommandShell|Exec|0|CommandShell.Exec command argument
");

                var options = ExternalOnlyOptions with { RepoScanInfoPath = repoScanInfoPath };
                var configured = _analyzer.AnalyzeText(source, options);
                var finding = configured.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
                Assert.IsNotNull(finding, "RepoScanInfo should enable custom source/sink detection.");
                Assert.AreEqual(CSharpMathBugConfidence.Confirmed, finding!.Confidence, "AiSource from RepoScanInfo should be external/confirmed.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_RepoScanInfoReplaceModel_DropsDefaultRules()
        {
            var source = @"
using System;
using System.Diagnostics;

public sealed class Runner {
    public void Run() {
        var cmd = Console.ReadLine();
        Process.Start(cmd);
    }
}";

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|replace
source|AiSource|Contoso.Security.PromptGateway|FetchPrompt|method
sink|CommandInjection|Contoso.Security.CommandShell|Exec|0|CommandShell.Exec command argument
");

                var options = ExternalOnlyOptions with { RepoScanInfoPath = repoScanInfoPath };
                var result = _analyzer.AnalyzeText(source, options);
                Assert.IsNull(result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003"),
                    "Replace mode should suppress built-in Process.Start sink rules.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_InvalidRepoScanInfo_ReportsDiagnostic()
        {
            var source = "class P { void M() { } }";

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");
                File.WriteAllText(repoScanInfoPath, @"
version|1
sink|UnknownKind|Contoso.Security.CommandShell|Exec|0|bad kind
");

                var options = CSharpMathBugAnalyzerOptions.Default with
                {
                    SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                    RepoScanInfoPath = repoScanInfoPath
                };

                var result = _analyzer.AnalyzeText(source, options);
                Assert.IsTrue(result.Diagnostics.Any(d => d.Contains("Invalid SecurityFlowKind", StringComparison.Ordinal)),
                    "Invalid RepoScanInfo directives should be surfaced via diagnostics.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }
}
