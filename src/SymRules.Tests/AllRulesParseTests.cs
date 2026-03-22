// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Xunit;
using SymRules;

namespace SymRules.Tests
{
    public class AllRulesParseTests
    {
        [Fact]
        public void AllNonBadRulesParseSuccessfully()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SymRules");
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.rule", SearchOption.AllDirectories) : Array.Empty<string>();
            var failures = new System.Collections.Generic.List<string>();
            foreach (var f in files)
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}Bad{Path.DirectorySeparatorChar}")) continue;
                var text = File.ReadAllText(f).Trim();
                var r = RuleTextParser.Parse(text, out var diag);
                if (r == null || !string.IsNullOrEmpty(diag))
                {
                    failures.Add($"{f}: {diag}");
                }
            }

            Assert.Empty(failures);
        }
    }
}

