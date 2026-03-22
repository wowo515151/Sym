//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ConsoleHelmsman.Tests;

[TestClass]
public sealed class ConsoleLogTests
{
    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void GetTail_WrapsRingBuffer()
    {
        var log = new ConsoleLog(5);

        log.AppendInput("12345");
        Assert.AreEqual("12345", log.GetTail(5));

        log.AppendInput("678");
        Assert.AreEqual("45678", log.GetTail(5));
        Assert.AreEqual("78", log.GetTail(2));
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void AppendSystemMessage_AddsTrailingNewline()
    {
        var log = new ConsoleLog(20);

        log.AppendSystemMessage("hello");
        var tail = log.GetTail(20);

        Assert.IsTrue(tail.EndsWith(Environment.NewLine, StringComparison.Ordinal));
    }
}
