using System;
using Xunit;

namespace SymRules.Tests
{
    public class RuleConverter_NullInput_Tests
    {
        [Fact]
        public void NullRule_ThrowsArgumentNullException()
        {
            SymRules.RuleDefinition r = null!;
            Assert.Throws<ArgumentNullException>(() => RuleConverter.ToCoreRule(r!));
        }
    }
}
