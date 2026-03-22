// Copyright Warren Harding 2026
using System;
using System.IO;
using Xunit;

namespace SymRules.Tests
{
    public class RuleFiles_Loaded_Tests
    {
        [Fact]
        public void LoadRulesFromRulesFolder_ParsesAndConverts()
        {
            var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SymRules", "Rules"));
            var rules = SymRules.RuleTextParser.LoadFromDirectory(folder, out var diags);
            Assert.NotNull(rules);
            Assert.Empty(diags);
            Assert.Contains(rules, r => r.Text.Contains("a + 0"));
            var coreRule = SymRules.RuleConverter.ToCoreRule(rules[0]);
            Assert.NotNull(coreRule);
            var formatted = Sym.CSharpIO.CSharpIO.FormatRule(coreRule);
            Assert.Equal("Rule(Wild(\"a\"), Wild(\"a\"))", formatted);
        }
    }
}
