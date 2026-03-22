// Copyright Warren Harding 2026
using Sym.Core;
using SymRules.Calculus;
using Xunit;

namespace SymRules.Tests
{
    public class DerivativeRuleApiTests2
    {
        [Fact]
        public void Differentiate_ReturnsSymbolicExpression_ForSimplePower()
        {
            var result = DerivativeRule.Differentiate("x^2", "x");

            var expression = Assert.IsAssignableFrom<IExpression>(result);
            var text = expression.ToDisplayString();
            Assert.Contains("x", text);
        }
    }
}
