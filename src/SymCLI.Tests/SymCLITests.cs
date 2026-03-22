using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SymCLI;
using SymSolvers.CSharpAnalysis;

namespace SymCLI.Tests
{
    [TestClass]
    public class SymCLITests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_TensorOptimization_ProducesCorrectOutputFile()
        {
            // Arrange
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                string inputPath = Path.Combine(tempDir, "input.ps");
                string outputPath = Path.Combine(tempDir, "output.txt");

                string script = @"
<Options>
  RulePacks: Tensor
  CostModel: Tensor
</Options>
Relu(TensorAdd(MatMul(A, B), C))
";
                File.WriteAllText(inputPath, script);

                // Act
                int exitCode = Program.Main(new string[] { inputPath, outputPath });

                // Assert
                Assert.AreEqual(0, exitCode, "CLI should return 0 exit code.");
                Assert.IsTrue(File.Exists(outputPath), "Output file should be created.");
                
                string result = File.ReadAllText(outputPath);
                StringAssert.Contains(result, "FusedMatMulAddRelu(A, B, C)", $"Expected fused op in output but got: {result}");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_InvalidInputPath_ReturnsErrorCode()
        {
            // Act
            int exitCode = Program.Main(new string[] { "non_existent_file.ps", "output.txt" });

            // Assert
            Assert.AreEqual(1, exitCode, "CLI should return error code 1 for missing input file.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_WritesAnalysisReport()
        {
            // Arrange
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                string inputPath = Path.Combine(tempDir, "analysis-input.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.txt");

                string source = @"
int a = 7;
int b = 2;
int c = a / b;
int d = 2 ^ 8;
";
                File.WriteAllText(inputPath, source);

                // Act
                int exitCode = Program.Main(new[] { "analyze", "csharp-math", inputPath, outputPath });

                // Assert
                Assert.AreEqual(0, exitCode, "Analyze subcommand should return 0 for successful analysis.");
                Assert.IsTrue(File.Exists(outputPath), "Analysis output file should be created.");

                string result = File.ReadAllText(outputPath);
                StringAssert.Contains(result, "C# Math Bug Analysis", "Expected analysis report header.");
                Assert.IsTrue(
                    result.Contains("CSMATH007", StringComparison.Ordinal) ||
                    result.Contains("CSMATH009", StringComparison.Ordinal),
                    $"Expected at least one math finding in output but got:{Environment.NewLine}{result}");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_DirectoryScan_SkipsBuildArtifactDirectories()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string srcDir = Path.Combine(tempDir, "src");
                string objDir = Path.Combine(tempDir, "obj");
                Directory.CreateDirectory(srcDir);
                Directory.CreateDirectory(objDir);

                string sourcePath = Path.Combine(srcDir, "real.cs");
                string objPath = Path.Combine(objDir, "generated.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.json");

                File.WriteAllText(sourcePath, "class P { int M(int a, int b) { return a / b; } }");
                File.WriteAllText(objPath, "class G { int M(int a) { return a / 0; } }");

                int exitCode = Program.Main(new[] { "analyze", "csharp-math", tempDir, outputPath, "--json" });

                Assert.AreEqual(0, exitCode, "Analyze subcommand should complete for directory input.");
                Assert.IsTrue(File.Exists(outputPath), "JSON output should be created.");

                var json = File.ReadAllText(outputPath);
                var result = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(json);
                Assert.IsNotNull(result, "Expected analysis result JSON to deserialize.");
                var findings = result!.Findings;
                Assert.AreNotEqual(0, findings.Count, "Expected at least one finding from source file.");
                Assert.IsTrue(findings.All(f =>
                        f.SourceSpan is not null &&
                        !f.SourceSpan.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)),
                    "Directory scan should skip obj folders.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_DefaultIncludesSecurity_AndMathOnlyOption()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string inputPath = Path.Combine(tempDir, "analysis-input.cs");
                string outputDefault = Path.Combine(tempDir, "analysis-default.json");
                string outputMathOnly = Path.Combine(tempDir, "analysis-math-only.json");

                string source = @"
using System.Diagnostics;
class P {
    void M(int a, int b, string cmd) {
        int q = a / b;
        Process.Start(cmd);
    }
}";
                File.WriteAllText(inputPath, source);

                int defaultExitCode = Program.Main(new[] { "analyze", "csharp-math", inputPath, outputDefault, "--json" });
                Assert.AreEqual(0, defaultExitCode, "Default analysis should succeed.");

                var defaultJson = File.ReadAllText(outputDefault);
                var defaultResult = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(defaultJson);
                Assert.IsNotNull(defaultResult, "Expected default JSON result to deserialize.");
                Assert.IsTrue(defaultResult!.Findings.Any(f => f.BugId == "CSMATH007"), "Expected CSMATH007 in default output.");
                Assert.IsTrue(defaultResult.Findings.Any(f => f.BugId == "CSSEC003"), "Expected CSSEC003 in default output.");

                int mathOnlyExitCode = Program.Main(new[] { "analyze", "csharp-math", inputPath, outputMathOnly, "--json", "--math-only" });
                Assert.AreEqual(0, mathOnlyExitCode, "Math-only analysis should succeed.");

                var mathOnlyJson = File.ReadAllText(outputMathOnly);
                var mathOnlyResult = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(mathOnlyJson);
                Assert.IsNotNull(mathOnlyResult, "Expected math-only JSON result to deserialize.");
                Assert.IsTrue(mathOnlyResult!.Findings.Any(f => f.BugId == "CSMATH007"), "Expected CSMATH007 in math-only output.");
                Assert.IsFalse(mathOnlyResult.Findings.Any(f => f.BugId.StartsWith("CSSEC", StringComparison.Ordinal)),
                    "Math-only output should exclude CSSEC findings.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_SecurityFlowModeOption_SwitchesBehavior()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string inputPath = Path.Combine(tempDir, "analysis-input.cs");
                string outputIntra = Path.Combine(tempDir, "analysis-intra.json");
                string outputSinkOnly = Path.Combine(tempDir, "analysis-sink-only.json");

                string source = @"
using System.Diagnostics;
class P {
    void M() {
        Process.Start(""dotnet --info"");
    }
}";
                File.WriteAllText(inputPath, source);

                int intraExitCode = Program.Main(new[] { "analyze", "csharp-math", inputPath, outputIntra, "--json" });
                Assert.AreEqual(0, intraExitCode, "Intra-procedural flow mode should succeed.");

                var intraJson = File.ReadAllText(outputIntra);
                var intraResult = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(intraJson);
                Assert.IsNotNull(intraResult, "Expected intra flow JSON result to deserialize.");
                Assert.IsFalse(intraResult!.Findings.Any(f => f.BugId == "CSSEC003"),
                    "Intra-procedural source/sink mode should not flag constant command literals.");

                int sinkOnlyExitCode = Program.Main(new[]
                {
                    "analyze", "csharp-math", inputPath, outputSinkOnly, "--json",
                    "--security-flow-mode", "SinkOnly"
                });
                Assert.AreEqual(0, sinkOnlyExitCode, "Sink-only mode should succeed.");

                var sinkOnlyJson = File.ReadAllText(outputSinkOnly);
                var sinkOnlyResult = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(sinkOnlyJson);
                Assert.IsNotNull(sinkOnlyResult, "Expected sink-only JSON result to deserialize.");
                Assert.IsTrue(sinkOnlyResult!.Findings.Any(f => f.BugId == "CSSEC003"),
                    "Sink-only mode should preserve legacy Process.Start sink detections.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_ExternalSourceOnlyAlias_IsAccepted()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string inputPath = Path.Combine(tempDir, "analysis-input.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.json");

                string source = @"
using System.Diagnostics;
class P {
    void M(string input) {
        Process.Start(input);
    }
}";
                File.WriteAllText(inputPath, source);

                int exitCode = Program.Main(new[]
                {
                    "analyze", "csharp-math", inputPath, outputPath,
                    "--json",
                    "--external-source-only"
                });

                Assert.AreEqual(0, exitCode, "External source alias should parse and run.");
                Assert.IsTrue(File.Exists(outputPath), "Expected JSON output file.");

                var json = File.ReadAllText(outputPath);
                var result = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(json);
                Assert.IsNotNull(result, "Expected JSON to deserialize.");
                Assert.IsFalse(result!.Findings.Any(f => f.BugId == "CSSEC003"),
                    "External-source-only should suppress findings rooted only in public parameters.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_AcceptsMaxParallelismOption()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                string inputPath = Path.Combine(tempDir, "analysis-input.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.json");

                string source = @"
class P {
    int M(int a) { return a / 0; }
}
";
                File.WriteAllText(inputPath, source);

                int exitCode = Program.Main(new[]
                {
                    "analyze", "csharp-math", inputPath, outputPath,
                    "--json",
                    "--max-parallelism", "1"
                });

                Assert.AreEqual(0, exitCode, "Analyze should succeed with --max-parallelism.");
                Assert.IsTrue(File.Exists(outputPath), "Expected JSON output file.");

                var json = File.ReadAllText(outputPath);
                var result = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(json);
                Assert.IsNotNull(result, "Expected JSON to deserialize.");
                Assert.IsTrue(result!.Findings.Any(), "Expected at least one finding.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_RepoScanInfoFlag_LoadsCustomRules()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string inputPath = Path.Combine(tempDir, "analysis-input.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.json");
                string repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");

                string source = @"
namespace Contoso.Security {
    public sealed class PromptGateway {
        public string FetchPrompt(string prompt) => prompt;
    }

    public sealed class CommandShell {
        public void Exec(string command) { }
    }
}

class P {
    void M() {
        var gateway = new Contoso.Security.PromptGateway();
        var shell = new Contoso.Security.CommandShell();
        var cmd = gateway.FetchPrompt(""run command"");
        shell.Exec(cmd);
    }
}";
                File.WriteAllText(inputPath, source);
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|append
source|AiSource|Contoso.Security.PromptGateway|FetchPrompt|method
sink|CommandInjection|Contoso.Security.CommandShell|Exec|0|CommandShell.Exec command argument
");

                int exitCode = Program.Main(new[]
                {
                    "analyze", "csharp-math", inputPath, outputPath,
                    "--json",
                    "--external-source-only",
                    "--repo-scan-info", repoScanInfoPath
                });

                Assert.AreEqual(0, exitCode, "RepoScanInfo option should parse and run.");
                Assert.IsTrue(File.Exists(outputPath), "Expected JSON output file.");

                var json = File.ReadAllText(outputPath);
                var result = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(json);
                Assert.IsNotNull(result, "Expected JSON to deserialize.");
                Assert.IsTrue(result!.Findings.Any(f => f.BugId == "CSSEC003"),
                    "Custom RepoScanInfo source/sink should produce command-injection finding.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_RepoScanInfoAutoDiscovery_LoadsRules()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string inputPath = Path.Combine(tempDir, "analysis-input.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.json");
                string repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");

                string source = @"
namespace Contoso.Security {
    public sealed class PromptGateway {
        public string FetchPrompt(string prompt) => prompt;
    }

    public sealed class CommandShell {
        public void Exec(string command) { }
    }
}

class P {
    void M() {
        var gateway = new Contoso.Security.PromptGateway();
        var shell = new Contoso.Security.CommandShell();
        var cmd = gateway.FetchPrompt(""run command"");
        shell.Exec(cmd);
    }
}";
                File.WriteAllText(inputPath, source);
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|append
source|AiSource|Contoso.Security.PromptGateway|FetchPrompt|method
sink|CommandInjection|Contoso.Security.CommandShell|Exec|0|CommandShell.Exec command argument
");

                int exitCode = Program.Main(new[]
                {
                    "analyze", "csharp-math", inputPath, outputPath,
                    "--json",
                    "--external-source-only"
                });

                Assert.AreEqual(0, exitCode, "RepoScanInfo auto-discovery should run successfully.");
                Assert.IsTrue(File.Exists(outputPath), "Expected JSON output file.");

                var json = File.ReadAllText(outputPath);
                var result = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(json);
                Assert.IsNotNull(result, "Expected JSON to deserialize.");
                Assert.IsTrue(result!.Findings.Any(f => f.BugId == "CSSEC003"),
                    "Auto-discovered RepoScanInfo should produce command-injection finding.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_RepoScanInfoRelativePath_ResolvesAgainstInputRoot()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string inputDir = Path.Combine(tempDir, "input");
            Directory.CreateDirectory(inputDir);

            var originalCwd = Environment.CurrentDirectory;
            try
            {
                string inputPath = Path.Combine(inputDir, "analysis-input.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.json");
                string repoScanInfoPath = Path.Combine(inputDir, "RepoScanInfo.txt");

                string source = @"
namespace Contoso.Security {
    public sealed class PromptGateway {
        public string FetchPrompt(string prompt) => prompt;
    }

    public sealed class CommandShell {
        public void Exec(string command) { }
    }
}

class P {
    void M() {
        var gateway = new Contoso.Security.PromptGateway();
        var shell = new Contoso.Security.CommandShell();
        var cmd = gateway.FetchPrompt(""run command"");
        shell.Exec(cmd);
    }
}";

                File.WriteAllText(inputPath, source);
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|append
source|AiSource|Contoso.Security.PromptGateway|FetchPrompt|method
sink|CommandInjection|Contoso.Security.CommandShell|Exec|0|CommandShell.Exec command argument
");

                // Set CWD to a location that does not contain RepoScanInfo.txt.
                Environment.CurrentDirectory = Path.GetTempPath();

                int exitCode = Program.Main(new[]
                {
                    "analyze", "csharp-math", inputDir, outputPath,
                    "--json",
                    "--external-source-only",
                    "--repo-scan-info", "RepoScanInfo.txt"
                });

                Assert.AreEqual(0, exitCode, "RepoScanInfo relative path option should parse and run.");
                Assert.IsTrue(File.Exists(outputPath), "Expected JSON output file.");

                var json = File.ReadAllText(outputPath);
                var result = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(json);
                Assert.IsNotNull(result, "Expected JSON to deserialize.");
                Assert.IsTrue(result!.Findings.Any(f => f.BugId == "CSSEC003"),
                    "Relative RepoScanInfo should load custom source/sink and produce command-injection finding.");
            }
            finally
            {
                Environment.CurrentDirectory = originalCwd;
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_InterproceduralIfdsMode_IsAccepted()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string inputPath = Path.Combine(tempDir, "analysis-input.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.json");

                string source = @"
using System;
using System.Diagnostics;
class P {
    static string ReadCommand() => Console.ReadLine();
    static void Execute(string cmd) => Process.Start(cmd);
    static void Run() => Execute(ReadCommand());
}
";
                File.WriteAllText(inputPath, source);

                int exitCode = Program.Main(new[]
                {
                    "analyze", "csharp-math", inputPath, outputPath,
                    "--json",
                    "--security-flow-mode", "InterproceduralIfds"
                });

                Assert.AreEqual(0, exitCode, "Interprocedural IFDS mode should parse and run successfully.");
                Assert.IsTrue(File.Exists(outputPath), "Expected JSON output file.");

                var json = File.ReadAllText(outputPath);
                var result = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(json);
                Assert.IsNotNull(result, "Expected JSON to deserialize.");
                Assert.IsTrue(result!.Findings.Any(f => f.BugId == "CSSEC003"),
                    "Expected command-injection finding in interprocedural IFDS mode.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [TestMethod]
        [Timeout(15000, CooperativeCancellation = true)]
        public void Main_AnalyzeCSharpMath_InterproceduralIdeMode_CrossFile_Detected()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                string srcDir = Path.Combine(tempDir, "src");
                Directory.CreateDirectory(srcDir);
                
                string source1 = Path.Combine(srcDir, "Source.cs");
                string source2 = Path.Combine(srcDir, "Sink.cs");
                string outputPath = Path.Combine(tempDir, "analysis-output.json");

                File.WriteAllText(source1, @"
using System;
public static class SourceProvider {
    public static string Read() => Console.ReadLine();
}");
                File.WriteAllText(source2, @"
using System.Diagnostics;
public static class SinkProvider {
    public static void Run() {
        var cmd = SourceProvider.Read();
        Process.Start(cmd);
    }
}");

                int exitCode = Program.Main(new[]
                {
                    "analyze", "csharp-math", srcDir, outputPath,
                    "--json",
                    "--security-flow-mode", "InterproceduralIde"
                });

                Assert.AreEqual(0, exitCode, "Interprocedural IDE mode should succeed on directory scan.");
                Assert.IsTrue(File.Exists(outputPath), "Expected JSON output file.");

                var json = File.ReadAllText(outputPath);
                var result = JsonSerializer.Deserialize<CSharpMathBugAnalysisResult>(json);
                Assert.IsNotNull(result, "Expected JSON to deserialize.");
                Assert.IsTrue(result!.Findings.Any(f => f.BugId == "CSSEC003"),
                    "Expected cross-file command-injection finding in IDE mode.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
