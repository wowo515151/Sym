// Copyright Warren Harding 2026
using Xunit;
using SymRules.Inequality;

namespace SymRules.Tests
{
    public class InequalityRuleTests
    {
        [Fact]
        public void IsLess_ReturnsTrue_WhenALessThanB()
        {
            Assert.True(InequalityRule.IsLess(1, 2));
        }
    }
}
