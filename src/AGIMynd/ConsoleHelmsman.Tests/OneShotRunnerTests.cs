// Copyright Warren Harding 2026
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ConsoleHelmsman.Tests;

[TestClass]
public sealed class OneShotRunnerTests
{
    private const int DefaultTimeoutMs = 15000;
    private const int LogTailChars = 80000;

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    [TestCategory("Integration")]
    public async Task OneShot_PowerShell_Help_UsesStdin()
    {
        var definition = new ConsoleAppBase
        {
            Name = "powershell",
            Path = "powershell.exe",
            Arguments = "-NoLogo -NonInteractive -Command -",
            OneShot = true
        };

        var output = await RunOneShotAndCaptureAsync(definition, "Get-Help Get-Process");

        Assert.IsTrue(
            output.Contains("Get-Process", StringComparison.OrdinalIgnoreCase),
            "Expected PowerShell help output to include Get-Process.");
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    [TestCategory("Integration")]
    public async Task OneShot_Codex_Help_Output()
    {
        var definition = LoadConfigDefinition("codex");
        if (definition == null)
        {
            Assert.Inconclusive("codex is not configured in consoles.config.");
        }

        definition.Arguments = "--help";
        definition.OneShot = true;

        var output = await RunOneShotAndCaptureAsync(definition, "help");

        Assert.IsTrue(
            output.Contains("Usage", StringComparison.OrdinalIgnoreCase),
            "Expected codex --help output to include Usage.");
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    [TestCategory("Integration")]
    [Ignore("Gemini CLI help can hang under redirected, non-interactive test execution in this environment.")]
    public async Task OneShot_Gemini_Help_Output()
    {
        var definition = LoadConfigDefinition("gemini");
        if (definition == null)
        {
            Assert.Inconclusive("gemini is not configured in consoles.config.");
        }

        definition.Arguments = "--help";
        definition.OneShot = true;

        var output = await RunOneShotAndCaptureAsync(definition, "help");

        Assert.IsTrue(
            output.Contains("Usage", StringComparison.OrdinalIgnoreCase),
            "Expected gemini --help output to include Usage.");
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    [TestCategory("Integration")]
    public async Task OneShot_Copilot_Help_Output()
    {
        var definition = LoadConfigDefinition("copilot");
        if (definition == null)
        {
            Assert.Inconclusive("copilot is not configured in consoles.config.");
        }

        definition.Arguments = "--help";
        definition.OneShot = true;

        var output = await RunOneShotAndCaptureAsync(definition, "help");

        Assert.IsTrue(
            output.Contains("Usage", StringComparison.OrdinalIgnoreCase),
            "Expected copilot --help output to include Usage.");
    }

    private static async Task<string> RunOneShotAndCaptureAsync(ConsoleAppBase definition, string input)
    {
        var resolvedPath = ResolveExecutable(definition.Path);
        if (resolvedPath == null)
        {
            Assert.Inconclusive($"Executable not found for {definition.Name}: {definition.Path}");
        }

        definition.Path = resolvedPath;

        using var processService = new ConsoleProcessService();
        var log = new ConsoleLog(200000);
        var runner = new OneShotRunner(processService);

        var result = runner.Run(definition, log, input);
        if (!result.Started)
        {
            Assert.Fail($"Failed to start {definition.Name}: {result.Error}");
        }

        if (!string.IsNullOrEmpty(result.Error))
        {
            Assert.Fail($"Failed to send input to {definition.Name}: {result.Error}");
        }

        var instance = processService.CurrentInstance;
        if (instance == null)
        {
            Assert.Fail($"No process instance created for {definition.Name}.");
        }

        if (!instance.Process.WaitForExit(DefaultTimeoutMs))
        {
            try
            {
                instance.Process.Kill(true);
            }
            catch
            {
            }

            Assert.Fail($"{definition.Name} did not exit within {DefaultTimeoutMs}ms.");
        }

        await Task.Delay(200);
        return log.GetTail(LogTailChars);
    }

    private static ConsoleAppBase? LoadConfigDefinition(string name)
    {
        var configPath = GetConfigPath();
        if (!File.Exists(configPath))
        {
            Assert.Inconclusive($"consoles.config not found at {configPath}");
        }

        var serializer = new XmlSerializer(typeof(ConsoleAppBases));
        using var stream = File.OpenRead(configPath);
        if (serializer.Deserialize(stream) is not ConsoleAppBases config)
        {
            return null;
        }

        var item = config.Items.FirstOrDefault(entry =>
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

        if (item == null)
        {
            return null;
        }

        return new ConsoleAppBase
        {
            Name = item.Name,
            Path = item.Path,
            Arguments = item.Arguments,
            OneShot = item.OneShot
        };
    }

    private static string GetConfigPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "ConsoleHelmsman",
            "consoles.config"));
    }

    private static string? ResolveExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathRooted(path))
        {
            return File.Exists(path) ? path : null;
        }

        var extensions = Path.HasExtension(path)
            ? new[] { string.Empty }
            : new[] { ".exe", ".cmd", ".bat", ".ps1", string.Empty };

        var searchPaths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in searchPaths)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, path + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
