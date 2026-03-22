// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Xunit;
using SymRules;

namespace SymRules.Tests
{
    public class ValidateAllRulesTests
    {
        private static string FindFolder(string sub)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SymRules"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SymRules"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "SymRules")
            }.Select(Path.GetFullPath);

            foreach (var c in candidates)
            {
                var folder = Path.Combine(c, sub);
                if (Directory.Exists(folder)) return folder;
            }

            // Fallback to original relative path used elsewhere
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SymRules", sub));
        }

        [Fact]
        public void AllRules_ParseWithoutDiagnostics()
        {
            var algebraic = FindFolder("Algebraic");
            var root = Path.GetDirectoryName(algebraic) ?? algebraic;
            var subdirs = Directory.GetDirectories(root).Select(Path.GetFileName).Where(n => !string.Equals(n, "Bad", StringComparison.OrdinalIgnoreCase));

            var allRules = subdirs.SelectMany(sd => RuleLoader.LoadRules(Path.Combine(root, sd))).ToList();

            Assert.NotEmpty(allRules);
            var bad = allRules.Where(r => !string.IsNullOrEmpty(r.Diagnostics)).ToList();
            Assert.True(!bad.Any(), $"Found rules with diagnostics: {string.Join(", ", bad.Select(r => r.Name ?? "<unnamed>"))}");
        }
    }
}

