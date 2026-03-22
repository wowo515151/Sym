using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class RepoScanInfoParserTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Parse_BomPrefixedVersionLine_IsAccepted()
        {
            var content = "\uFEFFversion|1\nmodel|append\n";
            var info = CSharpRepoScanInfoLoader.Parse(content, sourceName: "bom.txt");

            Assert.IsFalse(info.Diagnostics.Any(), "Expected BOM-prefixed version line to parse without diagnostics.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Parse_DuplicateSinkEntries_LaterWins_AndIsNormalized()
        {
            var content = @"
version|1
model|append

sink|CommandInjection|System.Diagnostics.Process|Start|0,1|FIRST
sink|CommandInjection|System.Diagnostics.Process|Start|0,1|SECOND
";

            var info = CSharpRepoScanInfoLoader.Parse(content, sourceName: "dupe.txt");

            Assert.AreEqual(1, info.Model.Sinks.Length, "Expected duplicate logical sinks to be normalized to a single entry.");
            Assert.AreEqual("SECOND", info.Model.Sinks[0].Description, "Later duplicate sink entry should override earlier one.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Merge_OverlayReplacesMatchingBaselineSinkByKey()
        {
            var baseline = new CSharpSecurityFlowModel(
                Sources: ImmutableArray<SourceSpec>.Empty,
                Sinks: ImmutableArray.Create(
                    new SinkSpec(
                        TypeName: "System.Diagnostics.Process",
                        MethodName: "Start",
                        TaintedIndices: new[] { 0, 1 },
                        Kind: SecurityFlowKind.CommandInjection,
                        Description: "BASE")),
                Sanitizers: ImmutableArray<SanitizerSpec>.Empty);

            var overlay = new CSharpSecurityFlowModel(
                Sources: ImmutableArray<SourceSpec>.Empty,
                Sinks: ImmutableArray.Create(
                    new SinkSpec(
                        TypeName: "System.Diagnostics.Process",
                        MethodName: "Start",
                        TaintedIndices: new[] { 0, 1 },
                        Kind: SecurityFlowKind.CommandInjection,
                        Description: "OVERLAY")),
                Sanitizers: ImmutableArray<SanitizerSpec>.Empty);

            var merged = CSharpSecurityFlowModelCatalog.Merge(baseline, overlay);
            Assert.AreEqual(1, merged.Sinks.Length, "Expected a single merged sink entry.");
            Assert.AreEqual("OVERLAY", merged.Sinks[0].Description, "Overlay should replace baseline for matching logical key.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void AnalyzeText_RepoScanInfoSinkOverride_AffectsEvidenceDescription()
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
model|append
sink|CommandInjection|System.Diagnostics.Process|Start|0,1|OVERRIDE_DESCRIPTION
");

                var analyzer = new CSharpMathBugAnalyzer();
                var options = CSharpMathBugAnalyzerOptions.Default with
                {
                    SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                    ConfidenceThreshold = 0.0,
                    RepoScanInfoPath = repoScanInfoPath
                };

                var result = analyzer.AnalyzeText(source, options);
                var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
                Assert.IsNotNull(finding, "Expected command injection finding.");

                Assert.IsTrue(
                    finding!.Evidence.Any(e => e.Contains("OVERRIDE_DESCRIPTION", StringComparison.Ordinal)),
                    "Expected RepoScanInfo sink description override to appear in evidence.");
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
