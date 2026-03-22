// Copyright Warren Harding 2026
using Xunit;
using SymRules;

namespace SymRules.Tests
{
    public class TryGenerateCoreSourceTests
    {
        [Fact]
        public void TryGenerateCoreSource_StripsNameAndNormalizesArrow()
        {
            var text = "AddZero: a + 0 => a";
            Assert.True(RuleTextParser.TryGenerateCoreSource(text, out var core, out var diag));
            Assert.True(string.IsNullOrEmpty(diag));
            Assert.Equal("Rule(Wild(\"a\") + 0, Wild(\"a\"))", core);
        }
    }
}
