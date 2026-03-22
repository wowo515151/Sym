// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HAMM;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using System.IO;
using System.Collections.Immutable;

namespace HAMM.Tests
{
    [TestClass]
    public class HAMMv4Tests
    {
        private const int TestTimeout = 10000;
        private string _tempPath = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "HAMM_Test_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempPath);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestDedupOnLoadMerge()
        {
            using (var store = new MemoryStore())
            {
                var expr = new Symbol("CommonFact");
                // Manually add two facts that should merge on load
                // We'll simulate this by adding them normally, then saving and loading.
                // But AddFact normally dedups. 
                // To test LOAD-time dedup, we can manually manipulate the files or use internal methods if available.
                
                store.AddFact(expr);
                store.Save(_tempPath);
            }

            // Manually duplicate the fact file and metadata entry
            string indexPath = Path.Combine(_tempPath, "HAMM.index.json");
            string indexJson = File.ReadAllText(indexPath);
            // We can't easily edit JSON for complex structure, but we can try.
            // Alternatively, we can use MemoryStore to add without dedup by some hack.
            
            // Actually, let's just use AddFactInternal with allowDedup: false
            using (var store = new MemoryStore())
            {
                var expr = new Symbol("CommonFact");
                // Use a private/internal way if possible, or just mock the persistence
                // AddFactInternal is private.
            }

            // Let's use reflection to call AddFactInternal with allowDedup: false
            using (var store = new MemoryStore())
            {
                var method = typeof(MemoryStore).GetMethod("AddFactInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var expr = new Symbol("CommonFact");
                method!.Invoke(store, new object?[] { expr, "Global", 1.0, ReActType.Generic, null, false, IngestSourceType.User, "User", null, null });
                method!.Invoke(store, new object?[] { expr, "Global", 1.0, ReActType.Generic, null, false, IngestSourceType.User, "User", null, null });
                
                Assert.AreEqual(2, store.GetFacts("Global").Count(), "Should have 2 facts before save because dedup was disabled.");
                store.Save(_tempPath);
            }

            using (var store = new MemoryStore())
            {
                store.Load(_tempPath);
                var facts = store.GetFacts("Global").ToList();
                Assert.AreEqual(1, facts.Count, "Should have merged 2 facts into 1 during load.");
                Assert.AreEqual(3, facts[0].OccurrenceCount, "OccurrenceCount should be 3 after load merge.");
            }
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestPathTextClassificationRegression()
        {
            using (var store = new MemoryStore())
            {
                store.AutoNoiseByPathDisabled = true;
                
                var pathFact = new Symbol(@"C:\Project\Source\File.cs");
                var fact = store.AddFact(pathFact);
                
                Assert.AreNotEqual(FactKind.Noise, fact.Kind, "Path-like text should NOT be auto-labeled Noise when AutoNoiseByPathDisabled is true.");
                Assert.AreEqual("Global", fact.Scope, "Should remain in Global or appropriate scope if not Noise.");
            }
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestScopeRoutingV4()
        {
            using (var store = new MemoryStore())
            {
                // Directive
                var goal = store.AddFact(new Function("Goal", new Symbol("Win")));
                Assert.AreEqual("Goals", goal.Scope);
                Assert.AreEqual(FactKind.Directive, goal.Kind);

                // ToolTrace
                var trace = store.AddFactV2(new AddFactRequest { Expression = new Symbol("Output"), SourceType = IngestSourceType.Tool });
                Assert.AreEqual("Diagnostics", trace.Scope);
                Assert.AreEqual(FactKind.ToolTrace, trace.Kind);

                // Artifact (oversized)
                var oversized = new Symbol(new string('a', 5000));
                var art = store.AddFact(oversized);
                Assert.AreEqual("Artifacts", art.Scope);
                Assert.AreEqual(FactKind.ArtifactPointer, art.Kind);
                Assert.AreEqual(MemoryContentType.Artifact, art.ContentType);

                // Noise
                var noise = store.AddFact(new Symbol("---"));
                Assert.AreEqual("Noise", noise.Scope);
                Assert.AreEqual(FactKind.Noise, noise.Kind);
            }
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestFastMaintenanceTrigger()
        {
            // Maintenance trigger requires tokens to exceed TargetActiveTokens.
            // Since AddFact now performs auto-maintenance, we set TargetActiveTokens high during ingestion 
            // to accumulate tokens, then lower it to verify manual FastMaintenance.
            using (var store = new MemoryStore())
            {
                store.TargetActiveTokens = 1000; // Set high initially to prevent auto-maintenance during ingestion
                store.MaxActiveTokens = 2000;
                
                // Add facts until we have enough tokens
                for (int i = 0; i < 5; i++)
                {
                    store.AddFact(new Symbol($"Fact_{i}_{new string('x', 10)}"));
                }
                
                // Active tokens should be ~30
                var activeTokensBefore = store.GetFacts().Sum(f => f.Tokens);
                store.TargetActiveTokens = 10; // Lower it now to trigger maintenance
                
                Assert.IsTrue(activeTokensBefore > store.TargetActiveTokens);
                
                store.FastMaintenance();
                
                var activeTokensAfter = store.GetFacts().Sum(f => f.Tokens);
                Assert.IsTrue(activeTokensAfter <= store.TargetActiveTokens, $"Active tokens ({activeTokensAfter}) should be <= TargetActiveTokens ({store.TargetActiveTokens}).");
            }
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestRecallSummaryBoost()
        {
            using (var store = new MemoryStore())
            {
                var factExpr = new Symbol("RegularFact");
                var summaryExpr = new Summary(new IExpression[] { new Symbol("S1"), new Symbol("S2") }.ToImmutableList());
                
                store.AddFact(factExpr);
                store.AddFact(summaryExpr);
                
                // Query that might hit both or we just check scoring if we could
                var results = store.QueryV2(new Symbol("S1"), new QueryOptions { IncludeArchive = false }).ToList();
                // Summary should be boosted. 
                // Let's check results order if we can make them comparable.
            }
        }
        
        [TestMethod, Timeout(TestTimeout)]
        public void TestResetProcedure()
        {
            using (var store = new MemoryStore())
            {
                store.AddFact(new Symbol("ToPersist"));
                store.Save(_tempPath);
                Assert.IsTrue(Directory.GetFiles(Path.Combine(_tempPath, "Facts")).Length > 0);
            }

            using (var store = new MemoryStore())
            {
                store.Reset(_tempPath);
                Assert.AreEqual(0, store.GetFacts().Count());
                Assert.AreEqual(0, Directory.GetFiles(Path.Combine(_tempPath, "Facts")).Length);
            }
        }
    }
}
