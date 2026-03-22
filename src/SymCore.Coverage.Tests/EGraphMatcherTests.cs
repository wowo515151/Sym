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
    public class EGraphMatcherTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestFindMatches_PreservesRuleObject()
        {
            var graph = new EGraph();
            graph.Add(new Add(new Symbol("x"), new Number(0)));
            graph.Rebuild();

            var pattern = new Add(new Wild("a"), new Number(0));
            var replacement = new Wild("a");
            var rule = new Sym.Core.Rule(pattern, replacement) { Name = "AddZero" };
            
            var rules = ImmutableList.Create(rule);
            var matches = EGraphMatcher.FindMatches(graph, rules);
            
#pragma warning disable MSTEST0037
            Assert.IsTrue(matches.Count == 1, "Should match 1 rule.");
#pragma warning restore MSTEST0037
            Assert.AreSame(rule, matches[0].Rule, "Matcher should return the EXACT same rule object.");
            Assert.IsNull(matches[0].Rule.Transform, "Transform should still be null in the matched rule.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestFindMatches_WithTransform_PreservesTransform()
        {
            var graph = new EGraph();
            graph.Add(new Add(new Number(1), new Number(2)));
            graph.Rebuild();

            var pattern = new Add(new Wild("a"), new Wild("b"));
            Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = b => new Number(3);
            var rule = new Sym.Core.Rule(pattern, new Number(0), null, null, transform) { Name = "ConstAdd" };
            
            var rules = ImmutableList.Create(rule);
            var matches = EGraphMatcher.FindMatches(graph, rules);
            
#pragma warning disable MSTEST0037
            Assert.IsTrue(matches.Count == 1, "Should match 1 rule.");
#pragma warning restore MSTEST0037
            Assert.AreSame(rule, matches[0].Rule);
            Assert.IsNotNull(matches[0].Rule.Transform, "Transform should be preserved in the matched rule.");
        }
    }
}
