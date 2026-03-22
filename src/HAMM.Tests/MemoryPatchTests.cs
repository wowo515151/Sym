using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;

namespace HAMM.Tests
{
    [TestClass]
    public class MemoryPatchTests
    {
        private string _tempDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "HAMM_Patch_Tests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestArtifactPersistence()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            hamm.Store.MaxInlineSymbolChars = 10; // Small limit to force artifact creation

            var largeContent = "This is a very long string that should definitely be turned into an artifact because it exceeds the limit.";
            var expr = new Symbol(largeContent);

            hamm.Remember(expr, "Global");

            // Verify it's in store - Artifacts are auto-routed
            var facts = hamm.Store.GetFacts("Artifacts").ToList();
            Assert.AreEqual(1, facts.Count);
            Assert.AreEqual(MemoryContentType.Artifact, facts[0].ContentType);
            Assert.IsTrue(facts[0].Expression is Symbol);
            Assert.IsTrue(((Symbol)facts[0].Expression).Name.StartsWith("ContentHash:"));

            // Save
            hamm.Store.Save(_tempDir);

            // Verify artifact file exists
            var artifactsDir = Path.Combine(_tempDir, "Artifacts");
            Assert.IsTrue(Directory.Exists(artifactsDir));
            var files = Directory.GetFiles(artifactsDir);
            Assert.AreEqual(1, files.Length);
            
            var savedContent = File.ReadAllText(files[0]);
            Assert.AreEqual(largeContent, savedContent);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestConceptPinning()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            
            hamm.Remember(new Symbol("ImportantConcept"), "Concepts");
            hamm.Remember(new Symbol("AnotherConcept"), "HAMM");
            hamm.Remember(new Symbol("JustAFact"), "Global");

            var facts = hamm.Store.GetFacts("Concepts").ToList();
            Assert.AreEqual(MemoryRetentionPolicy.Pinned, facts[0].Retention);

            facts = hamm.Store.GetFacts("HAMM").ToList();
            Assert.AreEqual(MemoryRetentionPolicy.Pinned, facts[0].Retention);

            facts = hamm.Store.GetFacts("Global").ToList();
            Assert.AreEqual(MemoryRetentionPolicy.Normal, facts[0].Retention);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestPathTokenFiltering()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            hamm.Store.AutoNoiseByPathDisabled = false;
            
            hamm.Remember(new Symbol("src/Some/Path"), "Global");
            hamm.Remember(new Symbol("bin"), "Global");
            hamm.Remember(new Symbol("NormalFact"), "Global");

            // Query Noise scope to see both Noise and Global facts (as Global is parent)
            var facts = hamm.Store.GetFacts("Noise").ToList();
            
            var pathFact = facts.First(f => f.Expression.ToDisplayString() == "src/Some/Path");
            var binFact = facts.First(f => f.Expression.ToDisplayString() == "bin");
            var normalFact = facts.First(f => f.Expression.ToDisplayString() == "NormalFact");

            // Path tokens should be ephemeral or low priority
            // We'll check for Ephemeral as that's a strong signal
            Assert.AreEqual(MemoryRetentionPolicy.Ephemeral, pathFact.Retention);
            Assert.AreEqual(MemoryRetentionPolicy.Ephemeral, binFact.Retention);
            Assert.AreEqual(MemoryRetentionPolicy.Normal, normalFact.Retention);
        }
    }
}
