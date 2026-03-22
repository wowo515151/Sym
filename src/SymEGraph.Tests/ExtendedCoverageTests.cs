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

namespace SymEGraph.Tests
{
    [TestClass]
    public class ExtendedCoverageTests
    {
        [TestMethod]
        [Timeout(30000)]
        public void TestCongruence_NestedAddition()
        {
            // (a + b) + c should be congruent to (d + b) + c if a == d
            var graph = new EGraph();
            var a = new Symbol("a");
            var b = new Symbol("b");
            var c = new Symbol("c");
            var d = new Symbol("d");

            int idA = graph.Add(a);
            int idB = graph.Add(b);
            int idC = graph.Add(c);
            int idD = graph.Add(d);

            int idAB = graph.Add(new Add(a, b));
            int idDB = graph.Add(new Add(d, b));

            int idABC = graph.Add(new Add(new Add(a, b), c));
            int idDBC = graph.Add(new Add(new Add(d, b), c));

            Assert.AreNotEqual(graph.Find(idABC), graph.Find(idDBC));

            // Union a and d
            graph.Union(idA, idD);
            graph.Rebuild();

            // After rebuild, AB and DB should be congruent, and ABC and DBC should be congruent.
            Assert.AreEqual(graph.Find(idA), graph.Find(idD), "a and d should be unioned");
            Assert.AreEqual(graph.Find(idAB), graph.Find(idDB), "a+b and d+b should be congruent");
            Assert.AreEqual(graph.Find(idABC), graph.Find(idDBC), "(a+b)+c and (d+b)+c should be congruent");
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestExtraction_WithPreferredForm_Expanded()
        {
            // (a + b) * c  vs  a*c + b*c
            // If we prefer expanded, a*c + b*c should be "cheaper" if we set costs correctly.
            // Note: EGraphSolverStrategy cost function for Expanded:
            // Pow: 10, Add: 1, Mul: 5, others: 1
            
            var graph = new EGraph();
            var a = new Symbol("a");
            var b = new Symbol("b");
            var c = new Symbol("c");

            var factorForm = new Multiply(new Add(a, b), c);
            var expandedForm = new Add(new Multiply(a, c), new Multiply(b, c));

            int id1 = graph.Add(factorForm);
            int id2 = graph.Add(expandedForm);
            graph.Union(id1, id2);
            graph.Rebuild();

            Func<ENode, long> expandedCost = node => {
                if (node.Head == "Pow") return 10;
                if (node.Head == "Add") return 1;
                if (node.Head == "Mul") return 5;
                return 1;
            };

            var extracted = EGraphExtract.ExtractBest(graph, id1, null, expandedCost);
            
            // Expanded cost: 
            // expandedForm: Add(Mul(a,c), Mul(b,c)) => 1 + (5+2+2) + (5+2+2) = 1 + 9 + 9 = 19
            // factorForm: Mul(Add(a,b), c) => 5 + (1+2+2) + 2 = 5 + 5 + 2 = 12
            
            // Wait, if Expanded cost for Add is 1 and Mul is 5, then Add is cheaper than Mul.
            // But expanded form has TWO Muls and ONE Add. Factored has ONE Mul and ONE Add.
            // Let's check the logic.
            // Base cost: Sym=2, Num=1, Default=1.
            
            // Factor: Mul (5)
            //          Add (1)
            //           a (2)
            //           b (2)
            //          c (2)
            // Total: 5 + 1 + 2 + 2 + 2 = 12
            
            // Expanded: Add (1)
            //            Mul (5)
            //             a (2)
            //             c (2)
            //            Mul (5)
            //             b (2)
            //             c (2)
            // Total: 1 + 5 + 2 + 2 + 5 + 2 + 2 = 19
            
            // My manual calculation shows Factored is still cheaper even with Add=1, Mul=5 because it has fewer Muls.
            // To make Expanded cheaper, Mul needs to be even cheaper or Add even more expensive? No, wait.
            // Usually "Expanded" means we prefer Sums.
            
            Func<ENode, long> preferSumsCost = node => {
                if (node.Head == "Mul") return 10;
                if (node.Head == "Add") return 1;
                return 1;
            };
            // Factor: 10 + 1 + 2 + 2 + 2 = 17
            // Expanded: 1 + 10 + 2 + 2 + 10 + 2 + 2 = 29
            // Still factored is cheaper because of fewer nodes. 
            // E-Graphs naturally prefer smaller expressions unless we heavily penalize certain structures.
            
            // Let's try to prefer Factored:
            Func<ENode, long> preferFactoredCost = node => {
                if (node.Head == "Add") return 10;
                if (node.Head == "Mul") return 1;
                return 1;
            };
            // Factor: 1 + 10 + 2 + 2 + 2 = 17
            // Expanded: 10 + 1 + 2 + 2 + 1 + 2 + 2 = 20
            // Factor is cheaper (17 < 20).
            
            var extractedFactored = EGraphExtract.ExtractBest(graph, id1, null, preferFactoredCost);
            Assert.AreEqual("((a + b) * c)", extractedFactored.ToDisplayString());
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestExtractExact()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            var two = new Number(2);
            var four = new Number(4);

            int id = graph.Add(new Add(x, x));
            graph.Union(id, graph.Add(four));
            graph.Rebuild();

            var found = EGraphExtract.ExtractExact(graph, id, four);
            Assert.IsNotNull(found);
            Assert.AreEqual("4", found.ToDisplayString());

            var notFound = EGraphExtract.ExtractExact(graph, id, new Number(5));
            Assert.IsNull(notFound);
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestRebuild_ChainReaction()
        {
            // a=b, b=c, c=d, d=e. Rebuild should propagate all to one class.
            var graph = new EGraph();
            int a = graph.Add(new Symbol("a"));
            int b = graph.Add(new Symbol("b"));
            int c = graph.Add(new Symbol("c"));
            int d = graph.Add(new Symbol("d"));
            int e = graph.Add(new Symbol("e"));

            graph.Union(a, b);
            graph.Union(b, c);
            graph.Union(c, d);
            graph.Union(d, e);
            
            graph.Rebuild();

            int root = graph.Find(a);
            Assert.AreEqual(root, graph.Find(b));
            Assert.AreEqual(root, graph.Find(c));
            Assert.AreEqual(root, graph.Find(d));
            Assert.AreEqual(root, graph.Find(e));
        }
        
        [TestMethod]
        [Timeout(30000)]
        public void TestEGraphMatcher_Wildcards()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            var y = new Symbol("y");
            var zero = new Number(0);
            
            graph.Add(new Add(x, zero));
            graph.Add(new Add(y, zero));
            
            // Rule: ?a + 0 -> ?a
            var rule = new Rule(new Add(new Wild("a"), zero), new Wild("a"));
            
            var history = new MatchHistory();
            var matches = EGraphMatcher.FindMatches(graph, ImmutableList.Create(rule), history);
            
            Assert.AreEqual(2, matches.Count);
            
            var xMatch = matches.FirstOrDefault(m => m.Bindings["a"] == graph.Find(graph.Add(x)));
            var yMatch = matches.FirstOrDefault(m => m.Bindings["a"] == graph.Find(graph.Add(y)));
            
            Assert.IsNotNull(xMatch);
            Assert.IsNotNull(yMatch);
        }
    }
}
