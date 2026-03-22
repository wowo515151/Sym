// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using Sym.Algebra;
using Sym.Core.Strategies;
using System.Collections.Immutable;
using System.Collections.Generic;

namespace SymTest.Tests.Algebra
{
    [TestClass]
    public class AlgebraicRuleTests
    {
        private ISolverStrategy? _strategy;
        private SolveContext? _context;

        [TestInitialize]
        public void Setup()
        {
            _strategy = new FullSimplificationStrategy();
            // Use the actual production rules from the library
            _context = new SolveContext(null, AlgebraicSimplificationRules.SimplificationRules, 100, false, null);
        }

        private IExpression Simplify(IExpression expression)
        {
            Assert.IsNotNull(_strategy);
            Assert.IsNotNull(_context);
            return Simplify(expression, additionalData: null);
        }

        private IExpression Simplify(IExpression expression, IDictionary<string, object>? additionalData)
        {
            Assert.IsNotNull(_strategy);
            Assert.IsNotNull(_context);
            var context = additionalData is null ? _context : _context.WithAdditionalData(additionalData);
            var result = _strategy.Solve(expression, context);
            Assert.IsTrue(result.IsSuccess, $"Simplification failed: {result.Message}");
            Assert.IsNotNull(result.ResultExpression);
            return result.ResultExpression;
        }

        [TestMethod]
        [Timeout(10000)]
        public void AdditiveIdentity_RemovesZero()
        {
            // x + 0 -> x
            var x = new Symbol("x");
            var expr = new Add(x, new Number(0));
            var result = Simplify(expr);
            Assert.AreEqual(x, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void SubtractionIdentity_RemovesZero()
        {
            // x - 0 -> x
            var x = new Symbol("x");
            var expr = new Subtract(x, new Number(0));
            var result = Simplify(expr);
            Assert.AreEqual(x, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void SubtractionOfSelf_ReturnsZero()
        {
            // x - x -> 0
            var x = new Symbol("x");
            var expr = new Subtract(x, x);
            var result = Simplify(expr);
            Assert.AreEqual(new Number(0), result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void SubtractionFromZero_ReturnsNegative()
        {
            // 0 - x -> -1 * x
            var x = new Symbol("x");
            var expr = new Subtract(new Number(0), x);
            var result = Simplify(expr);

            // Expect -1 * x
            var expected = new Multiply(new Number(-1), x);
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void DoubleNegative_ReturnsPositive()
        {
            // -(-x) -> x
            // Represented as -1 * (-1 * x)
            var x = new Symbol("x");
            var inner = new Multiply(new Number(-1), x);
            var expr = new Multiply(new Number(-1), inner);
            var result = Simplify(expr);
            Assert.AreEqual(x, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void MultiplicativeIdentity_RemovesOne()
        {
            // x * 1 -> x
            var x = new Symbol("x");
            var expr = new Multiply(x, new Number(1));
            var result = Simplify(expr);
            Assert.AreEqual(x, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void ZeroMultiplication_ReturnsZero()
        {
            // x * 0 -> 0
            var x = new Symbol("x");
            var expr = new Multiply(x, new Number(0));
            var result = Simplify(expr);
            Assert.AreEqual(new Number(0), result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void DivisionByOne_ReturnsNumerator()
        {
            // x / 1 -> x
            var x = new Symbol("x");
            var expr = new Divide(x, new Number(1));
            var result = Simplify(expr);
            Assert.AreEqual(x, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void DivisionBySelf_ReturnsOne()
        {
            // x / x -> 1
            var x = new Symbol("x");
            var expr = new Divide(x, x);
            var result = Simplify(expr);
            Assert.AreEqual(new Number(1), result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void ZeroDividedByX_ReturnsZero()
        {
            // 0 / x -> 0
            var x = new Symbol("x");
            var expr = new Divide(new Number(0), x);
            var result = Simplify(expr);
            Assert.AreEqual(new Number(0), result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void DivisionByNegativeOne_ReturnsNegative()
        {
            // x / -1 -> -x
            var x = new Symbol("x");
            var expr = new Divide(x, new Number(-1));
            var result = Simplify(expr);
            var expected = new Multiply(new Number(-1), x);
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void ReciprocalOfReciprocal_ReturnsOriginal()
        {
            // 1 / (1 / x) -> x
            var x = new Symbol("x");
            var inner = new Divide(new Number(1), x);
            var expr = new Divide(new Number(1), inner);
            var result = Simplify(expr);
            Assert.AreEqual(x, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void PowerByOne_ReturnsBase()
        {
            // x ^ 1 -> x
            var x = new Symbol("x");
            var expr = new Power(x, new Number(1));
            var result = Simplify(expr);
            Assert.AreEqual(x, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void PowerByZero_ReturnsOne()
        {
            // x ^ 0 -> 1
            var x = new Symbol("x");
            var expr = new Power(x, new Number(0));
            var result = Simplify(expr);
            Assert.AreEqual(new Number(1), result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void PowerOfOne_ReturnsOne()
        {
            // 1 ^ x -> 1
            var x = new Symbol("x");
            var expr = new Power(new Number(1), x);
            var result = Simplify(expr);
            Assert.AreEqual(new Number(1), result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void NegativeExponent_ReturnsReciprocal()
        {
            // x ^ -n -> 1 / x^n
            var x = new Symbol("x");
            var n = new Symbol("n");
            var negN = new Multiply(new Number(-1), n);
            var expr = new Power(x, negN);
            var result = Simplify(expr);

            var expected = new Divide(new Number(1), new Power(x, n));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void ProductWithPower_AddsExponents()
        {
            // x * x^n -> x^(1+n)
            var x = new Symbol("x");
            var n = new Symbol("n");
            var expr = new Multiply(x, new Power(x, n));
            var result = Simplify(expr, new Dictionary<string, object> { { "AssumeInteger", new[] { "n" } } });

            var expected = new Power(x, new Add(new Number(1), n));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void ProductWithPower_AddsExponents_SymmetricOrder()
        {
            // x^n * x -> x^(n+1)
            var x = new Symbol("x");
            var n = new Symbol("n");
            var expr = new Multiply(new Power(x, n), x);
            var result = Simplify(expr, new Dictionary<string, object> { { "AssumeInteger", new[] { "n" } } });

            var expected = new Power(x, new Add(n, new Number(1)));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void ProductOfPowers_AddsExponents()
        {
            // x^n * x^m -> x^(n+m)
            var x = new Symbol("x");
            var n = new Symbol("n");
            var m = new Symbol("m");
            var expr = new Multiply(new Power(x, n), new Power(x, m));
            var result = Simplify(expr, new Dictionary<string, object> { { "AssumeInteger", new[] { "n", "m" } } });

            var expected = new Power(x, new Add(n, m));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void PowerOfPower_MultipliesExponents()
        {
            // (x^n)^m -> x^(n*m)
            var x = new Symbol("x");
            var n = new Symbol("n");
            var m = new Symbol("m");
            var expr = new Power(new Power(x, n), m);
            var result = Simplify(expr, new Dictionary<string, object> { { "AssumeInteger", new[] { "m" } } });

            var expected = new Power(x, new Multiply(n, m));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void PowerOverProduct_Distributes()
        {
            // (x * y)^n -> x^n * y^n
            var x = new Symbol("x");
            var y = new Symbol("y");
            var n = new Symbol("n");
            var expr = new Power(new Multiply(x, y), n);
            var result = Simplify(expr, new Dictionary<string, object> { { "AssumeInteger", new[] { "n" } } });

            var expected = new Multiply(new Power(x, n), new Power(y, n));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void PowerOverQuotient_Distributes()
        {
            // (x / y)^n -> x^n / y^n
            var x = new Symbol("x");
            var y = new Symbol("y");
            var n = new Symbol("n");
            var expr = new Power(new Divide(x, y), n);
            var result = Simplify(expr, new Dictionary<string, object> { { "AssumeInteger", new[] { "n" } } });

            var expected = new Divide(new Power(x, n), new Power(y, n));
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [Timeout(10000)]
        public void CombineLikeTerms_AddsCoefficients()
        {
            // 2*x + 3*x -> 5*x
            var x = new Symbol("x");
            var term1 = new Multiply(new Number(2), x);
            var term2 = new Multiply(new Number(3), x);
            var expr = new Add(term1, term2);
            var result = Simplify(expr);

            var expected = new Multiply(new Number(5), x);
            Assert.AreEqual(expected, result);
        }
    }
}


