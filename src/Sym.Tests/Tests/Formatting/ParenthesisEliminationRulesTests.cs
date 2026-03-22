using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Operations;
using Sym.Formatting;
using System.Collections.Immutable;

namespace Sym.Test.Formatting
{
    [TestClass]
    public class ParenthesisEliminationRulesTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Format_SimpleAddition_NoParentheses()
        {
            var expr = new Add(new Symbol("x"), new Symbol("y"));
            var result = ParenthesisEliminationRules.Format(expr);
            Assert.AreEqual("x + y", result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Format_AddAndMultiply_CorrectParentheses()
        {
            // (x + y) * z => x * z + y * z in canonical form due to distribution
            var expr = new Multiply(new Add(new Symbol("x"), new Symbol("y")), new Symbol("z"));
            var result = ParenthesisEliminationRules.Format(expr);
            Assert.AreEqual("x * z + y * z", result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Format_MultiplyAndAdd_NoParenthesesForMultiply()
        {
            // x * y + z => z + x * y in canonical form
            var expr = new Add(new Multiply(new Symbol("x"), new Symbol("y")), new Symbol("z"));
            var result = ParenthesisEliminationRules.Format(expr);
            Assert.AreEqual("z + x * y", result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Format_SubtractionPattern_DisplaysAsSubtraction()
        {
            // x + (-1 * y) => x - y
            var expr = new Add(new Symbol("x"), new Multiply(new Number(-1m), new Symbol("y")));
            var result = ParenthesisEliminationRules.Format(expr);
            Assert.AreEqual("x - y", result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Format_DivisionPattern_DisplaysAsDivision()
        {
            // x * y^-1 => x / y
            var expr = new Multiply(new Symbol("x"), new Power(new Symbol("y"), new Number(-1m)));
            var result = ParenthesisEliminationRules.Format(expr);
            Assert.AreEqual("x / y", result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Format_Power_CorrectPrecedence()
        {
            // (x + y)**z
            var expr = new Power(new Add(new Symbol("x"), new Symbol("y")), new Symbol("z"));
            var result = ParenthesisEliminationRules.Format(expr);
            Assert.AreEqual("(x + y) ** z", result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Format_Equation_NoParenthesesAroundEquality()
        {
            // x + y = z
            var expr = new Equality(new Add(new Symbol("x"), new Symbol("y")), new Symbol("z"));
            var result = ParenthesisEliminationRules.Format(expr);
            Assert.AreEqual("x + y = z", result);
        }
    }
}
