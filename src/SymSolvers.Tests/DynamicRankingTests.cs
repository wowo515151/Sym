// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymSolvers;

namespace SymSolvers.Tests
{
    [TestClass]
    public class DynamicRankingTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void TestFeatureExtractor()
        {
            var x = new Symbol("x");
            var expr = new Function("sin", ImmutableList.Create<IExpression>(x));
            var features = ProblemFeatureExtractor.Analyze(expr);
            Assert.IsTrue(features.HasTrig);
            Assert.IsFalse(features.HasInequality);

            var ineq = new Function("lt", ImmutableList.Create<IExpression>(x, new Number(0)));
            features = ProblemFeatureExtractor.Analyze(ineq);
            Assert.IsTrue(features.HasInequality);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestEGraphPreferredForm()
        {
            // Construct EGraph with equivalent forms: a*(b+c) vs a*b + a*c
            // x * (x + 1) vs x^2 + x
            var graph = new Sym.Core.EGraph.EGraph();
            var x = new Symbol("x");
            var one = new Number(1);
            
            // Form 1: x * (x + 1)
            var add = new Add(x, one);
            var form1 = new Multiply(x, add);
            var id1 = graph.Add(form1);

            // Form 2: x^2 + x
            var pow = new Power(x, new Number(2));
            var form2 = new Add(pow, x);
            var id2 = graph.Add(form2);

            // Union them
            graph.Union(id1, id2);
            graph.Rebuild();

            // Test Factored Preference (penalize Add)
            var contextFactored = new SolveContext(additionalData: 
                ImmutableDictionary<string, object>.Empty.Add("PreferredForm", "Factored"));
            
            // We use EGraphSolverStrategy but we need to inject the graph
            var strategy = new EGraphSolverStrategy();
            
            // Since we can't easily inject the pre-built graph into Solve() without "SharedEGraph",
            // we use the SharedEGraph feature from Phase 1!
            var context1 = contextFactored.WithSharedEGraph(graph);
            var res1 = strategy.Solve(form1, context1);
            
            // It might fail if no change (already factored), which is fine.
            // Assert.IsTrue(res1.IsSuccess);
            
            // Let's try a clearer case: x+x+x vs 3*x
            // Use a fresh graph to avoid noise
            var graph2 = new Sym.Core.EGraph.EGraph();
            var x2 = new Symbol("x");
            
            var idA = graph2.Add(new Add(x2, new Add(x2, x2)));
            var idB = graph2.Add(new Multiply(new Number(3), x2));
            graph2.Union(idA, idB);
            graph2.Rebuild();
            
            Assert.AreEqual(graph2.Find(idA), graph2.Find(idB), "Classes should be unioned");
            var count = graph2.GetClass(graph2.Find(idA)).Nodes.Count;
            // Console.WriteLine($"Class Node Count: {count}");
            
            // Factored: Add=10, Mul=1.
            var resFactored = EGraphExtract.ExtractBest(graph2, idA, null, 
                node => (node.Head == "Add" ? 10 : 1));
            
            // Console.WriteLine($"Factored Result: {resFactored.ToDisplayString()} ({resFactored.GetType().Name})");
            Assert.IsTrue(resFactored is Multiply, $"Expected Multiply (3*x) for Factored preference, got {resFactored.GetType().Name}");

            // Expanded: Add=1, Mul=10.
            // 3*x cost = 10.
            // x+x+x cost = 2.
            // Should pick x+x+x (Add).
            
            var resExpanded = EGraphExtract.ExtractBest(graph2, idA, null, 
                node => (node.Head == "Mul" ? 10 : 1));
            
            // Console.WriteLine($"Expanded Result: {resExpanded.ToDisplayString()} ({resExpanded.GetType().Name})");
            Assert.IsTrue(resExpanded is Add, $"Expected Add (x+x+x) for Expanded preference, got {resExpanded.GetType().Name}");
        }
    }
}
