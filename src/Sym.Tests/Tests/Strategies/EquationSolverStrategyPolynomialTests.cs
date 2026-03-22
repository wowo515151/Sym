using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Core.Strategies;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;
using System.Linq;

namespace SymTest.Strategies
{
    [TestClass]
    public class EquationSolverStrategyPolynomialTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Solve_Cubic_FindsRationalRoots()
        {
            var x = new Symbol("x");
            // x^3 - 6x^2 + 11x - 6 = (x-1)(x-2)(x-3)
            // expr = x^3 + -6x^2 + 11x + -6
            var expr = new Add(
                new Power(x, new Number(3)),
                new Multiply(new Number(-6), new Power(x, new Number(2))),
                new Multiply(new Number(11), x),
                new Number(-6)
            ).Canonicalize();
            
            var equation = new Equality(expr, new Number(0));
            
            var solver = new EquationSolverStrategy();
            var context = new SolveContext(targetVariable: x);
            
            var result = solver.Solve(equation, context);
            
            Assert.IsTrue(result.IsSuccess, $"Failed to solve: {result.Message}");
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Vector));
            var vector = (Vector)result.ResultExpression;
            
            // Expected roots: 1, 2, 3
            var resultStrings = vector.Arguments.Select(a => a.ToDisplayString()).ToList();
            CollectionAssert.Contains(resultStrings, "(x = 1)");
            CollectionAssert.Contains(resultStrings, "(x = 2)");
            CollectionAssert.Contains(resultStrings, "(x = 3)");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_Quadratic_SymbolicDisc_Works()
        {
            var x = new Symbol("x");
            // x^2 - 2 = 0 => x = +/- sqrt(2)
            var equation = new Equality(new Add(new Power(x, new Number(2)), new Number(-2)), new Number(0));
            
            var solver = new EquationSolverStrategy();
            var context = new SolveContext(targetVariable: x);
            
            var result = solver.Solve(equation, context);
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Vector));
            var vector = (Vector)result.ResultExpression;
            
            var resultStrings = vector.Arguments.Select(a => a.ToDisplayString()).ToList();
            // 2^0.5 is Sqrt(2)
            CollectionAssert.Contains(resultStrings, "(x = Pow(2, 0.5))");
            CollectionAssert.Contains(resultStrings, "(x = (-1 * Pow(2, 0.5)))");
        }
    }
}
