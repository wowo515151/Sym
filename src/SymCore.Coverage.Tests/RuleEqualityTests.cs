// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymCore.Coverage.Tests
{
    [TestClass]
    public class RuleEqualityTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestRuleEquality_IgnoresName()
        {
            var p = new Symbol("a");
            var r = new Symbol("b");
            var rule1 = new Sym.Core.Rule(p, r) { Name = "A" };
            var rule2 = new Sym.Core.Rule(p, r) { Name = "B" };
            
            Assert.AreEqual(rule1, rule2, "Rules with same pattern/replacement should be equal.");
            Assert.AreEqual(rule1.GetHashCode(), rule2.GetHashCode());
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestRuleEquality_DifferentiatesByTransform()
        {
            // WAIT! Check Rule.Equals implementation in Rule.cs.
            // It ONLY compares Pattern and Replacement.
            
            var p = new Symbol("a");
            var r = new Symbol("b");
            Func<ImmutableDictionary<string, IExpression>, IExpression?> t1 = b => new Symbol("c");
            
            var rule1 = new Sym.Core.Rule(p, r, null, null, t1) { Name = "T1" };
            var rule2 = new Sym.Core.Rule(p, r) { Name = "NoT" };
            
            // If Rule.Equals is used in MatchHistory or other collections, this might be a problem.
            Assert.AreNotEqual(rule1, rule2, "Rules with different transforms should NOT be equal.");
        }
    }
}
