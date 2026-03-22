//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymRules;

namespace SymCore.Coverage.Tests
{
    [TestClass]
    public class RuleTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestRuleConstructor_TransformDefaultNull()
        {
            var pattern = new Symbol("a");
            var replacement = new Symbol("b");
            var rule = new Sym.Core.Rule(pattern, replacement);
            
            Assert.IsNull(rule.Transform, "Transform should be null by default.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestRuleConstructor_TransformPassedNull()
        {
            var pattern = new Symbol("a");
            var replacement = new Symbol("b");
            var rule = new Sym.Core.Rule(pattern, replacement, null, null, null);
            
            Assert.IsNull(rule.Transform, "Transform should be null when passed null.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestRuleConstructor_TransformPassedFunction()
        {
            var pattern = new Symbol("a");
            var replacement = new Symbol("b");
            Func<ImmutableDictionary<string, IExpression>, IExpression?> transform = b => new Symbol("c");
            var rule = new Sym.Core.Rule(pattern, replacement, null, null, transform);
            
            var t = rule.Transform;
            if (t != null)
            {
                var result = t(ImmutableDictionary<string, IExpression>.Empty);
                Assert.IsNotNull(result, "Transform result should not be null.");
                Assert.AreEqual("c", result.ToDisplayString());
            }
            else
            {
                Assert.Fail("Transform should be non-null when passed a function.");
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestRuleConverter_ToCoreRule_TransformIsNull()
        {
            var rule = new SymRules.RuleDefinition {
                Name = "TestRule",
                Text = "a + 0 -> a",
                CoreSource = "Rule(a + 0, a)"
            };
            
            var coreRule = rule.ToCoreRule();
            
            Assert.IsNotNull(coreRule);
            Assert.AreEqual("TestRule", coreRule.Name);
            Assert.IsNull(coreRule.Transform, "Converted rule should have null Transform by default.");
        }
    }
}
