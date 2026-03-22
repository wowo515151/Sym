//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ConsoleHelmsman;

namespace AGIHelmsman.Tests
{
    [TestClass]
    public class PlanBridgeTests
    {
        private string _root = string.Empty;

        [TestInitialize]
        public void Init()
        {
            _root = Path.Combine(Path.GetTempPath(), "AGIHelmsman.PlanBridge.Tests", DateTime.Now.Ticks.ToString());
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "External", "ToTools"));
            Directory.CreateDirectory(Path.Combine(_root, "External", "FromTools"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ToolCommandXml_RoutesToSender()
        {
            var tcs = new TaskCompletionSource<(string name, string command)>();

            var bridge = new PlanBridgeService(
                _root,
                getDefinitionByName: name => null,
                getSelectedConsoleName: () => null,
                getWorkingDirectory: () => _root,
                log: _ => { },
                sendToConsoleAsync: (name, command) =>
                {
                    tcs.TrySetResult((name, command));
                    return Task.FromResult<string?>(null);
                });

            bridge.Start();

            var planPath = Path.Combine(_root, "External", "ToTools", "plan-cli.xml");
            var xml = "<ToolCommand>\n  <ToolName>testcli</ToolName>\n  <ToolInput>echo hello</ToolInput>\n</ToolCommand>";
            await File.WriteAllTextAsync(planPath, xml);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.AreEqual(tcs.Task, completed, "Timed out waiting for sendToConsoleAsync to be invoked.");

            var (name, command) = await tcs.Task;
            Assert.AreEqual("testcli", name, "CLIName mismatch");
            Assert.AreEqual("echo hello", command, "Command mismatch");

            // Ensure response file was written
            var responsePath = Path.Combine(_root, "External", "FromTools", "plan-cli.xml.fromtool.txt");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && !File.Exists(responsePath)) await Task.Delay(100);
            Assert.IsTrue(File.Exists(responsePath), "Response file not created.");

            bridge.Dispose();
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task OneShotExecution_ExecutedByBridge_WritesEchoResponse()
        {
            var bridge = new PlanBridgeService(
                _root,
                getDefinitionByName: name => new ConsoleAppBase
                {
                    Name = name,
                    Path = "cmd.exe",
                    Arguments = "/C echo {prompt}",
                    OneShot = true
                },
                getSelectedConsoleName: () => null,
                getWorkingDirectory: () => _root,
                log: _ => { },
                sendToConsoleAsync: null);

            bridge.Start();

            var planPath = Path.Combine(_root, "External", "ToTools", "plan-oneshot.txt");
            var text = "echo-from-oneshot";
            await File.WriteAllTextAsync(planPath, text);

            var responsePath = Path.Combine(_root, "External", "FromTools", "plan-oneshot.txt.fromtool.txt");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && !File.Exists(responsePath)) await Task.Delay(100);

            Assert.IsTrue(File.Exists(responsePath), "Response file not created for one-shot execution.");
            var response = await File.ReadAllTextAsync(responsePath);
            StringAssert.Contains(response, "echo-from-oneshot", "One-shot response did not contain echoed text.");

            bridge.Dispose();
        }
    }
}