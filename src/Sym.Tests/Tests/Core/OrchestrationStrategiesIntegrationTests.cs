// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Core.Strategies;
using Sym.Operations;
using Sym.Atoms;
using System.Collections.Immutable;

namespace Sym.Test.Core
{
    [TestClass]
    public class OrchestrationStrategiesIntegrationTests
    {
        private static ImmutableList<Rule> generalSimplificationRules = ImmutableList.Create<Rule>
        (
             new Rule(new Add(new Wild("x"), new Number(0)), new Wild("x")),
             new Rule(new Multiply(new Wild("x"), new Number(1)), new Wild("x")),
             new Rule(new Multiply(new Wild("x"), new Number(0)), new Number(0)),
             new Rule(new Power(new Wild("x"), new Number(0)), new Number(1)),
             new Rule(new Power(new Wild("x"), new Number(1)), new Wild("x"))
        );

        [TestMethod]
        [Timeout(10000)]
        public void SimplifyThenSolveEquation_EndToEnd()
        {
            // Problem: (x + 0) + 2 = 5  -> simplify -> x + 2 = 5 -> solve -> x = 3
            var messyLeft = new Add(new Add(new Symbol("x"), new Number(0)), new Number(2));
            var problem = new Equality(messyLeft, new Number(5));

            // First simplify the equality expression
            var simplifyResult = SymSolver.Simplify(problem, generalSimplificationRules, 100, false);
            Assert.IsTrue(simplifyResult.IsSuccess, simplifyResult.Message);
            Assert.IsNotNull(simplifyResult.ResultExpression);

            // Then solve the simplified equation for x
            var solveResult = SymSolver.SolveEquation(simplifyResult.ResultExpression, new Symbol("x"), generalSimplificationRules, 100, false);
            Assert.IsTrue(solveResult.IsSuccess, solveResult.Message);
            Assert.AreEqual(new Equality(new Symbol("x"), new Number(3)), solveResult.ResultExpression);
        }
    }
}
