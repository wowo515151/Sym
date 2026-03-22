// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using SymSolvers.StableForms;

namespace SymSolvers.Tests.StableForms;

[TestClass]
public sealed class StableLossBenchmarkRunnerTests
{
    [TestMethod]
        [Timeout(10000)]
    public void BenchmarkRunner_ProducesReport()
    {
        var corpusPath = Path.Combine(Path.GetTempPath(), $"corpus_{Guid.NewGuid():N}.jsonl");
        var reportPath = Path.Combine(Path.GetTempPath(), $"report_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(corpusPath, new[]
            {
                "{\"expression\":\"log(1 + x)\"}",
                "{\"expression\":\"(exp(x)-1)/x\"}"
            });

            StableLossBenchmarkRunner.Run(corpusPath, reportPath);

            Assert.IsTrue(File.Exists(reportPath), "Report should be written.");
            var lines = File.ReadAllLines(reportPath);
            Assert.IsTrue(lines.Length >= 2, "Report should have entries for corpus items.");
            StringAssert.Contains(lines[0], "ok");
        }
        finally
        {
            if (File.Exists(corpusPath)) File.Delete(corpusPath);
            if (File.Exists(reportPath)) File.Delete(reportPath);
        }
    }
}
