// Copyright Warren Harding 2026
#nullable enable
using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class SinkComprehensivenessDraftTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Core_MapBugId_AllSecurityFlowKinds_AreMappedAndRoundTrip()
        {
            foreach (SecurityFlowKind kind in Enum.GetValues(typeof(SecurityFlowKind)))
            {
                var bugId = CSharpSecurityFlowCore.MapBugId(kind);
                Assert.AreNotEqual("CSSEC999", bugId, $"Expected mapped bug id for sink kind '{kind}'.");
                Assert.IsTrue(CSharpBugCatalog.Bugs.ContainsKey(bugId), $"Missing bug catalog entry for '{bugId}' ({kind}).");

                Assert.IsTrue(
                    CSharpSecurityFlowCore.TryMapBugIdToSinkKind(bugId, out var roundTripKind),
                    $"Expected reverse mapping for '{bugId}'.");
                Assert.AreEqual(kind, roundTripKind, $"Round-trip bug-id mapping mismatch for '{kind}'.");
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void DefaultModel_ContainsExpandedSinkFamilies()
        {
            var model = CSharpSecurityFlowModelCatalog.Default;

            AssertHasSink(model, SecurityFlowKind.CommandInjection, "System.Diagnostics.ProcessStartInfo", ".ctor", 0);
            AssertHasSink(model, SecurityFlowKind.PathTraversal, "System.IO.File", "WriteAllText", 0);
            AssertHasSink(model, SecurityFlowKind.SqlInjection, "System.Data.SqlClient.SqlCommand", ".ctor", 0);
            AssertHasSink(model, SecurityFlowKind.LdapInjection, "System.DirectoryServices.DirectorySearcher", ".ctor");
            AssertHasSink(model, SecurityFlowKind.XpathInjection, "System.Xml.XPath.XPathExpression", "Compile", 0);
            AssertHasSink(model, SecurityFlowKind.RedirectInjection, "Microsoft.AspNetCore.Http.HttpResponse", "Redirect", 0);
            AssertHasSink(model, SecurityFlowKind.HeaderInjection, "System.Web.HttpResponse", "AddHeader");
            AssertHasSink(model, SecurityFlowKind.TemplateInjection, "Scriban.Template", "Parse", 0);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_CustomSqlSink_ReportsWithoutGuardSpecificRules()
        {
            var source = @"
using System;

namespace Contoso.Data {
    public sealed class SqlRunner {
        public void ExecuteQuery(string sql) { }
    }
}

public sealed class Runner {
    private readonly Contoso.Data.SqlRunner _sql = new();

    public void Run() {
        var query = Console.ReadLine();
        _sql.ExecuteQuery(query);
    }
}";

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|append
sink|SqlInjection|Contoso.Data.SqlRunner|ExecuteQuery|0|SqlRunner.ExecuteQuery SQL argument
");

                var analyzer = new CSharpMathBugAnalyzer();
                var options = CSharpMathBugAnalyzerOptions.Default with
                {
                    SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                    ConfidenceThreshold = 0.0,
                    RepoScanInfoPath = repoScanInfoPath
                };

                var result = analyzer.AnalyzeText(source, options);
                var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC031");
                Assert.IsNotNull(finding, "Expected SQL-injection finding for custom sink kind without dedicated guard mapping.");
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
        public void Analyze_IntraProcedural_ProcessStartInfoCtor_DetectedAsCommandSink()
        {
            var source = @"
using System;
using System.Diagnostics;

public sealed class Runner {
    public void Run() {
        var cmd = Console.ReadLine();
        var psi = new ProcessStartInfo(cmd);
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            });

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected ProcessStartInfo constructor to be treated as command-injection sink.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_IntraProcedural_FileWriteAllText_DetectedAsPathSink()
        {
            var source = @"
using System;
using System.IO;

public sealed class Runner {
    public void Run() {
        var path = Console.ReadLine();
        File.WriteAllText(path, ""data"");
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            });

            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC004");
            Assert.IsNotNull(finding, "Expected File.WriteAllText to be treated as a path-traversal sink.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_CustomRedirectSink_ProducesRedirectBugId()
        {
            var source = @"
using System;

namespace Contoso.Web {
    public sealed class Redirector {
        public void Redirect(string url) { }
    }
}

public sealed class Runner {
    private readonly Contoso.Web.Redirector _redirector = new();

    public void Run() {
        var target = Console.ReadLine();
        _redirector.Redirect(target);
    }
}";

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|append
sink|RedirectInjection|Contoso.Web.Redirector|Redirect|0|Redirector.Redirect target argument
");

                var analyzer = new CSharpMathBugAnalyzer();
                var options = CSharpMathBugAnalyzerOptions.Default with
                {
                    SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                    ConfidenceThreshold = 0.0,
                    RepoScanInfoPath = repoScanInfoPath
                };

                var result = analyzer.AnalyzeText(source, options);
                var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC034");
                Assert.IsNotNull(finding, "Expected redirect-injection sink kind to map to CSSEC034.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void AssertHasSink(
            CSharpSecurityFlowModel model,
            SecurityFlowKind kind,
            string typeName,
            string methodName,
            params int[] expectedIndices)
        {
            var matched = model.Sinks.Any(s =>
                s.Kind == kind &&
                string.Equals(s.TypeName, typeName, StringComparison.Ordinal) &&
                string.Equals(s.MethodName, methodName, StringComparison.Ordinal) &&
                (expectedIndices.Length == 0 || expectedIndices.All(i => s.TaintedIndices.Contains(i))));

            Assert.IsTrue(
                matched,
                $"Expected sink kind={kind}, member={typeName}.{methodName}, indices=[{string.Join(",", expectedIndices)}].");
        }
    }
}
