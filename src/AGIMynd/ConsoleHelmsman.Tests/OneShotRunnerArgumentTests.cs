//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ConsoleHelmsman.Tests;

[TestClass]
public sealed class OneShotRunnerArgumentTests
{
    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void BuildOneShotArguments_ReplacesPromptToken()
    {
        var definition = new ConsoleAppBase { Arguments = "-p {prompt}" };

        var args = OneShotRunner.BuildOneShotArguments(definition, "Hello world", out var usesPromptArgument);

        Assert.IsTrue(usesPromptArgument);
        Assert.AreEqual("-p \"Hello world\"", args);
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void BuildOneShotArguments_EscapesEmbeddedQuotes()
    {
        var definition = new ConsoleAppBase { Arguments = "--prompt={prompt}" };

        var args = OneShotRunner.BuildOneShotArguments(definition, "say \"hi\"", out var usesPromptArgument);

        Assert.IsTrue(usesPromptArgument);
        Assert.AreEqual("--prompt=\"say \\\"hi\\\"\"", args);
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void BuildOneShotArguments_KeepsArgumentsWhenNoPromptToken()
    {
        var definition = new ConsoleAppBase { Arguments = "--help" };

        var args = OneShotRunner.BuildOneShotArguments(definition, "ignored", out var usesPromptArgument);

        Assert.IsFalse(usesPromptArgument);
        Assert.AreEqual("--help", args);
    }
}
