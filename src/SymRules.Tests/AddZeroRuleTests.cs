using System;
using System.IO;
using Xunit;
using SymRules;

namespace SymRules.Tests
{
    public class AddZeroRuleTests
    {
        [Fact]
        public void AddZeroRuleLoadsAndParses()
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[] {
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "SymRules", "Algebraic"),
                Path.Combine(baseDir, "..", "..", "..", "src", "SymRules", "Algebraic"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "SymRules", "Algebraic")
            };
            string path = null;
            foreach (var c in candidates)
            {
                var p = Path.GetFullPath(c);
                if (Directory.Exists(p)) { path = p; break; }
            }
            Assert.True(!string.IsNullOrEmpty(path), $"Rules folder not found: {Path.GetFullPath(candidates[0])}");
            var file = Path.Combine(path, "add_zero.rule");
            Assert.True(File.Exists(file), "add_zero.rule missing");
            var text = File.ReadAllText(file).Trim();
            var r = RuleTextParser.Parse(text, out var diag);
            Assert.NotNull(r);
            Assert.True(string.IsNullOrEmpty(diag), diag);
            Assert.False(string.IsNullOrWhiteSpace(r.CoreSource));
        }
    }
}
