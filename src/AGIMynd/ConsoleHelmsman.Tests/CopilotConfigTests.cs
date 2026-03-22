// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ConsoleHelmsman.Tests;

[TestClass]
public sealed class CopilotConfigTests
{
    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void Copilot_Arguments_ContainsModelFlag()
    {
        var configPath = GetConfigPath();
        if (!File.Exists(configPath))
        {
            Assert.Inconclusive($"consoles.config not found at {configPath}");
        }

        var serializer = new XmlSerializer(typeof(ConsoleAppBases));
        using var stream = File.OpenRead(configPath);
        var config = serializer.Deserialize(stream) as ConsoleAppBases ?? throw new InvalidOperationException("Failed to deserialize consoles.config");

        var item = config.Items.FirstOrDefault(i => string.Equals(i.Name, "copilot", StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            Assert.Inconclusive("copilot not configured in consoles.config.");
        }

        StringAssert.Contains(item.Arguments, "--model", "Expected --model flag to be present in copilot Arguments.");
        StringAssert.Contains(item.Arguments, "gpt-5-mini", "Expected model name 'gpt-5-mini' to be present in copilot Arguments.");
        Assert.IsTrue(
            item.Arguments.Contains("--model \"gpt-5-mini\"") || item.Arguments.Contains("--model &quot;gpt-5-mini&quot;"),
            "Expected the model value to be quoted: --model \"gpt-5-mini\" or escaped in XML.");
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
}
