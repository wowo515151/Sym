// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;

namespace Sym.Tests.Core
{
    [TestClass]
    public class EGraphTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void EGraph_Add_SimpleExpression()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            var expr = new Add(x, new Number(1));
            
            int id = graph.Add(expr);
            Assert.IsTrue(id >= 0);
            Assert.AreEqual(3, graph.NodeCount); // 'x', '1', and 'Add(x, 1)'
        }

        [TestMethod]
        [Timeout(10000)]
        public void EGraph_Union_MergesClasses()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            var y = new Symbol("y");
            
            int idx = graph.Add(x);
            int idy = graph.Add(y);
            
            Assert.AreNotEqual(graph.Find(idx), graph.Find(idy));
            
            graph.Union(idx, idy);
            Assert.AreEqual(graph.Find(idx), graph.Find(idy));
        }

        [TestMethod]
        [Timeout(10000)]
        public void EGraph_Rebuild_CongruenceClosure()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            var y = new Symbol("y");
            
            // f(x)
            var fx = new Function("f", x);
            // f(y)
            var fy = new Function("f", y);
            
            int idfx = graph.Add(fx);
            int idfy = graph.Add(fy);
            
            int idx = graph.Add(x);
            int idy = graph.Add(y);
            
            Assert.AreNotEqual(graph.Find(idfx), graph.Find(idfy));
            
            // Union x and y
            graph.Union(idx, idy);
            // Rebuild should detect f(x) == f(y) because x == y
            graph.Rebuild();
            
            Assert.AreEqual(graph.Find(idfx), graph.Find(idfy));
        }

        [TestMethod]
        [Timeout(10000)]
        public void EGraph_Extraction_BestExpression()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            var one = new Number(1);
            
            // (1 + x)
            var expr1 = new Add(one, x);
            // (x + 1)
            var expr2 = new Add(x, one);
            
            int id1 = graph.Add(expr1);
            int id2 = graph.Add(expr2);
            
            // They are NOT equivalent yet
            Assert.AreNotEqual(graph.Find(id1), graph.Find(id2));
            
            // Force them to be equivalent (simulating a rule application)
            graph.Union(id1, id2);
            graph.Rebuild();
            
            // Extract best
            var best = EGraphExtract.ExtractBest(graph, id1);
            Assert.IsNotNull(best);
            // Since cost is uniform, either (1+x) or (x+1) could be chosen.
            // By default it picks based on iteration order.
            Assert.IsTrue(best.ToDisplayString().Contains("x") && best.ToDisplayString().Contains("1"));
        }
    }
}
