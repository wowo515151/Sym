// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class AiSourceFlowTests
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
        public void Analyze_AiQueryAsync_ToProcessStart_DetectedAsExternal()
        {
            var source = @"
using System.Diagnostics;
using System.Threading.Tasks;

namespace AGIMynd {
    public interface IMyndLLM {
        Task<string> QueryAsync(string prompt);
    }
}

public sealed class Runner {
    private readonly AGIMynd.IMyndLLM _llm;
    public Runner(AGIMynd.IMyndLLM llm) { _llm = llm; }

    public void Run() {
        var cmd = _llm.QueryAsync(""plan"").Result;
        Process.Start(cmd);
    }
}";

            var result = _analyzer.AnalyzeText(source, ExternalOnlyOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected command injection finding rooted in AiSource.");
            Assert.AreEqual(CSharpMathBugConfidence.Confirmed, finding!.Confidence, "AiSource should be treated as external untrusted input.");
            Assert.IsTrue(finding.Evidence.Any(e => e.Contains("QueryAsync")), "Evidence should include the AI source call.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_AiResponseObject_PropertyContent_PropagatesToSink()
        {
            var source = @"
using System.Diagnostics;

namespace Microsoft.Extensions.AI {
    public sealed class LlmResponse {
        public string Content { get; set; } = string.Empty;
    }

    public interface IChatClient {
        LlmResponse GetResponseAsync(string prompt);
    }
}

public sealed class Runner {
    private readonly Microsoft.Extensions.AI.IChatClient _chat;
    public Runner(Microsoft.Extensions.AI.IChatClient chat) { _chat = chat; }

    public void Run() {
        var reply = _chat.GetResponseAsync(""hello"");
        Process.Start(reply.Content);
    }
}";

            var result = _analyzer.AnalyzeText(source, ExternalOnlyOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected taint propagation from AI response object property to sink.");
            Assert.IsTrue(finding!.Evidence.Any(e => e.Contains("property", System.StringComparison.OrdinalIgnoreCase)),
                "Evidence should reflect property-based propagation.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_GenericAiLibrary_HeuristicSourceDetection_Works()
        {
            var source = @"
using System.Diagnostics;

namespace Acme.GenerativeSdk {
    public sealed class ChatKernel {
        public string GenerateAsync(string prompt) => prompt;
    }
}

public sealed class Runner {
    private readonly Acme.GenerativeSdk.ChatKernel _kernel = new Acme.GenerativeSdk.ChatKernel();

    public void Run() {
        var cmd = _kernel.GenerateAsync(""do something"");
        Process.Start(cmd);
    }
}";

            var result = _analyzer.AnalyzeText(source, ExternalOnlyOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected heuristic AI source detection for semantic-kernel-like libraries.");
            Assert.IsTrue(finding!.Evidence.Any(e => e.Contains("AI call")),
                "Evidence should mention heuristic AI source classification.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_UnknownSdk_QueryWithPromptParameter_HeuristicDetectsAiSource()
        {
            var source = @"
using System.Diagnostics;
using System.Threading.Tasks;

namespace Contoso.Runtime {
    public sealed class AgentGateway {
        public Task<string> QueryAsync(string prompt) => Task.FromResult(prompt);
    }
}

public sealed class Runner {
    private readonly Contoso.Runtime.AgentGateway _gateway = new Contoso.Runtime.AgentGateway();

    public void Run() {
        var cmd = _gateway.QueryAsync(""run a command"").Result;
        Process.Start(cmd);
    }
}";

            var result = _analyzer.AnalyzeText(source, ExternalOnlyOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Expected prompt-shaped QueryAsync method to be classified as AI-like source.");
            Assert.IsTrue(finding!.Evidence.Any(e => e.Contains("AI call")),
                "Evidence should mention heuristic AI classification.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NonAiQueryMethod_NotClassifiedAsAiSource()
        {
            var source = @"
using System.Diagnostics;

namespace Contoso.Data {
    public sealed class SqlGateway {
        public string Query(string sql) => sql;
    }
}

public sealed class Runner {
    private readonly Contoso.Data.SqlGateway _sql = new Contoso.Data.SqlGateway();

    public void Run(string sql) {
        var cmd = _sql.Query(sql);
        Process.Start(cmd);
    }
}";

            var result = _analyzer.AnalyzeText(source, ExternalOnlyOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "External-source mode should not treat generic SQL Query APIs as AiSource.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_AiCommand_ToRunToolAsync_Detected()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var source = @"
using System.Threading.Tasks;

namespace AGIMynd {
    public interface IMyndLLM {
        Task<string> QueryAsync(string prompt);
    }

    public interface IToolRunner {
        Task<string> RunToolAsync(string toolName, string args);
    }
}

public sealed class Runner {
    private readonly AGIMynd.IMyndLLM _llm;
    private readonly AGIMynd.IToolRunner _runner;

    public Runner(AGIMynd.IMyndLLM llm, AGIMynd.IToolRunner runner) {
        _llm = llm;
        _runner = runner;
    }

    public void Run() {
        var command = _llm.QueryAsync(""generate shell command"").Result;
        _runner.RunToolAsync(""powershell"", command);
    }
}";

            try
            {
                var repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|append
source|AiSource|AGIMynd.IMyndLLM|QueryAsync|method
sink|CommandInjection|AGIMynd.IToolRunner|RunToolAsync|0,1|IToolRunner.RunToolAsync tool/args argument
");

                var options = ExternalOnlyOptions with { RepoScanInfoPath = repoScanInfoPath };
                var result = _analyzer.AnalyzeText(source, options);
                var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
                Assert.IsNotNull(finding, "Expected RunToolAsync to be treated as command sink via RepoScanInfo.");
                Assert.AreEqual(CSharpMathBugConfidence.Confirmed, finding!.Confidence);
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
