//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymSolvers;
using System.Collections.Generic;

namespace SymEGraph.Tests
{
    [TestClass]
    public class EGraphTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void TestBasicSimplification_Identity()
        {
            System.Console.WriteLine($"DEBUG: ALWAYS PRINT Rule assembly: {typeof(Sym.Core.Rule).Assembly.Location}");
            // Rule: x + 0 -> x
            var x = new Symbol("x");
            var zero = new Number(0);
            var pattern = new Add(new Wild("a"), zero);
            var replacement = new Wild("a");
            var rule = new Rule(pattern, replacement);
            System.Console.WriteLine($"DEBUG: ALWAYS PRINT Rule pattern: {rule.Pattern.ToDisplayString()} (Type: {rule.Pattern.GetType().Name})");

            var problem = new Add(x, zero); // x + 0

            var context = new SolveContext(
                targetVariable: null,
                rules: ImmutableList.Create(rule),
                maxIterations: 10,
                enableTracing: false
            );

            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);

            if (!result.IsSuccess)
            {
                System.Console.Error.WriteLine($"DEBUG: Solver failed with message: {result.Message}");
            }
            Assert.IsTrue(result.IsSuccess, $"Solver should succeed. Message: {result.Message}");
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("x", result.ResultExpression.ToDisplayString(), "Result should be simplified to x");
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestSystemSolving_Propagation_Simple()
        {
            // System: x = y + 2, y = 3
            // Rule: 3 + 2 -> 5 (Simulating constant folding/arithmetic)
            
            var y = new Symbol("y");
            var two = new Number(2);
            var three = new Number(3);
            var five = new Number(5);
            
            var rule = new Rule(
                new Add(three, two),
                five
            );

            var x = new Symbol("x");
            var eq1 = new Equality(x, new Add(y, two));
            var eq2 = new Equality(y, three);
            
            var problem = new Vector(ImmutableList.Create<IExpression>(eq1, eq2));

            var context = new SolveContext(
                targetVariable: x,
                rules: ImmutableList.Create(rule),
                maxIterations: 10,
                enableTracing: false
            );

            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess);
            var resultEq = result.ResultExpression as Equality;
            Assert.IsNotNull(resultEq);
            Assert.AreEqual("x", resultEq.LeftOperand.ToDisplayString());
            Assert.AreEqual("5", resultEq.RightOperand.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTransitivity_Chain()
        {
            // a = b, b = c => a = c
            var a = new Symbol("a");
            var b = new Symbol("b");
            var c = new Symbol("c");

            var eq1 = new Equality(a, b);
            var eq2 = new Equality(b, c);
            
            var problem = new Vector(ImmutableList.Create<IExpression>(eq1, eq2));

            var context = new SolveContext(
                targetVariable: a,
                rules: ImmutableList<Rule>.Empty, 
                maxIterations: 10,
                enableTracing: false
            );

            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess);
            
            // The result equation should match 'a' to 'c' (or b, if c is not considered better).
            var resultEq = result.ResultExpression as Equality;
            Assert.IsNotNull(resultEq);
            var rhs = resultEq.RightOperand.ToDisplayString();
            Assert.IsTrue(rhs == "c" || rhs == "b");
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestComplexSystem_CycleDetection_Or_Resolution()
        {
            // x = y, y = x. Should not crash.
            var x = new Symbol("x");
            var y = new Symbol("y");
            
            var eq1 = new Equality(x, y);
            var eq2 = new Equality(y, x);
            
            var problem = new Vector(ImmutableList.Create<IExpression>(eq1, eq2));

            var context = new SolveContext(
                targetVariable: x,
                rules: ImmutableList<Rule>.Empty,
                maxIterations: 5,
                enableTracing: false
            );

            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);
            
            Assert.IsTrue(result.IsSuccess);
            var resultEq = result.ResultExpression as Equality;
            Assert.IsNotNull(resultEq);
            var rhs = resultEq.RightOperand.ToDisplayString();
             Assert.IsTrue(rhs == "y" || rhs == "x");
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestConstantFolding_ExplicitRule()
        {
            // Test that EGraph can apply a rule that computes a value
            // 2 + 2 -> 4
            var two = new Number(2);
            var four = new Number(4);
            var rule = new Rule(new Add(two, two), four);
            
            var problem = new Add(two, two);
             var context = new SolveContext(
                targetVariable: null,
                rules: ImmutableList.Create(rule),
                maxIterations: 5,
                enableTracing: false
            );
            
            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("4", result.ResultExpression.ToDisplayString());
        }
    }
}
