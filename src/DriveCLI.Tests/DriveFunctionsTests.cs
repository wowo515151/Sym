// Copyright Warren Harding 2026
using System.Diagnostics;

namespace DriveCLI.Tests;

[TestClass]
public sealed class DriveFunctionsTests
{
    public TestContext TestContext { get; set; } = null!;

    private string RootDir => Path.Combine(Path.GetTempPath(), "DriveCLI.Tests", TestContext.TestName);

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(RootDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(RootDir))
        {
            Directory.Delete(RootDir, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task WriteReadAppend_10s()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var filePath = Path.Combine(RootDir, "a.txt");
        await DriveCLI.DriveFunctions.WriteAllTextAsync(filePath, "hello", overwrite: true, cancellationToken: cts.Token);
        var read1 = await DriveCLI.DriveFunctions.ReadAllTextAsync(filePath, cts.Token);
        Assert.AreEqual("hello", read1);

        await DriveCLI.DriveFunctions.AppendAllTextAsync(filePath, " world", cancellationToken: cts.Token);
        var read2 = await DriveCLI.DriveFunctions.ReadAllTextAsync(filePath, cts.Token);
        Assert.AreEqual("hello world", read2);
    }

    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public async Task Write_NoOverwrite_Throws_10s()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var filePath = Path.Combine(RootDir, "b.txt");
        await DriveCLI.DriveFunctions.WriteAllTextAsync(filePath, "x", overwrite: true, cancellationToken: cts.Token);

        try
        {
            await DriveCLI.DriveFunctions.WriteAllTextAsync(filePath, "y", overwrite: false, cancellationToken: cts.Token);
            Assert.Fail("Expected IOException.");
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public void Exists_Delete_10s()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _ = cts;

        var filePath = Path.Combine(RootDir, "c.txt");
        Assert.IsFalse(DriveCLI.DriveFunctions.FileExists(filePath));

        File.WriteAllText(filePath, "z");
        Assert.IsTrue(DriveCLI.DriveFunctions.FileExists(filePath));

        DriveCLI.DriveFunctions.DeleteFileIfExists(filePath);
        Assert.IsFalse(DriveCLI.DriveFunctions.FileExists(filePath));
    }

    [TestMethod]
    [Timeout(10000, CooperativeCancellation = true)]
    public void CreateDir_List_10s()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _ = cts;

        var sub = Path.Combine(RootDir, "sub");
        DriveCLI.DriveFunctions.CreateDirectory(sub);
        Assert.IsTrue(DriveCLI.DriveFunctions.DirectoryExists(sub));

        var filePath = Path.Combine(sub, "d.txt");
        File.WriteAllText(filePath, "d");

        var entries = DriveCLI.DriveFunctions.EnumerateFileSystemEntries(sub).ToArray();
        CollectionAssert.Contains(entries, filePath);
    }
}
