//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymSolvers;
using SymRules;

namespace SymEGraph.Tests
{
    [TestClass]
    public class EGraphCoverageTests
    {
        [TestMethod]
        [Timeout(30000)]
        public void TestRulePack_Algebraic_Simplification()
        {
            var packs = RulePackLibrary.GetRulePacks();
            var algebraicPack = packs.FirstOrDefault(p => p.Name == "AlgebraicStrategy");
            Assert.IsNotNull(algebraicPack);
            
            var rules = algebraicPack!.Rules;

            var x = new Symbol("x");
            // Do NOT canonicalize the problem, so it differs from the expected result 'x'.
            var problem = new Add(new Multiply(new Add(x, new Number(0)), new Number(1)), new Number(0));
            
            var context = new SolveContext(
                targetVariable: null,
                rules: rules,
                maxIterations: 10
            );
            
            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);
            
            Assert.IsTrue(result.IsSuccess, $"Solver failed: {result.Message}");
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("x", result.ResultExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestRulePack_Trigonometry_Identity()
        {
            var packs = RulePackLibrary.GetRulePacks();
            var trigPack = packs.FirstOrDefault(p => p.Name == "Trigonometry");
            var algPack = packs.FirstOrDefault(p => p.Name == "AlgebraicStrategy");
            
            Assert.IsNotNull(trigPack);

            var x = new Symbol("x");
            var sinx = new Function("sin", x);
            var cosx = new Function("cos", x);
            var problem = new Add(new Power(sinx, new Number(2)), new Power(cosx, new Number(2))).Canonicalize();
            
            var rules = trigPack.Rules.AddRange(algPack?.Rules ?? ImmutableList<Sym.Core.Rule>.Empty);
            
            var context = new SolveContext(
                targetVariable: null,
                rules: rules,
                maxIterations: 10
            );
            
            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);
            
            Assert.IsTrue(result.IsSuccess, $"Solver failed: {result.Message}");
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("1", result.ResultExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestRulePack_Inequality_Simplification()
        {
            var packs = RulePackLibrary.GetRulePacks();
            var ineqPack = packs.FirstOrDefault(p => p.Name == "Inequality");
            Assert.IsNotNull(ineqPack);

            // x < x -> false
            var x = new Symbol("x");
            var problem = new Function("lt", x, x);
            
            var context = new SolveContext(
                targetVariable: null,
                rules: ineqPack.Rules,
                maxIterations: 10
            );
            
            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);
            
            Assert.IsTrue(result.IsSuccess, $"Solver failed: {result.Message}");
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("false", result.ResultExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestEGraph_SystemOfEquations_Propagation()
        {
            // x = y + z
            // y = 2
            // z = 3
            // Solve for x: x = 5
            
            var x = new Symbol("x");
            var y = new Symbol("y");
            var z = new Symbol("z");
            
            var eq1 = new Equality(x, new Add(y, z)).Canonicalize();
            var eq2 = new Equality(y, new Number(2)).Canonicalize();
            var eq3 = new Equality(z, new Number(3)).Canonicalize();
            
            var problem = new Vector(ImmutableList.Create<IExpression>(eq1, eq2, eq3));
            
            var packs = RulePackLibrary.GetRulePacks();
            var rules = packs.SelectMany(p => p.Rules).ToImmutableList();
            
            var context = new SolveContext(
                targetVariable: x,
                rules: rules,
                maxIterations: 20
            );
            
            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);
            
            Assert.IsTrue(result.IsSuccess, $"Solver failed: {result.Message}");
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("(x = 5)", result.ResultExpression.ToDisplayString());
        }
    }
}