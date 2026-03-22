// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Core.Strategies;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;

namespace SymTest.Strategies
{
    [TestClass]
    public class EquationSolverStrategyInverseTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Solve_Asin_IsolatesTarget()
        {
            var x = new Symbol("x");
            var asinX = new Function("asin", ImmutableList.Create<IExpression>(x));
            var one = new Number(1m);
            var equation = new Equality(asinX, one);
            
            var solver = new EquationSolverStrategy();
            var context = new SolveContext(targetVariable: x, maxIterations: 10);
            
            var result = solver.Solve(equation, context);
            
            Assert.IsTrue(result.IsSuccess, $"Failed to solve: {result.Message}");
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Equality));
            var solved = (Equality)result.ResultExpression;
            Assert.AreEqual("x", solved.LeftOperand.ToDisplayString());
            Assert.AreEqual("sin(1)", solved.RightOperand.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_Acos_IsolatesTarget()
        {
            var x = new Symbol("x");
            var acosX = new Function("acos", ImmutableList.Create<IExpression>(x));
            var zero = new Number(0m);
            var equation = new Equality(acosX, zero);
            
            var solver = new EquationSolverStrategy();
            var context = new SolveContext(targetVariable: x, maxIterations: 10);
            
            var result = solver.Solve(equation, context);
            
            Assert.IsTrue(result.IsSuccess, $"Failed to solve: {result.Message}");
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Equality));
            if (result.ResultExpression is Equality solved)
            {
                Assert.AreEqual("x", solved.LeftOperand.ToDisplayString());
                Assert.AreEqual("cos(0)", solved.RightOperand.ToDisplayString());
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void Solve_Atan_IsolatesTarget()
        {
            var x = new Symbol("x");
            var atanX = new Function("atan", ImmutableList.Create<IExpression>(x));
            var zero = new Number(0m);
            var equation = new Equality(atanX, zero);
            
            var solver = new EquationSolverStrategy();
            var context = new SolveContext(targetVariable: x, maxIterations: 10);
            
            var result = solver.Solve(equation, context);
            
            Assert.IsTrue(result.IsSuccess, $"Failed to solve: {result.Message}");
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Equality));
            if (result.ResultExpression is Equality solved)
            {
                Assert.AreEqual("x", solved.LeftOperand.ToDisplayString());
                Assert.AreEqual("tan(0)", solved.RightOperand.ToDisplayString());
            }
        }
    }
}
