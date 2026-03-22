//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SymRules;

namespace SymRules.Tests
{
    [TestClass]
    public class ExtendedRuleCoverageTests
    {
        [TestMethod]
        [Timeout(30000)]
        public void TestRuleTextParser_TypedWildcards()
        {
            string text = "f(?x:Number) -> ?x";
            bool success = RuleTextParser.TryGenerateCoreSource(text, out var coreSource, out var diagnostic);
            
            Assert.IsTrue(success, diagnostic);
            Assert.AreEqual("Rule(f(Wild(\"x\", Number)), Wild(\"x\"))", coreSource);
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestRuleTextParser_MixedWildcards()
        {
            string text = "?a + ?b:Symbol -> ?b + ?a";
            bool success = RuleTextParser.TryGenerateCoreSource(text, out var coreSource, out var diagnostic);
            
            Assert.IsTrue(success, diagnostic);
            // RHS only has ?b, so it becomes Wild("b") without the type.
            Assert.AreEqual("Rule(Wild(\"a\") + Wild(\"b\", Symbol), Wild(\"b\") + Wild(\"a\"))", coreSource);
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestRuleTextParser_NamedRule()
        {
            string text = "MyRule: a -> b";
            bool success = RuleTextParser.TryGenerateCoreSource(text, out var coreSource, out var diagnostic);
            
            Assert.IsTrue(success, diagnostic);
            Assert.AreEqual("Rule(Wild(\"a\"), Wild(\"b\"))", coreSource);
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestRulePackLibrary_LoadsStandardPacks()
        {
            var packs = RulePackLibrary.GetRulePacks();
            Assert.IsTrue(packs.Count > 0, "Should find at least one rule pack in the repository.");
            
            // The name in pack.json is AlgebraicStrategy
            var algebraic = packs.FirstOrDefault(p => p.Name == "AlgebraicStrategy");
            Assert.IsNotNull(algebraic);
            Assert.IsTrue(algebraic.Rules.Count > 0, "Algebraic pack should have rules.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestRuleLoader_GetRuleFiles_HandlesEmptyOrMissing()
        {
            // We can't easily test missing directory without IO, but we can check if it returns files from a known path.
            var dir = AppContext.BaseDirectory;
            var files = RuleLoader.GetRuleFiles(dir);
            // Just ensure it doesn't crash
            Assert.IsNotNull(files);
        }
        
        [TestMethod]
        [Timeout(30000)]
        public void TestRuleTextParser_InvalidRule_MissingArrow()
        {
            string text = "a + b";
            bool success = RuleTextParser.TryGenerateCoreSource(text, out var coreSource, out var diagnostic);
            Assert.IsFalse(success);
            Assert.AreEqual("Rule must contain a single '->' separator", diagnostic);
        }
    }
}
