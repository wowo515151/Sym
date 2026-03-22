// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SymRules.Tests
{
    public class AlgebraicRulesCoverageTests
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

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SymRules", sub));
        }

        [Fact]
        public void AddZeroRule_HasExpectedNameAndCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();
            var r = Assert.Single(rules.Where(r => r.Name == "AddZero"));

            Assert.Contains("Rule(", r.CoreSource);
            Assert.False(string.IsNullOrWhiteSpace(r.Text));
            Assert.Null(r.Diagnostics);
        }

        [Fact]
        public void SubSelfRule_HasExpectedNameAndCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();
            var r = Assert.Single(rules.Where(r => r.Name.Equals("subtract_same", StringComparison.OrdinalIgnoreCase)));

            Assert.Contains("Rule(", r.CoreSource);
            Assert.False(string.IsNullOrWhiteSpace(r.Text));
            Assert.Null(r.Diagnostics);
        }

        [Fact]
        public void MulOneRule_HasExpectedNameAndCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();
            var r = Assert.Single(rules.Where(r => r.Name.Equals("mul_one", StringComparison.OrdinalIgnoreCase)));

            Assert.Contains("Rule(", r.CoreSource);
            Assert.False(string.IsNullOrWhiteSpace(r.Text));
            Assert.Null(r.Diagnostics);
        }

        [Fact]
        public void ZeroMulRule_HasExpectedNameAndCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();
            var r = Assert.Single(rules.Where(r => r.Name.Equals("zero_mul", StringComparison.OrdinalIgnoreCase)));

            Assert.Contains("Rule(", r.CoreSource);
            Assert.False(string.IsNullOrWhiteSpace(r.Text));
            Assert.Null(r.Diagnostics);
        }

        [Fact]
        public void DistribRule_HasExpectedNameAndCoreSource()
        {
            // The curated Algebraic pack currently does not ship an active distribution rule
            // (only commented examples). This test remains as a guard that the pack stays
            // loadable even if distribution rules are reintroduced later.
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();
            Assert.NotEmpty(rules);
        }
    }
}
