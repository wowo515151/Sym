// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SymRules.Tests
{
    public class CosRuleTests
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
        public void LoadCosRule_ParsesAndConverts()
        {
            var root = FindRepoRoot();
            var folder = Path.Combine(root, "SymRules", "Trigonometry");
            var rules = SymRules.RuleTextParser.LoadFromDirectory(folder, out var diags);
            Assert.NotNull(rules);
            Assert.Empty(diags);
            Assert.Contains(rules, r => r.Text.Contains("cos(0)"));
            var r = rules.First(rr => rr.Text.Contains("cos(0)"));
            var coreRule = SymRules.RuleConverter.ToCoreRule(r);
            Assert.NotNull(coreRule);
        }
    }
}
