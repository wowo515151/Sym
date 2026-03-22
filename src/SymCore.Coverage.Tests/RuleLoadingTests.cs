// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymRules;
using Sym.Core.EGraph;

namespace SymCore.Coverage.Tests
{
    [TestClass]
    public class RuleLoadingTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public void TestEquationSolvingPack_ContainsIsolateSquare()
        {
            var packs = RulePackLibrary.GetRulePacks();
            var pack = packs.FirstOrDefault(p => p.Name == "EquationSolving");
            Assert.IsNotNull(pack, "EquationSolving pack should be found.");
            
            var rule = pack.Rules.FirstOrDefault(r => r.Name == "IsolateSquare");
            Assert.IsNotNull(rule, "IsolateSquare rule should be found in the pack.");
            
            // Expected Pattern: Equality(Pow(Wild("a"), 2), Wild("b"))
            // Actually, based on RuleConverter logic:
            // "a" -> Wild("a")
            // "2" -> Number(2)
            // "b" -> Wild("b")
            
            Assert.IsInstanceOfType(rule.Pattern, typeof(Equality));
            var eq = (Equality)rule.Pattern;
            System.Console.Error.WriteLine($"DEBUG: IsolateSquare Pattern Head: {ENode.GetHead(rule.Pattern)}");
            System.Console.Error.WriteLine($"DEBUG: IsolateSquare LHS Head: {ENode.GetHead(eq.LeftOperand)}");
            
            Assert.IsInstanceOfType(eq.LeftOperand, typeof(Power));
            var pow = (Power)eq.LeftOperand;
            Assert.IsInstanceOfType(pow.Base, typeof(Wild));
            Assert.IsInstanceOfType(pow.Exponent, typeof(Number));
            Assert.AreEqual(2m, ((Number)pow.Exponent).Value);
            Assert.IsInstanceOfType(eq.RightOperand, typeof(Wild));
        }
    }
}
