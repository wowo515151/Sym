// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SymRules.Tests
{
    public class RuleTextParserTests
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
        public void AlgebraicRules_ParseToCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();
            Assert.NotEmpty(rules);
            Assert.Contains(rules, r => !string.IsNullOrEmpty(r.CoreSource));
        }

        [Fact]
        public void MalformedRule_ProducesDiagnostics()
        {
            var folder = FindFolder("Bad");
            var rules = RuleLoader.LoadRules(folder).ToList();
            Assert.NotEmpty(rules);
            Assert.Contains(rules, r => !string.IsNullOrEmpty(r.Diagnostics));
        }
    }
}

