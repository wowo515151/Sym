// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ConsoleHelmsman.Tests
{
    [TestClass]
    public class ConsoleProcessServiceTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ConsoleProcessService_WritesStdoutToFromToolsFile()
        {
            // Try to locate powershell on PATH; if not present mark inconclusive
            var psPath = ResolveExecutable("powershell.exe") ?? ResolveExecutable("pwsh.exe");
            if (psPath == null)
            {
                Assert.Inconclusive("PowerShell not found on PATH; cannot run streaming test.");
            }

            var service = new ConsoleHelmsman.ConsoleProcessService();

            var definition = new ConsoleHelmsman.ConsoleAppBase
            {
                Name = "powershell",
                Path = psPath,
                // Use a short command that prints then sleeps briefly so background readers can capture output.
                Arguments = "-NoLogo -NonInteractive -Command \"Write-Output 'hello world'; Start-Sleep -Seconds 1\"",
                OneShot = false
            };

            var log = new ConsoleHelmsman.ConsoleLog(200000);

            try
            {
                var started = service.Start(definition, log, out var err);
                Assert.IsTrue(started, $"Failed to start process: {err}");

                // Provide a temp FromTools file for the instance
                var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(tempDir);
                var fromToolsFile = Path.Combine(tempDir, "console_test.fromtool.txt");
                service.CurrentInstance!.FromToolsPath = fromToolsFile;

                // Wait for the background reader to write the output
                bool found = false;
                for (int i = 0; i < 40; i++)
                {
                    if (File.Exists(fromToolsFile))
                    {
                        var text = File.ReadAllText(fromToolsFile, Encoding.UTF8);
                        if (text.Contains("hello world")) { found = true; break; }
                    }
                    await Task.Delay(100);
                }

                Assert.IsTrue(found, "Expected to find 'hello world' in FromTools file written by ConsoleProcessService");
            }
            finally
            {
                try { service.Dispose(); } catch { }
            }
        }

        private static string? ResolveExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;

            var extensions = Path.HasExtension(path) ? new[] { string.Empty } : new[] { ".exe", ".cmd", ".bat", ".ps1", string.Empty };
            var searchPaths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var dir in searchPaths)
            {
                foreach (var ext in extensions)
                {
                    var candidate = Path.Combine(dir, path + ext);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            return null;
        }
    }
}
