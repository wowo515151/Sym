using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SymRules.Tests
{
    public class AlgebraicRulesTests
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
        public void AddZeroRuleFile_LoadsAndGeneratesCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();

            var rule = Assert.Single(rules.Where(r => r.Name == "AddZero"));
            Assert.Contains("a + 0", rule.Text);
            Assert.Contains("Rule(", rule.CoreSource);
            Assert.Null(rule.Diagnostics);
        }

        [Fact]
        public void SubtractSameRuleFile_LoadsAndGeneratesCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();

            var rule = Assert.Single(rules.Where(r => r.Name.Equals("subtract_same", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("a - a", rule.Text);
            Assert.Contains("Rule(", rule.CoreSource);
            Assert.Null(rule.Diagnostics);
        }

        [Fact]
        public void MulOneRuleFile_LoadsAndGeneratesCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();

            var rule = Assert.Single(rules.Where(r => r.Name.Equals("mul_one", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("a*1", rule.Text.Replace(" ", string.Empty));
            Assert.Contains("Rule(", rule.CoreSource);
            Assert.Null(rule.Diagnostics);
        }

        [Fact]
        public void ZeroMulRuleFile_LoadsAndGeneratesCoreSource()
        {
            var folder = FindFolder("Algebraic");
            var rules = RuleLoader.LoadRules(folder).ToList();

            var rule = Assert.Single(rules.Where(r => r.Name.Equals("zero_mul", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("0 * a", rule.Text);
            Assert.Contains("Rule(", rule.CoreSource);
            Assert.Null(rule.Diagnostics);
        }
    }
}
