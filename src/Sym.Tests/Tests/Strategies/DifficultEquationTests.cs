// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Core.Strategies;
using Sym.Operations;
using Sym.Algebra;
using Sym.Atoms;
using System.Collections.Immutable;

namespace Sym.Test.Strategies
{
    [TestClass]
    public class DifficultEquationTests
    {
        private EquationSolverStrategy _strategy = new();
        private ImmutableList<Rule> _rules = AlgebraicSimplificationRules.SimplificationRules;

        [TestMethod]
        [Timeout(10000)]
        public void Solve_EquationRequiringDistributionAndCombining()
        {
            // (x + 1) * (x + 2) = x**2 + 7
            // x**2 + 3x + 2 = x**2 + 7
            // 3x + 2 = 7
            // 3x = 5
            // x = 5/3
            var x = new Symbol("x");
            var lhs = new Multiply(new Add(x, new Number(1)), new Add(x, new Number(2)));
            var rhs = new Add(new Power(x, new Number(2)), new Number(7));
            var problem = new Equality(lhs, rhs);
            
            var context = new SolveContext(x, _rules, 100, false, null);
            var result = _strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess, result.Message);
            // Result should be x = 1.666... (5/3)
            // Depending on how it isolates, it might be a Number or a Divide.
            // 5/3 is approx 1.6666666666666666666666666667
            if (result.ResultExpression is Equality eq && eq.RightOperand is Number num)
            {
                Assert.AreEqual(5m / 3m, num.Value);
            }
            else
            {
                Assert.Fail($"Expected x = number, got {result.ResultExpression?.ToDisplayString()}");
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_LinearEquationWithDistributionAndCombining()
        {
            // 2 * (x + 3) + 3 * (x - 1) = 13
            // 2x + 6 + 3x - 3 = 13
            // 5x + 3 = 13
            // 5x = 10
            // x = 2
            var x = new Symbol("x");
            var term1 = new Multiply(new Number(2), new Add(x, new Number(3)));
            var term2 = new Multiply(new Number(3), new Add(x, new Number(-1)));
            var lhs = new Add(term1, term2);
            var rhs = new Number(13);
            var problem = new Equality(lhs, rhs);

            var context = new SolveContext(x, _rules, 100, false, null);
            var result = _strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual(new Equality(x, new Number(2)).ToDisplayString(), result.ResultExpression?.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_EquationWithFractions()
        {
            // 1 / (x + 1) = 2
            // 1 = 2 * (x + 1)
            // 1 = 2x + 2
            // -1 = 2x
            // x = -0.5
            var x = new Symbol("x");
            var lhs = new Power(new Add(x, new Number(1)), new Number(-1));
            var rhs = new Number(2);
            var problem = new Equality(lhs, rhs);

            var context = new SolveContext(x, _rules, 100, false, null);
            var result = _strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess, result.Message);
            var expected = new Equality(x, new Number(-0.5m));
            Assert.AreEqual(expected.ToDisplayString(), result.ResultExpression?.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_QuadraticEquationDirectly()
        {
            // x**2 - 5x + 6 = 0
            // Solutions: x=2, x=3
            var x = new Symbol("x");
            var poly = new Add(new Power(x, new Number(2)), new Multiply(new Number(-5), x), new Number(6));
            var problem = new Equality(poly, new Number(0));

            var context = new SolveContext(x, _rules, 100, false, null);
            var result = _strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess, result.Message);
            // Should return a Vector of solutions: [x=3, x=2] (ordered by Canonicalize)
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Vector));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_QuadraticPerfectSquare()
        {
            // (x - 2)**2 = 0 => x = 2
            var x = new Symbol("x");
            var problem = new Equality(new Power(new Subtract(x, new Number(2)), new Number(2)), new Number(0));

            var context = new SolveContext(x, _rules, 100, false, null);
            var result = _strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual(new Equality(x, new Number(2)).ToDisplayString(), result.ResultExpression?.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_QuadraticWithIrrationalRoots()
        {
            // x**2 - 2 = 0 => x = sqrt(2), x = -sqrt(2)
            var x = new Symbol("x");
            var problem = new Equality(new Subtract(new Power(x, new Number(2)), new Number(2)), new Number(0));

            var context = new SolveContext(x, _rules, 100, false, null);
            var result = _strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess, result.Message);
            var resDisplay = result.ResultExpression?.ToDisplayString() ?? "";
            Assert.IsTrue(resDisplay.Contains("Pow(2, 0.5)") || resDisplay.Contains("sqrt(2)"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_TrigonometricEquation()
        {
            // sin(x) = 1  => x = asin(1) => x = 1.570796...
            var x = new Symbol("x");
            var problem = new Equality(new Function("sin", x), new Number(1));

            var context = new SolveContext(x, _rules, 100, false, null);
            var result = _strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess, result.Message);
            // Result should be x = pi/2
            if (result.ResultExpression is Equality eq && eq.RightOperand is Number num)
            {
                Assert.AreEqual((decimal)System.Math.Asin(1.0), num.Value);
            }
            else
            {
                Assert.Fail($"Expected x = number, got {result.ResultExpression?.ToDisplayString()}");
            }
        }
    }
}
