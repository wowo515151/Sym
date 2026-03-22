using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace Sym.Tests.Core
{
    [TestClass]
    public class RuleIndexTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void RuleIndex_GroupsByHead()
        {
            var x = new Symbol("x");
            var rules = new List<Rule>
            {
                new Rule(new Add(x, new Number(0)), x),
                new Rule(new Multiply(x, new Number(1)), x),
                new Rule(new Wild("y"), new Number(1)) // Universal rule
            };

            var index = RuleIndex.Create(rules);

            // Add head should be "add" (lowered)
            var addRules = index.GetCandidateRules(new Add(x, new Number(1)));
            // Should contain the Add rule and the Wild rule
            Assert.AreEqual(2, addRules.Count());

            // Multiply head should be "multiply"
            var mulRules = index.GetCandidateRules(new Multiply(x, new Number(2)));
            Assert.AreEqual(2, mulRules.Count());

            // Some other head should only get the Wild rule
            var sinRules = index.GetCandidateRules(new Function("sin", x));
            Assert.AreEqual(1, sinRules.Count());
        }

        [TestMethod]
        [Timeout(10000)]
        public void RuleIndex_Equality()
        {
            var x = new Symbol("x");
            var rule = new Rule(new Add(x, new Number(0)), x);
            
            var index1 = RuleIndex.Create(new[] { rule });
            var index2 = RuleIndex.Create(new[] { new Rule(new Add(x, new Number(0)), x) });
            
            Assert.AreEqual(index1, index2);
            Assert.AreEqual(index1.GetHashCode(), index2.GetHashCode());
        }
    }
}
