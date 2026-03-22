// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sym.CSharpIO;
using Sym.Core;
using SymSolvers.Stability;

namespace SymSolvers.StableForms;

/// <summary>
/// Lightweight benchmark harness for stable-loss synthesis. Reads JSONL with { "expression": "..."} and writes a text report.
/// </summary>
public static class StableLossBenchmarkRunner
{
    public static void Run(string corpusPath, string reportPath)
    {
        if (!File.Exists(corpusPath))
        {
            throw new FileNotFoundException("Corpus file not found.", corpusPath);
        }

        var strategy = new StableLossSynthesisStrategy();
        var report = new List<string>();

        foreach (var line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            BenchmarkItem? item = null;
            try
            {
                item = JsonSerializer.Deserialize<BenchmarkItem>(line);
            }
            catch
            {
                // skip malformed
            }
            if (item?.Expression is null) continue;

            var expr = CSharpIO.ParseExpressions(item.Expression)[0];
            var result = strategy.Solve(expr, new SolveContext());

            var summary = result.IsSuccess && result.ResultExpression is not null
                ? $"ok\t{item.Expression}\t{result.ResultExpression.ToDisplayString()}"
                : $"fail\t{item.Expression}\t{result.Message}";
            report.Add(summary);
        }

        File.WriteAllLines(reportPath, report);
    }

    private sealed class BenchmarkItem
    {
        [JsonPropertyName("expression")]
        public string? Expression { get; set; }
    }
}
