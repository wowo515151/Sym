// Copyright Warren Harding 2026
using System;
using Xunit;

namespace SymRules.Tests
{
    public class MinimalParseTest
    {
        [Fact]
        public void SimpleRule_ParsesToCoreSource()
        {
            var ok = SymRules.RuleTextParser.TryGenerateCoreSource("AddZero: a + 0 -> a", out var core, out var diag);
            Assert.True(ok, diag ?? "Parse failed");
            Assert.False(string.IsNullOrEmpty(core));
        }
    }
}
