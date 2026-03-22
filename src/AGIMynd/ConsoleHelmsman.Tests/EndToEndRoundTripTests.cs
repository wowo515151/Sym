//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;

namespace ConsoleHelmsman.Tests
{
    [TestClass]
    public class EndToEndRoundTripTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        [TestCategory("Integration")]
        public async Task RoundTrip_ToTools_To_ConsoleHelmsman_To_FromTools()
        {
            // Setup temp repo layout
            var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
            try
            {
                var memory = Path.Combine(tempRoot, "Memory");
                var externalPlans = Path.Combine(tempRoot, "External", "ToTools");
                var externalResponses = Path.Combine(tempRoot, "External", "FromTools");
                Directory.CreateDirectory(memory);
                Directory.CreateDirectory(Path.Combine(memory, "ToTools"));
                Directory.CreateDirectory(Path.Combine(memory, "FromTools"));
                Directory.CreateDirectory(externalPlans);
                Directory.CreateDirectory(externalResponses);

                // Start BufferManager using these paths
                var manager = new BufferManager(memory, externalPlans, externalResponses);
                var bmTask = manager.StartAsync();

                try
                {
                    // Write a plan into Memory/ToTools
                    var planName = "powershell_echo_test.txt";
                    var planPath = Path.Combine(memory, "ToTools", planName);
                    var content = "echo \"hello world\"";
                    var xml = $"<ToolCommand>\n  <ToolName>powershell</ToolName>\n  <ToolInput>{content}</ToolInput>\n</ToolCommand>";
                    await File.WriteAllTextAsync(planPath, xml, Encoding.UTF8);

                    // Wait for BufferManager to copy to external plans
                    var externalPlanPath = Path.Combine(externalPlans, planName);
                    bool copiedToExternal = false;
                    for (int i = 0; i < 40; i++)
                    {
                        if (File.Exists(externalPlanPath)) { copiedToExternal = true; break; }
                        await Task.Delay(100);
                    }
                    Assert.IsTrue(copiedToExternal, "Plan was not copied to External/ToTools by BufferManager");

                    // Parse the ToolCommand from the external plan (simulating ConsoleHelmsman)
                    var planXml = await File.ReadAllTextAsync(externalPlanPath, Encoding.UTF8);
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ConsoleHelmsman.Models.ToolCommand));
                    ConsoleHelmsman.Models.ToolCommand? toolCmd;
                    using (var reader = new StringReader(planXml))
                    {
                        toolCmd = serializer.Deserialize(reader) as ConsoleHelmsman.Models.ToolCommand;
                    }

                    Assert.IsNotNull(toolCmd, "Failed to parse ToolCommand XML from plan.");
                    Assert.AreEqual("powershell", toolCmd!.ToolName, "ToolName mismatch");
                    var command = toolCmd.ToolInput;

                    // Simulate ConsoleHelmsman: read the external plan and run it via powershell
                    string? ps = ResolveExecutable("pwsh") ?? ResolveExecutable("powershell");
                    if (ps == null)
                    {
                        Assert.Inconclusive("No PowerShell executable found on PATH to run external responder");
                    }

                    // Simulate external responder reading the plan and writing a response
                    var externalResponseName = planName + ".fromtool.txt";
                    var externalResponsePath = Path.Combine(externalResponses, externalResponseName);

                    // Start the process directly to execute the command
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ps,
                        Arguments = $"-NoLogo -NonInteractive -Command \"{command}; Start-Sleep -Seconds 1\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    var p = System.Diagnostics.Process.Start(startInfo)!;
                    var output = await p.StandardOutput.ReadToEndAsync();
                    await p.WaitForExitAsync();

                    // Write response in External/FromTools same as ConsoleHelmsman would
                    await File.WriteAllTextAsync(externalResponsePath, output, Encoding.UTF8);

                    // Wait for BufferManager to copy the response into Memory/FromTools
                    var internalResponsePath = Path.Combine(memory, "FromTools", externalResponseName);
                    bool copiedToMemory = false;
                    for (int i = 0; i < 40; i++)
                    {
                        if (File.Exists(internalResponsePath)) { copiedToMemory = true; break; }
                        await Task.Delay(100);
                    }

                    Assert.IsTrue(copiedToMemory, "Response was not copied to Memory/FromTools by BufferManager");

                    var resp = await File.ReadAllTextAsync(internalResponsePath, Encoding.UTF8);
                    // PowerShell may emit line breaks depending on how the command is invoked.
                    StringAssert.Contains(resp, "hello");
                    StringAssert.Contains(resp, "world");
                }
                finally
                {
                    manager.Stop();
                    await bmTask;
                }
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }

        private static string? ResolveExecutable(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var dir in paths)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
                if (File.Exists(candidate + ".exe")) return candidate + ".exe";
            }
            return null;
        }
    }
}
