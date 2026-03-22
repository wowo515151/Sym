// Copyright Warren Harding 2026
using System;
using Xunit;

namespace SymRules.Tests
{
    public class RulePipelineIntegrationTests
    {
        [Fact]
        public void RuleTextParser_Converter_CSharpIO_Pipeline()
        {
            var text = "AddZero: a + 0 -> a";
            Assert.True(RuleTextParser.TryGenerateCoreSource(text, out var core, out var diag));
            Assert.True(string.IsNullOrEmpty(diag));

            var parsed = RuleTextParser.Parse(text, out var pdiag);
            Assert.NotNull(parsed);

            var coreRule = RuleConverter.ToCoreRule(parsed!);
            Assert.NotNull(coreRule);
            var formatted = Sym.CSharpIO.CSharpIO.FormatRule(coreRule);
            Assert.Equal("Rule(Wild(\"a\"), Wild(\"a\"))", formatted);
        }
    }
}
