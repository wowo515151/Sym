using System;
using Xunit;

namespace SymRules.Tests
{
    public class RuleConverter_EmptyParse_Tests
    {
        [Fact]
        public void CoreSourceWithNoRules_ReturnsNull()
        {
            var r = new RuleDefinition { Name = "test", CoreSource = "// comment only" };
            var result = r.ToCoreRule();
            Assert.Null(result);
        }
    }
}
