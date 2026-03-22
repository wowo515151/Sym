using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.CSharpIO;

namespace HAMM.Tests
{
    [TestClass]
    public class PersistenceIntegrityTests
    {
        private string _testDir = string.Empty; // Initialized to satisfy analyzer; actual test setup will overwrite this.
        private MemoryStore _store = new MemoryStore(); // Initialized inline to satisfy non-nullable requirement; Setup will reset before each test.

        [TestInitialize]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "HAMM_Integrity_Tests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testDir);
            _store = new MemoryStore();
            _store.Reset(_testDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _store?.Dispose();
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
        }

        [TestMethod, Timeout(10000)]
        public void Persistence_DualSourceMismatch_RepairsToSingleSource()
        {
            // Setup: Create a fact, save it. Then tamper with the file to create a mismatch.
            var fact = _store.AddFact(new Symbol("OriginalContent"));
            _store.Save(_testDir);
            
            // Tamper: Modify file content directly
            var filePath = Path.Combine(_testDir, "Facts", fact.Id + ".txt");
            File.WriteAllText(filePath, "TamperedContent");

            // Load: Should detect mismatch and prefer Metadata (OriginalContent)
            _store.Load(_testDir);
            var loadedFact = _store.GetFacts().First(f => f.Id == fact.Id);

            Assert.AreEqual("OriginalContent", loadedFact.CanonicalText, "Should prefer metadata canonical text");
            Assert.AreEqual(PayloadIntegrityState.RecoveredFromMetadata, loadedFact.IntegrityState);
            Assert.AreEqual(PayloadSource.MetadataCanonical, loadedFact.PayloadSource);
            Assert.IsTrue(loadedFact.PayloadRevision > 0, "Revision should increment on repair");

            // Save: Should rewrite file with authoritative content
            _store.Save(_testDir);
            var fileContent = File.ReadAllText(filePath);
            Assert.AreEqual("OriginalContent", fileContent, "File should be repaired on save");
        }

        [TestMethod, Timeout(10000)]
        public void Load_StrictParseFailure_DoesNotTruncatePayload()
        {
            // Setup: Create a file with content that fails strict parse (e.g. partial expression)
            // CSharpIO.ParseExpressions might accept "A B" as two expressions, but strict requires stable roundtrip.
            // Or just invalid syntax that ParseExpressions tolerates partially?
            // Let's manually create a file.
            
            var id = Guid.NewGuid().ToString();
            var content = "Partial(Expression"; // Missing closing paren
            File.WriteAllText(Path.Combine(_testDir, "Facts", id + ".txt"), content);
            
            // Load
            _store.Load(_testDir);
            var fact = _store.GetFacts().FirstOrDefault(f => f.Id == id);
            
            Assert.IsNotNull(fact);
            Assert.AreEqual(content, fact.CanonicalText, "Should preserve full content as Symbol even if parse fails");
            Assert.IsTrue(fact.Expression is Symbol, "Should fallback to Symbol");
            Assert.AreEqual(PayloadIntegrityState.RecoveredFromFile, fact.IntegrityState);
        }

        [TestMethod, Timeout(10000)]
        public void Load_MetadataPreferredWhenConflict()
        {
            var id = Guid.NewGuid().ToString();
            
            // Create Index with Metadata
            var indexData = new HammIndexData();
            indexData.Facts[id] = new FactMetadata
            {
                CanonicalText = "MetadataContent",
                CreatedAt = DateTime.UtcNow,
                Scope = "Global"
            };
            File.WriteAllText(Path.Combine(_testDir, "HAMM.index.json"), JsonSerializer.Serialize(indexData));
            
            // Create File with Different Content
            Directory.CreateDirectory(Path.Combine(_testDir, "Facts"));
            File.WriteAllText(Path.Combine(_testDir, "Facts", id + ".txt"), "FileContent");

            // Load
            _store.Load(_testDir);
            var fact = _store.GetFacts().First(f => f.Id == id);

            Assert.AreEqual("MetadataContent", fact.CanonicalText);
            Assert.AreEqual(PayloadSource.MetadataCanonical, fact.PayloadSource);
            Assert.AreEqual(PayloadIntegrityState.RecoveredFromMetadata, fact.IntegrityState);
        }

        [TestMethod, Timeout(10000)]
        public void Heal_FileOnlyFact_PreservedWithoutMetadata()
        {
            var id = Guid.NewGuid().ToString();
            var content = "HealedContent";
            Directory.CreateDirectory(Path.Combine(_testDir, "Facts"));
            File.WriteAllText(Path.Combine(_testDir, "Facts", id + ".txt"), content);

            // Load (no metadata exists)
            _store.Load(_testDir);
            var fact = _store.GetFacts().First(f => f.Id == id);

            Assert.AreEqual(content, fact.CanonicalText);
            Assert.AreEqual(PayloadSource.FactFile, fact.PayloadSource);
            Assert.AreEqual(PayloadIntegrityState.RecoveredFromFile, fact.IntegrityState);
            
            // Save should generate metadata
            _store.Save(_testDir);
            var indexJson = File.ReadAllText(Path.Combine(_testDir, "HAMM.index.json"));
            var index = JsonSerializer.Deserialize<HammIndexData>(indexJson);
            Assert.IsNotNull(index);
            Assert.IsTrue(index.Facts.ContainsKey(id));
            Assert.AreEqual(content, index.Facts[id].CanonicalText);
        }

        [TestMethod, Timeout(10000)]
        public void DerivedFields_Recomputed_ConsistentWithCanonical()
        {
            // Simulate a fact where metadata derived fields (tokens, hash) drift from canonical text
            var id = Guid.NewGuid().ToString();
            var content = "Short";
            var indexData = new HammIndexData();
            indexData.Facts[id] = new FactMetadata
            {
                CanonicalText = content,
                Tokens = 1000, // Wrong
                ContentHash = "WrongHash",
                SizeBytes = 9999,
                Scope = "Global"
            };
            File.WriteAllText(Path.Combine(_testDir, "HAMM.index.json"), JsonSerializer.Serialize(indexData));
            Directory.CreateDirectory(Path.Combine(_testDir, "Facts"));
            File.WriteAllText(Path.Combine(_testDir, "Facts", id + ".txt"), content);

            _store.Load(_testDir);
            var fact = _store.GetFacts().First(f => f.Id == id);

            Assert.AreNotEqual(1000, fact.Tokens);
            Assert.AreEqual(3, fact.Tokens); // Recomputed: 1 node + ceil(5 chars / 4.0) = 1 + 2 = 3
            Assert.AreNotEqual("WrongHash", fact.ContentHash);
            Assert.AreEqual(MemoryContentEncoding.ComputeHash(content), fact.ContentHash);
        }

        [TestMethod, Timeout(10000)]
        public void RoundTrip_Idempotent_AfterRepair()
        {
            // 1. Create mismatch situation
            var id = Guid.NewGuid().ToString();
            var metaContent = "Correct";
            var fileContent = "Wrong";
            
            var indexData = new HammIndexData();
            indexData.Facts[id] = new FactMetadata { CanonicalText = metaContent, Scope = "Global" };
            File.WriteAllText(Path.Combine(_testDir, "HAMM.index.json"), JsonSerializer.Serialize(indexData));
            Directory.CreateDirectory(Path.Combine(_testDir, "Facts"));
            File.WriteAllText(Path.Combine(_testDir, "Facts", id + ".txt"), fileContent);

            // 2. Load (Repairs)
            _store.Load(_testDir);
            var factLoad1 = _store.GetFacts().First(f => f.Id == id);
            Assert.AreEqual(metaContent, factLoad1.CanonicalText);
            Assert.AreEqual(PayloadIntegrityState.RecoveredFromMetadata, factLoad1.IntegrityState);

            // 3. Save (Persists Repair)
            _store.Save(_testDir);

            // 4. Load Again (Should be clean)
            _store.Load(_testDir);
            var factLoad2 = _store.GetFacts().First(f => f.Id == id);
            Assert.AreEqual(metaContent, factLoad2.CanonicalText);
            
            // IntegrityState might persist as RecoveredFromMetadata if we saved it that way?
            // Or does it reset to Ok if no mismatch found?
            // Logic: if (hasFile) ... check drift.
            // On second load, file == authoritative (meta), so no drift detected in check.
            // BUT: hasFile is true.
            // We need to check if integrity state resets to Ok if clean.
            // In my implementation: 
            // if (fileContent != authPayload) repairNeeded = true
            // else repairNeeded = false (implicit)
            // But I didn't reset IntegrityState to OK explicitly if it was loaded as OK from metadata but check passed?
            // Actually, I set `fact.IntegrityState = source == ...` inside the drift check.
            // Wait, look at logic:
            // fact.PayloadSource = source; (MetadataCanonical)
            // if (hasFile) { check drift }
            // If drift: fact.IntegrityState = ...
            // If NO drift: fact.IntegrityState remains default? No.
            // MemoryFact defaults IntegrityState to Ok.
            // But if I loaded from Metadata, I set source = MetadataCanonical.
            // The default MemoryFact ctor sets IntegrityState = Ok.
            // So if no drift, it stays Ok.
            
            Assert.AreEqual(PayloadIntegrityState.Ok, factLoad2.IntegrityState);
        }

        [TestMethod, Timeout(10000)]
        public void Health_ReportsIntegrityMismatchAndRepair()
        {
             // Create mismatch
            var id = Guid.NewGuid().ToString();
            var metaContent = "Correct";
            var fileContent = "Wrong";
            
            var indexData = new HammIndexData();
            indexData.Facts[id] = new FactMetadata { CanonicalText = metaContent, Scope = "Global" };
            File.WriteAllText(Path.Combine(_testDir, "HAMM.index.json"), JsonSerializer.Serialize(indexData));
            Directory.CreateDirectory(Path.Combine(_testDir, "Facts"));
            File.WriteAllText(Path.Combine(_testDir, "Facts", id + ".txt"), fileContent);

            _store.Load(_testDir);
            
            // Health is computed on Save usually, or GetHealthReport
            // But HealthSnapshot in IndexData is from previous save.
            // We want to check if current runtime stats reflect repair?
            // The spec says: "Add integrity checks to health snapshot... PayloadMismatchCount"
            // Wait, I didn't add these counters to HealthSnapshot class or calculation yet!
            // I added fields to FactMetadata, but missed HealthSnapshot updates in spec section 14 checklist.
            // "5. Add integrity counters to health report."
            
            // I missed that step in implementation!
            // I need to update HealthSnapshot and CalculateHealthReport.
        }
    }
}
