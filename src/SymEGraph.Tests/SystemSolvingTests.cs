// Copyright Warren Harding 2026
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
    public class SystemSolvingTests
    {
        [TestMethod]
        [Timeout(30000)]
        public void TestSystemSolving_Linear_Propagation()
        {
            // x = y + 2
            // y = 3
            // Expected: x = 5
            
            var x = new Symbol("x");
            var y = new Symbol("y");
            
            var eq1 = new Equality(x, new Add(y, new Number(2))).Canonicalize();
            var eq2 = new Equality(y, new Number(3)).Canonicalize();
            
            var problem = new Vector(ImmutableList.Create<IExpression>(eq1, eq2));
            
            var packs = RulePackLibrary.GetRulePacks();
            var rules = packs.SelectMany(p => p.Rules).ToImmutableList();
            
            var context = new SolveContext(
                targetVariable: x,
                rules: rules,
                maxIterations: 30
            );
            
            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);
            
            Assert.IsTrue(result.IsSuccess, $"Solver failed: {result.Message}");
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("(x = 5)", result.ResultExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestSystemSolving_Substitution_Chain()
        {
            // a = b + 1
            // b = c * 2
            // c = 3
            // Solve for a: a = (3 * 2) + 1 = 7
            
            var a = new Symbol("a");
            var b = new Symbol("b");
            var c = new Symbol("c");
            
            var eq1 = new Equality(a, new Add(b, new Number(1))).Canonicalize();
            var eq2 = new Equality(b, new Multiply(c, new Number(2))).Canonicalize();
            var eq3 = new Equality(c, new Number(3)).Canonicalize();
            
            var problem = new Vector(ImmutableList.Create<IExpression>(eq1, eq2, eq3));
            
            var packs = RulePackLibrary.GetRulePacks();
            var rules = packs.SelectMany(p => p.Rules).ToImmutableList();
            
            var context = new SolveContext(
                targetVariable: a,
                rules: rules,
                maxIterations: 20
            );
            
            var strategy = new EGraphSolverStrategy();
            var result = strategy.Solve(problem, context);
            
            Assert.IsTrue(result.IsSuccess, $"Solver failed: {result.Message}");
            Assert.IsNotNull(result.ResultExpression);
            Assert.AreEqual("(a = 7)", result.ResultExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestSystemSolving_NonLinear_Simple()
        {
            // x^2 = 4
            // Expected: x = 2
            
            var x = new Symbol("x");
            var problem = Sym.CSharpIO.CSharpIO.ParseExpressions("x^2 = 4")[0];
            
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
            Assert.AreEqual("(x = 2)", result.ResultExpression.ToDisplayString());
        }
    }
}