using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SymRules.Tests
{
    public class RuleLoaderIntegrationTests
    {
        [Fact]
        public void LoadRules_UsingRuleLoader_ParsesAndConverts()
        {
            var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SymRules", "Rules"));
            var rules = SymRules.RuleLoader.LoadRules(folder).ToList();
            Assert.NotEmpty(rules);

            var r = rules.First();
            Assert.NotNull(r);

            var coreRule = SymRules.RuleConverter.ToCoreRule(r);
            Assert.NotNull(coreRule);

            var formatted = Sym.CSharpIO.CSharpIO.FormatRule(coreRule);
            Assert.Equal("Rule(Wild(\"a\"), Wild(\"a\"))", formatted);
        }
    }
}
