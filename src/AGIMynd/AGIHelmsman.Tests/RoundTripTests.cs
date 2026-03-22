//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;
using ConsoleHelmsman;

namespace AGIHelmsman.Tests
{
    [TestClass]
    public class RoundTripTests
    {
        private string _root = string.Empty;
        public TestContext? TestContext { get; set; }

        [TestInitialize]
        public void Init()
        {
            _root = Path.Combine(Path.GetTempPath(), "AGIHelmsman.Tests", DateTime.Now.Ticks.ToString());
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "External", "ToTools"));
            Directory.CreateDirectory(Path.Combine(_root, "External", "FromTools"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch { }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task PlanToCmdEcho_RoundTrip_WritesResponse()
        {
            // Arrange
            TestContext?.WriteLine($"Test root: {_root}");
            var memoryRoot = Path.Combine(_root, "Memory");
            Directory.CreateDirectory(memoryRoot);

            // Create a simple AGIMynd BufferManager to push Plans from memory to External
            var plansBuffer = Path.Combine(_root, "External", "ToTools");
            var responsesBuffer = Path.Combine(_root, "External", "FromTools");

            var bufferManager = new BufferManager(memoryRoot, plansBuffer, responsesBuffer);

            // Create a fake 'copilot' console definition which uses cmd.exe to echo input
            var copilot = new ConsoleAppBase
            {
                Name = "copilot",
                Path = "cmd.exe",
                Arguments = "/C echo {prompt}",
                OneShot = true
            };

            // Start PlanBridgeService pointing at repo root
            var planBridge = new PlanBridgeService(
                repoRoot: _root,
                getDefinitionByName: name => string.Equals(name, "copilot", StringComparison.OrdinalIgnoreCase) ? copilot : null,
                getSelectedConsoleName: () => null,
                getWorkingDirectory: () => memoryRoot,
                log: _ => { /* log */ },
                sendToConsoleAsync: null);
            planBridge.Start();
            TestContext?.WriteLine("PlanBridge started");

            // Start BufferManager in background
            var cts = new System.Threading.CancellationTokenSource();
            var bufferTask = Task.Run(() => bufferManager.StartAsync(cts.Token));

            try
            {
                // Act: write a plan file into memory ToTools folder (it should be moved by BufferManager)
                var memoryPlans = Path.Combine(memoryRoot, "ToTools");
                Directory.CreateDirectory(memoryPlans);
                var planPath = Path.Combine(memoryPlans, "plan1.txt");
                var planText = "hello-from-plan";
                var toolXml = $"<ToolCommand>\n  <ToolName>copilot</ToolName>\n  <ToolInput>{planText}</ToolInput>\n</ToolCommand>";
                await File.WriteAllTextAsync(planPath, toolXml);
                TestContext?.WriteLine($"Wrote plan to {planPath}");

                // Wait up to 10s for response to appear
                var responsePath = Path.Combine(responsesBuffer, "plan1.txt.fromtool.txt");
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(responsePath)) break;
                    TestContext?.WriteLine($"Waiting for response at {responsePath}...");
                    await Task.Delay(200);
                }

                // Assert
                Assert.IsTrue(File.Exists(responsePath), "Response file was not created.");
                var response = await File.ReadAllTextAsync(responsePath);
                TestContext?.WriteLine($"Response: {response}");
                StringAssert.Contains(response, "hello-from-plan", "Response does not contain echoed text.");
            }
            finally
            {
                planBridge.Dispose();
                cts.Cancel();
                try { await bufferTask; } catch { }
            }
        }
    }
}
