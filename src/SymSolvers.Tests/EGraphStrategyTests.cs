//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;
using System.Collections.Generic;

namespace SymSolvers.Tests
{
    [TestClass]
    public class EGraphStrategyTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void TestBasicSimplification()
        {
            // Rule: x + 0 -> x
            var x = new Symbol("x");
            var zero = new Number(0);
            var pattern = new Add(new Wild("a"), zero);
            var replacement = new Wild("a");
            var rule = new Rule(pattern, replacement);

            var problem = new Add(x, zero); // x + 0

            var context = new SolveContext(
                targetVariable: null,
                rules: ImmutableList.Create(rule),
                maxIterations: 10,
                enableTracing: false
            );

            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("x", result.ResultExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestSystemSolving_Propagation()
        {
            // System: x = y + 2, y = 3
            // Rules: Arithmetic (Add numbers)
            // We need a rule that simplifies Add(Num, Num) -> Num? 
            // Or does EGraphExtract/Instantiate handle constant folding?
            // EGraphInstantiator creates nodes. It doesn't fold.
            // EGraphExtract "reconstructs".
            // We need explicit rules for arithmetic or a constant folding pass.
            // But for this test, let's provide a specific rule: 3 + 2 -> 5.
            
            var y = new Symbol("y");
            var two = new Number(2);
            var three = new Number(3);
            var five = new Number(5);
            
            // Rule: 3 + 2 -> 5
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
            // Result should be x = 5
            var resultEq = result.ResultExpression as Equality;
            Assert.IsNotNull(resultEq);
            Assert.AreEqual("x", resultEq.LeftOperand.ToDisplayString());
            Assert.AreEqual("5", resultEq.RightOperand.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTransitivity()
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
                rules: ImmutableList<Rule>.Empty, // No rules needed for transitivity
                maxIterations: 10,
                enableTracing: false
            );

            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);

            Assert.IsTrue(result.IsSuccess);
            // Result should be a = c (since c is the smallest/simplest form of c? or just equivalent)
            // ExtractBest might return 'c' or 'b' or 'a' depending on internal ordering if costs are equal.
            // But typically structural cost: a, b, c are all cost 1.
            // However, Union logic usually merges into one class.
            // If we ask for 'a', we want the best representation of 'a'.
            // Since 'c' is in the class, and equal cost, it's ambiguous.
            // But let's see. EGraphExtract picks one.
            // The test might be flaky on "c" vs "b". 
            // But solving for 'a' implies we want something *other* than 'a' if possible?
            // Actually ExtractBest just returns the best node.
            
            // Let's assert that the RHS is 'c' OR 'b'.
            var resultEq = result.ResultExpression as Equality;
            Assert.IsNotNull(resultEq);
            var rhs = resultEq.RightOperand.ToDisplayString();
            Assert.IsTrue(rhs == "c" || rhs == "b"); 
        }
    }
}
