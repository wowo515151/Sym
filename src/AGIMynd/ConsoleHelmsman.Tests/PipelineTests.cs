//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AGIMynd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ConsoleHelmsman.Tests
{
    [TestClass]
    public class PipelineTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        [TestCategory("Integration")]
        public async Task Pipeline_Agent_To_Copilot_RoundTrip()
        {
            // 1. Setup temp directories
            var tempRoot = Path.Combine(Path.GetTempPath(), "SymPipelineTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempRoot);
            
            var memoryRoot = Path.Combine(tempRoot, "Memory");
            var externalRoot = Path.Combine(tempRoot, "External");
            var toTools = Path.Combine(externalRoot, "ToTools");
            var fromTools = Path.Combine(externalRoot, "FromTools");
            var memoryToTools = Path.Combine(memoryRoot, "ToTools");
            var memoryFromTools = Path.Combine(memoryRoot, "FromTools");

            Directory.CreateDirectory(toTools);
            Directory.CreateDirectory(fromTools);
            Directory.CreateDirectory(memoryToTools);
            Directory.CreateDirectory(memoryFromTools);

            // 2. Initialize BufferManager (Agent side)
            var bufferManager = new BufferManager(memoryRoot, toTools, fromTools);
            var bufferCts = new CancellationTokenSource();
            var bufferTask = bufferManager.StartAsync(bufferCts.Token);

            // 3. Initialize PlanBridgeService (ConsoleHelmsman side)
            // Mock definitions
            ConsoleAppBase? GetDefinition(string name)
            {
                if (string.Equals(name, "copilot", StringComparison.OrdinalIgnoreCase))
                {
                    // Return a definition that simulates Copilot using PowerShell
                    // This allows us to verify argument substitution works as expected.
                    return new ConsoleAppBase
                    {
                        Name = "copilot",
                        // Use powershell to echo the prompt
                        Path = "powershell", 
                        Arguments = "-NoLogo -NonInteractive -Command \"Write-Output 'Simulated Copilot: {prompt}'\"",
                        OneShot = true
                    };
                }
                return null;
            }

            var bridgeService = new PlanBridgeService(
                tempRoot, // Repo root containing External/ToTools
                GetDefinition,
                () => "copilot", // Default selected console
                () => tempRoot, // Working directory
                log => Console.WriteLine($"[PlanBridge] {log}")
            );

            bridgeService.Start();

            try
            {
                // 4. Simulate Agent creating a request
                var requestContent = "Time series forecasting research";
                var toolXml = $"<ToolCommand><ToolName>copilot</ToolName><ToolInput>{requestContent}</ToolInput></ToolCommand>";
                var requestFileName = "agent_request.txt";
                var requestFilePath = Path.Combine(memoryToTools, requestFileName);

                await File.WriteAllTextAsync(requestFilePath, toolXml, Encoding.UTF8);

                // 5. Wait for the round trip
                // Agent -> BufferManager -> External/ToTools -> PlanBridge -> Process -> External/FromTools -> BufferManager -> Memory/FromTools

                var expectedResponseFile = Path.Combine(memoryFromTools, requestFileName + ".fromtool.txt");
                bool found = false;
                string responseContent = "";

                // Wait up to 10 seconds
                for (int i = 0; i < 100; i++)
                {
                    if (File.Exists(expectedResponseFile))
                    {
                        // Ensure it's fully written (simple check)
                        try
                        {
                            responseContent = await File.ReadAllTextAsync(expectedResponseFile);
                            if (!string.IsNullOrEmpty(responseContent))
                            {
                                found = true;
                                break;
                            }
                        }
                        catch { }
                    }
                    await Task.Delay(100);
                }

                Assert.IsTrue(found, $"Response file not found at {expectedResponseFile} within timeout.");
                
                // 6. Verify content
                // PowerShell might wrap output or add newlines, so we check for containment
                StringAssert.Contains(responseContent, "Simulated Copilot: Time series forecasting research", "Response did not contain expected output from simulated tool.");
            }
            finally
            {
                // Cleanup
                bufferCts.Cancel();
                try { await bufferTask; } catch { }
                bridgeService.Dispose();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }
}
