using System;
using System.IO;
using Xunit;
using SymRules;

namespace SymRules.Tests
{
    public class RuleParsingIntegrationTests
    {
        [Fact]
        public void AtLeastOneRuleParsesFromRepositoryRulesFolder()
        {
            // AppContext.BaseDirectory is typically: <repo>\src\SymRules.Tests\bin\<cfg>\<tfm>\
            // Going up 4 levels lands at <repo>\src, so don't append an extra "src".
            var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SymRules", "Rules"));
            var rules = RuleTextParser.LoadFromDirectory(folder, out var diagnostics);
            Assert.True(rules.Length > 0, $"Expected at least one parsed rule; diagnostics: {string.Join(" | ", diagnostics)}");
        }
    }
}
