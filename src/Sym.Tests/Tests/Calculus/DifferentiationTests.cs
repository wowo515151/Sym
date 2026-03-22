using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using Sym.Calculus;
using Sym.Core.Rewriters;
using System.Collections.Immutable;
using System.Linq;

namespace SymTest.Calculus
{
    [TestClass]
    public class DifferentiationTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Differentiate_SumOfThreeTerms_Works()
        {
            var x = new Symbol("x");
            var y = new Symbol("y");
            var z = new Symbol("z");
            
            // d/dx (x + y + z)
            var expr = new Derivative(
                new Add(x, y, z).Canonicalize(),
                x
            ).Canonicalize();
            
            var result = Rewriter.RewriteFully(expr, CalculusRules.DifferentiationRules);
            
            // Expected result: 1 (since d/dx x = 1, d/dx y = 0, d/dx z = 0)
            Assert.AreEqual("1", result.RewrittenExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Differentiate_ProductOfThreeTerms_Works()
        {
            var x = new Symbol("x");
            var y = new Symbol("y");
            var z = new Symbol("z");
            
            // d/dx (x * y * z)
            var expr = new Derivative(
                new Multiply(x, y, z).Canonicalize(),
                x
            ).Canonicalize();
            
            var result = Rewriter.RewriteFully(expr, CalculusRules.DifferentiationRules);
            
            // Expected result: y * z
            Assert.AreEqual("(y * z)", result.RewrittenExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Differentiate_HyperbolicFunctions_Works()
        {
            var x = new Symbol("x");
            
            // sinh(x)' = cosh(x)
            var dsinh = new Derivative(new Function("sinh", x), x).Canonicalize();
            Assert.AreEqual("cosh(x)", Rewriter.RewriteFully(dsinh, CalculusRules.DifferentiationRules).RewrittenExpression.ToDisplayString());

            // cosh(x)' = sinh(x)
            var dcosh = new Derivative(new Function("cosh", x), x).Canonicalize();
            Assert.AreEqual("sinh(x)", Rewriter.RewriteFully(dcosh, CalculusRules.DifferentiationRules).RewrittenExpression.ToDisplayString());

            // tanh(x)' = 1 / cosh(x)^2 = cosh(x)^-2
            var dtanh = new Derivative(new Function("tanh", x), x).Canonicalize();
            var result = Rewriter.RewriteFully(dtanh, CalculusRules.DifferentiationRules).RewrittenExpression;
            Assert.AreEqual("Pow(cosh(x), -2)", result.ToDisplayString());
        }
    }
}
