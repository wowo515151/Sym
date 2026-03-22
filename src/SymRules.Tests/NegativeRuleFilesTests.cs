// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Xunit;
using SymRules;

namespace SymRules.Tests
{
    public class NegativeRuleFilesTests
    {
        private static string FindRepoRoot()
        {
            var di = new DirectoryInfo(AppContext.BaseDirectory);
            while (di != null)
            {
                if (Directory.Exists(Path.Combine(di.FullName, "SymRules")) && Directory.Exists(Path.Combine(di.FullName, "SymRules.Tests")))
                {
                    return di.FullName;
                }

                if (Directory.Exists(Path.Combine(di.FullName, "src", "SymRules")) && Directory.Exists(Path.Combine(di.FullName, "src", "SymRules.Tests")))
                {
                    return Path.Combine(di.FullName, "src");
                }

                di = di.Parent;
            }

            throw new InvalidOperationException("Repo root not found");
        }

        [Fact]
        public void BadRuleFilesProduceDiagnostics()
        {
            var root = FindRepoRoot();
            var badDir = Path.Combine(root, "SymRules", "Bad");
            Assert.True(Directory.Exists(badDir), $"Expected bad rule folder at {badDir}");

            var rules = RuleLoader.LoadRules(badDir).ToArray();
            Assert.NotEmpty(rules);

            Assert.All(rules, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Text));
                Assert.True(!string.IsNullOrEmpty(r.Diagnostics), "Expected diagnostics for malformed rule file");
                Assert.True(string.IsNullOrEmpty(r.CoreSource), "Malformed rules should not have CoreSource");
            });
        }

        [Fact]
        public void BadRuleFilesDoNotBlockValidRules()
        {
            var root = FindRepoRoot();
            var symRulesRoot = Path.Combine(root, "SymRules");
            Assert.True(Directory.Exists(symRulesRoot), $"Expected SymRules root at {symRulesRoot}");

            var rules = RuleLoader.LoadRules(symRulesRoot).ToArray();
            Assert.Contains(rules, r => !string.IsNullOrEmpty(r.CoreSource) && string.IsNullOrEmpty(r.Diagnostics));
        }
    }
}
