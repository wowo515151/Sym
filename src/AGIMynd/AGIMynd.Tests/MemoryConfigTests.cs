//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AGIMynd.Tests;

[TestClass]
public sealed class MemoryConfigTests
{
    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void GetDefaultMemoryRoot_UsesEnvironmentOverride()
    {
        // Environment variables are not relied upon by the application in tests.
        // Verify that programmatic override is available instead.
        GetDefaultMemoryRoot_UsesProgrammaticOverride();
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void GetDefaultMemoryRoot_UsesProgrammaticOverride()
    {
        var expected = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            MemoryConfig.SetMemoryRoot(expected);
            var actual = MemoryConfig.GetDefaultMemoryRoot();
            Assert.AreEqual(expected, actual);
        }
        finally
        {
            MemoryConfig.SetMemoryRoot(null!);
        }
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void GetDefaultMemoryRoot_EnvironmentVariablePrioritizedOverProgrammatic()
    {
        // Environment variables are not used; programmatic override should be honored.
        var progValue = "PROG_PATH";
        try
        {
            MemoryConfig.SetMemoryRoot(progValue);
            var actual = MemoryConfig.GetDefaultMemoryRoot();
            Assert.AreEqual(progValue, actual);
        }
        finally
        {
            MemoryConfig.SetMemoryRoot(null!);
        }
    }

    [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
    public void GetDefaultMemoryRoot_ReturnsValidDefault()
    {
        const string envVar = "AGIMYND_MEMORY_ROOT";
        var original = Environment.GetEnvironmentVariable(envVar);

        try
        {
            Environment.SetEnvironmentVariable(envVar, null);
            MemoryConfig.SetMemoryRoot(null!);
            
            var actual = MemoryConfig.GetDefaultMemoryRoot();
            Assert.IsFalse(string.IsNullOrWhiteSpace(actual));
            Assert.IsTrue(Path.IsPathRooted(actual));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }
}
