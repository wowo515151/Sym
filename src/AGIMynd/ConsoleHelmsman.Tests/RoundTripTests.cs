// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;

namespace ConsoleHelmsman.Tests
{
    [TestClass]
    public class RoundTripTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        [TestCategory("Integration")]
        public async Task FullRoundTrip_MemoryToTools_ExternalRunner_WritesMemoryFromTools()
        {
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

                var manager = new BufferManager(memory, externalPlans, externalResponses);
                var mgrTask = manager.StartAsync();
                try
                {
                    // Write plan into Memory/ToTools
                    var planName = "powershell_echo_roundtrip.txt";
                    var planPath = Path.Combine(memory, "ToTools", planName);
                    var cmd = "echo 'hello world'";
                    var xml = $"<ToolCommand>\n  <ToolName>powershell</ToolName>\n  <ToolInput>{cmd}</ToolInput>\n</ToolCommand>";
                    await File.WriteAllTextAsync(planPath, xml, Encoding.UTF8);

                    // Wait for external copy
                    var externalPlanPath = Path.Combine(externalPlans, planName);
                    bool foundExternal = false;
                    for (int i = 0; i < 40; i++)
                    {
                        if (File.Exists(externalPlanPath)) { foundExternal = true; break; }
                        await Task.Delay(100);
                    }
                    Assert.IsTrue(foundExternal, "BufferManager did not copy plan to external plans folder");

                    // Parse the ToolCommand from the external plan
                    var planXml = await File.ReadAllTextAsync(externalPlanPath, Encoding.UTF8);
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ConsoleHelmsman.Models.ToolCommand));
                    ConsoleHelmsman.Models.ToolCommand? toolCmd;
                    using (var reader = new StringReader(planXml))
                    {
                        toolCmd = serializer.Deserialize(reader) as ConsoleHelmsman.Models.ToolCommand;
                    }

                    Assert.IsNotNull(toolCmd, "Failed to parse ToolCommand XML from plan.");
                    Assert.AreEqual("powershell", toolCmd!.ToolName, "ToolName mismatch");
                    cmd = toolCmd.ToolInput;

                    // Simulate external runner (ConsoleHelmsman) by executing the command in PowerShell and writing response
                    var ps = ResolveExecutable("pwsh") ?? ResolveExecutable("powershell");
                    if (ps == null)
                    {
                        Assert.Inconclusive("No PowerShell found on PATH to run external responder");
                    }

                    var responseName = planName + ".fromtool.txt";
                    var externalResponsePath = Path.Combine(externalResponses, responseName);

                    // Use ConsoleHelmsman's ConsoleProcessService to run the command and stream output into External/FromTools
                    var service = new ConsoleHelmsman.ConsoleProcessService();
                    var definition = new ConsoleHelmsman.ConsoleAppBase
                    {
                        Name = "powershell",
                        Path = ps,
                        Arguments = $"-NoLogo -NonInteractive -Command \"{cmd}; Start-Sleep -Seconds 1\"",
                        OneShot = false
                    };

                    var log = new ConsoleHelmsman.ConsoleLog(200000);
                    try
                    {
                        var started = service.Start(definition, log, out var serr);
                        Assert.IsTrue(started, $"Failed to start ConsoleProcessService: {serr}");

                        // instruct the service to write its per-instance FromTools output to our external response path
                        if (service.CurrentInstance != null)
                        {
                            service.CurrentInstance.FromToolsPath = externalResponsePath;
                        }

                        // Wait for the external response file to be written by the service
                        bool wrote = false;
                        for (int i = 0; i < 40; i++)
                        {
                            if (File.Exists(externalResponsePath)) { wrote = true; break; }
                            await Task.Delay(100);
                        }

                        Assert.IsTrue(wrote, "ConsoleProcessService did not write the external response file");
                    }
                    finally
                    {
                        try { service.Dispose(); } catch { }
                    }

                    // Wait for BufferManager to copy response back to Memory/FromTools
                    var internalResponsePath = Path.Combine(memory, "FromTools", responseName);
                    bool foundInternal = false;
                    for (int i = 0; i < 40; i++)
                    {
                        if (File.Exists(internalResponsePath)) { foundInternal = true; break; }
                        await Task.Delay(100);
                    }
                    Assert.IsTrue(foundInternal, "BufferManager did not copy external response back into Memory/FromTools");

                    var resp = await File.ReadAllTextAsync(internalResponsePath, Encoding.UTF8);
                    // Normalize whitespace/newlines before checking content to be robust across shells
                    var norm = resp.Replace("\r", "").Replace("\n", " ").Replace("\t", " ").Trim();
                    Assert.Contains("hello world", norm, $"Response did not contain expected text. Got: '{norm}'");
                }
                finally
                {
                    manager.Stop();
                    await mgrTask;
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
            foreach (var d in paths)
            {
                var c = Path.Combine(d, name);
                if (File.Exists(c)) return c;
                if (File.Exists(c + ".exe")) return c + ".exe";
            }
            return null;
        }
    }
}
