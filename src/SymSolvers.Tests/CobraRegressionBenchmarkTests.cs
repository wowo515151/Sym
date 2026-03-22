// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using SymCobra.Regression;

namespace SymSolvers.Tests;

[TestClass]
public class CobraRegressionBenchmarkTests
{
    [TestMethod]
    public void Benchmark_RegressionEvaluation_Performance()
    {
        int rowCount = 100000;
        int featureCount = 10;
        string csvPath = Path.Combine(Path.GetTempPath(), $"cobra-bench-{Guid.NewGuid():N}.csv");
        
        var header = string.Join(",", Enumerable.Range(0, featureCount).Select(i => $"f{i}")) + ",target";
        var lines = new List<string> { header };
        var random = new Random(42);
        for (int i = 0; i < rowCount; i++)
        {
            var row = Enumerable.Range(0, featureCount).Select(_ => random.NextDouble()).ToList();
            double target = row[0] * row[1] + row[2] - row[3];
            lines.Add(string.Join(",", row) + "," + target);
        }
        File.WriteAllLines(csvPath, lines);

        try
        {
            var options = new CobraRegressionOptions(
                csvPath,
                "target",
                Enumerable.Range(0, featureCount).Select(i => $"f{i}").ToList(),
                0.01,
                500,
                "MSE"
            );

            var engine = new CobraRegressionEngine();
            
            var sw = Stopwatch.StartNew();
            var result = engine.SolveTabular(options, CancellationToken.None);
            sw.Stop();

            Console.WriteLine($"Regression took {sw.ElapsedMilliseconds}ms for {rowCount} rows and {options.MaxCandidates} candidates.");
            Assert.IsNotNull(result.BestExpression);
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }
}
