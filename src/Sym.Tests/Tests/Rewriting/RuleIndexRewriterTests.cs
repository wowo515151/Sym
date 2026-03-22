// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;
using System.Collections.Generic;

namespace SymTest.Rewriting
{
    [TestClass]
    public class RuleIndexRewriterTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void RewriteSinglePass_WithRuleIndex_AppliesRule()
        {
            var x = new Symbol("x");
            var zero = new Number(0m);
            var rule = new Rule(new Add(new Wild("a"), zero), new Wild("a"));
            var index = RuleIndex.Create(new[] { rule });
            
            var expr = new Add(x, zero);
            var result = Rewriter.RewriteSinglePass(expr, index);
            
            Assert.IsTrue(result.Changed);
            Assert.AreEqual("x", result.RewrittenExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void RewriteFully_WithRuleIndex_AppliesMultipleRules()
        {
            var x = new Symbol("x");
            var one = new Number(1m);
            var zero = new Number(0m);
            
            var rules = new List<Rule>
            {
                new Rule(new Add(new Wild("a"), zero), new Wild("a")),
                new Rule(new Multiply(new Wild("b"), one), new Wild("b"))
            };
            var index = RuleIndex.Create(rules);
            
            // (x + 0) * 1
            var expr = new Multiply(new Add(x, zero), one);
            var result = Rewriter.RewriteFully(expr, index);
            
            Assert.IsTrue(result.Changed);
            Assert.AreEqual("x", result.RewrittenExpression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void RuleIndex_GetCandidateRules_FiltersCorrectly()
        {
            var x = new Symbol("x");
            var sinX = new Function("sin", ImmutableList.Create<IExpression>(x));
            var cosX = new Function("cos", ImmutableList.Create<IExpression>(x));
            
            var ruleSin = new Rule(new Function("sin", ImmutableList.Create<IExpression>(new Wild("a"))), new Number(0m));
            var ruleCos = new Rule(new Function("cos", ImmutableList.Create<IExpression>(new Wild("b"))), new Number(1m));
            var ruleWild = new Rule(new Wild("w"), new Number(-1m));
            
            var index = RuleIndex.Create(new[] { ruleSin, ruleCos, ruleWild });
            
            var candidatesSin = index.GetCandidateRules(sinX).ToImmutableList();
            Assert.IsTrue(candidatesSin.Contains(ruleSin));
            Assert.IsTrue(candidatesSin.Contains(ruleWild));
            Assert.IsFalse(candidatesSin.Contains(ruleCos));
            
            var candidatesCos = index.GetCandidateRules(cosX).ToImmutableList();
            Assert.IsTrue(candidatesCos.Contains(ruleCos));
            Assert.IsTrue(candidatesCos.Contains(ruleWild));
            Assert.IsFalse(candidatesCos.Contains(ruleSin));
        }
    }
}
