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

namespace SymCore.Coverage.Tests
{
    [TestClass]
    public class EGraphEqualityTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestMatching_NumericLiteral()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            // Add x^2
            graph.Add(new Power(x, new Number(2)));
            graph.Rebuild();

            // Pattern: Pow(a, 2)
            var pattern = new Power(new Wild("a"), new Number(2));
            var rule = new Sym.Core.Rule(pattern, new Symbol("match"));
            
            var matches = EGraphMatcher.FindMatches(graph, ImmutableList.Create(rule));
#pragma warning disable MSTEST0037
            Assert.IsTrue(matches.Count == 1, "Should match x^2 structurally with numeric literal 2");
#pragma warning restore MSTEST0037
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestEqualityMatching_Directional()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            var four = new Number(4);
            
            // Add x^2 = 4
            graph.Add(new Equality(new Power(x, new Number(2)), four));
            graph.Rebuild();

            // Pattern: Equality(Pow(a, 2), b)
            var pattern = new Equality(new Power(new Wild("a"), new Number(2)), new Wild("b"));
            var rule = new Sym.Core.Rule(pattern, new Symbol("match"));
            
            var matches = EGraphMatcher.FindMatches(graph, ImmutableList.Create(rule));
#pragma warning disable MSTEST0037
            Assert.IsTrue(matches.Count == 1, "Should match x^2 = 4");
#pragma warning restore MSTEST0037
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestEqualityMatching_Reordered_Fails()
        {
            var graph = new EGraph();
            var x = new Symbol("x");
            var four = new Number(4);
            
            // Add 4 = x^2
            graph.Add(new Equality(four, new Power(x, new Number(2))));
            graph.Rebuild();

            // Pattern: Equality(Pow(a, 2), b)
            var pattern = new Equality(new Power(new Wild("a"), new Number(2)), new Wild("b"));
            var rule = new Sym.Core.Rule(pattern, new Symbol("match"));
            
            var matches = EGraphMatcher.FindMatches(graph, ImmutableList.Create(rule));
            
            // In a CAS, Equality is usually commutative, but EGraphMatcher is structural.
#pragma warning disable MSTEST0037
            Assert.IsTrue(matches.Count == 0, "Structural matcher should NOT match reversed Equality by default.");
#pragma warning restore MSTEST0037
        }
    }
}
