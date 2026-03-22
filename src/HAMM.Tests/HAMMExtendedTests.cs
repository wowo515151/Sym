// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HAMM;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using System.Collections.Immutable;
using System.IO;

namespace HAMM.Tests
{
    [TestClass]
    public class HAMMExtendedTests
    {
        private const int TestTimeout = 10000;
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestSaveLoad()
        {
            var a = new Symbol("A");
            var b = new Symbol("B");
            var eq = new Equality(a, b);

            // 1. Create and Save
            using (var hamm1 = new HeuristicAssociativeMemoryModel())
            {
                hamm1.Remember(eq, certainty: 1.0);
                hamm1.Store.Save(_tempDir);
            }

            // 2. Load
            using (var hamm2 = new HeuristicAssociativeMemoryModel())
            {
                hamm2.Store.Load(_tempDir);
                
                var facts = hamm2.Store.GetFacts().ToList();
                Assert.AreEqual(1, facts.Count);
                Assert.IsTrue(facts[0].Expression.InternalEquals(eq));
                Assert.AreEqual(1.0, facts[0].Certainty, 1e-4);
                
                // Test EGraph restoration
                // var equivs = hamm2.RecallEquivalent(new Symbol("A")).ToList();
                // hamm2.Remember(new Equality(a, new Number(100)));
                // var results = hamm2.RecallEquivalent(new Equality(b, new Number(100))).ToList();
                // Assert.IsTrue(results.Any(r => r is Equality e && e.RightOperand is Number n && n.Value == 100));
            }
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestHealNewFile()
        {
            using (var hamm = new HeuristicAssociativeMemoryModel())
            {
                hamm.Store.Save(_tempDir);
            }

            // Drop a file
            var factsDir = Path.Combine(_tempDir, "Facts");
            Directory.CreateDirectory(factsDir);
            File.WriteAllText(Path.Combine(factsDir, "new.txt"), "NewFact");

            using (var hamm2 = new HeuristicAssociativeMemoryModel())
            {
                hamm2.Store.Load(_tempDir);
                var facts = hamm2.Store.GetFacts().ToList();
                Assert.AreEqual(1, facts.Count);
                Assert.IsTrue(facts[0].Expression is Symbol s && s.Name == "NewFact");
            }
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestHealDeletedFile()
        {
            string id;
            using (var hamm = new HeuristicAssociativeMemoryModel())
            {
                hamm.Remember(new Symbol("ToDelete"));
                id = hamm.Store.GetFacts().First().Id;
                hamm.Store.Save(_tempDir);
            }

            // Delete file
            File.Delete(Path.Combine(_tempDir, "Facts", id + ".txt"));

            using (var hamm2 = new HeuristicAssociativeMemoryModel())
            {
                hamm2.Store.Load(_tempDir);
                var facts = hamm2.Store.GetFacts().ToList();
                
                // v6 behavior: Fact is recovered from metadata
                Assert.AreEqual(1, facts.Count, "Deleted file should be recovered from metadata.");
                Assert.AreEqual("ToDelete", facts[0].CanonicalText);
                Assert.AreEqual(PayloadIntegrityState.RecoveredFromMetadata, facts[0].IntegrityState);
                
                // Ensure file is restored on Save
                hamm2.Store.Save(_tempDir);
                Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "Facts", id + ".txt")), "File should be restored.");
            }
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestInequalityConstraint()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            var a = new Symbol("A");
            var b = new Symbol("B");

            // 1. Declare A != B
            hamm.Store.AddDisjoint(a, b);

            // 2. Try to say A == B with high certainty
            var eq = new Equality(a, b);
            hamm.Remember(eq, certainty: 1.0);

            // 3. The fact should be immediately invalidated or rejected because it violates the disjoint constraint
            var facts = hamm.Store.GetFacts().Where(f => f.Expression.InternalEquals(eq)).ToList();
            
            // It might be added but marked invalidated instantly
            Assert.IsTrue(facts.Count == 0 || facts[0].IsInvalidated, "Fact A=B should be invalidated due to disjoint constraint.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestAdjacencyIndex()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            var a = new Symbol("A");
            var b = new Symbol("B");
            var rel = new Equality(a, b);

            hamm.Remember(rel);

            var relatedToA = hamm.Store.GetRelatedFacts("A").ToList();
            var relatedToB = hamm.Store.GetRelatedFacts("B").ToList();
            var relatedToC = hamm.Store.GetRelatedFacts("C").ToList();

            Assert.AreEqual(1, relatedToA.Count);
            Assert.AreEqual(1, relatedToB.Count);
            Assert.AreEqual(0, relatedToC.Count);
            Assert.IsTrue(relatedToA[0].Expression.InternalEquals(rel));
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestTemporalState()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            var sym = new Symbol("Score");
            var val1 = new Equality(sym, new Number(10));
            var val2 = new Equality(sym, new Number(20));

            hamm.Remember(val1);
            var fact1 = hamm.Store.GetFacts().First(f => f.Expression.InternalEquals(val1));

            // Update state
            hamm.Store.UpdateState(fact1, val2);

            var active = hamm.Store.GetFacts().ToList();
            var archive = hamm.RecallDeep(sym, limit: 100).Where(e => e.InternalEquals(val1)).ToList(); // RecallDeep checks archive

            Assert.IsFalse(active.Any(f => f.Expression.InternalEquals(val1)), "Old state should be archived.");
            Assert.IsTrue(active.Any(f => f.Expression.InternalEquals(val2)), "New state should be active.");
            
            // Check linking
            var fact2 = active.First(f => f.Expression.InternalEquals(val2));
            Assert.IsNotNull(fact2.PreviousVersion, "New fact should point to previous.");
            Assert.IsTrue(fact2.PreviousVersion.Expression.InternalEquals(val1));
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestProvenance()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            var a = new Symbol("A");
            hamm.Remember(a);
            var factA = hamm.Store.GetFacts().First();

            var b = new Symbol("B");
            hamm.Store.AddFact(b, dependencies: new[] { factA });

            var factB = hamm.Store.GetFacts().First(f => f.Expression.InternalEquals(b));
            Assert.AreEqual("Inference", factB.Source);
            Assert.AreEqual(1, factB.Dependencies.Count);
            Assert.AreEqual(factA, factB.Dependencies[0]);
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestEquivalence()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            var a = new Symbol("A");
            var b = new Symbol("B");
            
            // A = B
            hamm.Remember(new Equality(a, b), certainty: 1.0);
            
            // Remember Inverse(A) = 100
            // We use Inverse to avoid the "Single Value Assignment" logic in ResolveContradictions
            var invA = new Inverse(a);
            var num = new Number(100);
            var factA = new Equality(invA, num);
            hamm.Remember(factA);

            // Query Inverse(B) = 100
            var invB = new Inverse(b);
            var query = new Equality(invB, num);
            
            var results = hamm.RecallEquivalent(query).ToList();
            
            Assert.IsTrue(results.Any(r => r.InternalEquals(factA)), "Should find Inverse(A)=100 when querying Inverse(B)=100 if A=B.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestSimplification()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            var a = new Symbol("A");
            var b = new Symbol("B");
            var num = new Number(42);

            // Know A = 42
            hamm.Remember(new Equality(a, num), certainty: 1.0);
            
            // Simplify A -> should be 42 (cheaper cost)
            var simplified = hamm.Simplify(a);
            
            Assert.IsTrue(simplified is Number n && n.Value == 42, $"Expected 42, got {simplified.ToDisplayString()}");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestReasoning()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            // Rule: x + 0 -> x
            var x = new Wild("x");
            var zero = new Number(0);
            var pattern = new Add(ImmutableList.Create<IExpression>(x, zero));
            var replacement = x;
            var rule = new Rule(pattern, replacement);

            // Fact: A + 0
            var a = new Symbol("A");
            var factExpr = new Add(ImmutableList.Create<IExpression>(a, zero));
            
            // We need to add the fact to EGraph. Remember does this.
            hamm.Remember(factExpr);

            // Apply reasoning
            hamm.Reason(new[] { rule });

            // Now, A+0 should be equivalent to A.
            // If we simplify A+0, we should get A (assuming A is cheaper than A+0).
            var simplified = hamm.Simplify(factExpr);
            
            Assert.IsTrue(simplified.InternalEquals(a), $"Expected A, got {simplified.ToDisplayString()}");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestConcurrency()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            var tasks = new List<Task>();
            int threadCount = 10;
            int iterations = 100;

            for (int i = 0; i < threadCount; i++)
            {
                int id = i;
                tasks.Add(Task.Run(() =>
                {
                    var r = new Random(id);
                    for (int j = 0; j < iterations; j++)
                    {
                        var sym = new Symbol($"Sym_{r.Next(100)}");
                        // Write
                        hamm.Remember(sym, certainty: 0.9);
                        
                        // Read
                        var facts = hamm.Store.GetFacts().ToList();
                        
                        // Complex Read
                        if (j % 10 == 0)
                        {
                            hamm.RecallEquivalent(sym);
                        }
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsTrue(hamm.Store.GetFacts().Count() > 0);
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestConditionalReasoning()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            // Rule: x -> x_is_big IF x > 10 (numerically)
            // Note: Since EGraph reasoning works on IDs, we will need to extract the value to check the condition.
            
            var x = new Wild("x");
            var bigMarker = new Symbol("Big");
            
            // Inverse(x) -> Inverse(Big) if x > 10.
            
            var invX = new Inverse(x);
            var invBig = new Inverse(bigMarker); // Just a marker
            
            var rule = new Rule(invX, invBig, condition: bindings => 
            {
                if (bindings.TryGetValue("x", out var val) && val is Number n)
                {
                    return n.Value > 10;
                }
                return false;
            });

            // Fact 1: Inverse(20). 20 > 10. Should trigger.
            var fact1 = new Inverse(new Number(20));
            hamm.Remember(fact1);

            // Fact 2: Inverse(5). 5 < 10. Should NOT trigger.
            var fact2 = new Inverse(new Number(5));
            hamm.Remember(fact2);

            hamm.Reason(new[] { rule });

            // Check if Inverse(20) is equivalent to Inverse(Big)
            // We can check by seeing if Big is in the same class as 20? No, Inverse(Big) ~ Inverse(20).
            // So Inverse(Big) should simplify to Inverse(Big) or Inverse(20) depending on cost.
            // Let's just check if we can find Inverse(Big) when querying Inverse(20).
            
            var eq1 = hamm.RecallEquivalent(fact1).ToList();
            // EGraph Instantiator instantiates replacement.
            // Replacement is Inverse(Big).
            // Union(Inverse(20), Inverse(Big)).
            // So they are in same class.
            // RecallEquivalent(fact1) should find fact1.
            // But we want to know if the equivalence exists.
            
            // Better check: Simplify(Inverse(20)). If it returns Inverse(Big), it worked (if cost is lower).
            // Number(20) cost is 1. Symbol(Big) cost is 10.
            // Inverse(20) cost 100+1. Inverse(Big) cost 100+10.
            // So Inverse(20) is cheaper. Simplify won't show it.
            
            // Let's use equality check.
            var queryBig = new Inverse(bigMarker);
            var results = hamm.RecallEquivalent(queryBig).ToList(); 
            // Should contain fact1 (Inverse(20)) if merged.
            
            Assert.IsTrue(results.Any(r => r.InternalEquals(fact1)), "Inverse(20) should trigger rule and match Inverse(Big).");
            
            // Check fact2
            var results2 = hamm.RecallEquivalent(new Inverse(bigMarker)).ToList();
            Assert.IsFalse(results2.Any(r => r.InternalEquals(fact2)), "Inverse(5) should NOT trigger rule.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestReActWrappers()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            var t = new Symbol("Thought");
            var a = new Symbol("Action");
            var o = new Symbol("Observation");

            hamm.Think(t);
            hamm.Act(a);
            hamm.Observe(o);

            var thoughts = hamm.Recall(t, typeFilter: ReActType.Thought).ToList();
            var actions = hamm.Recall(a, typeFilter: ReActType.Action).ToList();
            var observations = hamm.Recall(o, typeFilter: ReActType.Observation).ToList();

            Assert.AreEqual(1, thoughts.Count);
            Assert.AreEqual(1, actions.Count);
            Assert.AreEqual(1, observations.Count);
            
            // Check cross filtering
            var noActions = hamm.Recall(a, typeFilter: ReActType.Thought).ToList();
            Assert.AreEqual(0, noActions.Count);
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestDeepScopes()
        {
            using var hamm = new HeuristicAssociativeMemoryModel();
            hamm.SetParentScope("Level2", "Level1");
            hamm.SetParentScope("Level1", "Global");

            hamm.Remember(new Symbol("G"), scope: "Global");
            hamm.Remember(new Symbol("L1"), scope: "Level1");
            hamm.Remember(new Symbol("L2"), scope: "Level2");

            hamm.CurrentScope = "Level2";
            var facts = hamm.Store.GetFacts("Level2").Select(f => ((Symbol)f.Expression).Name).ToHashSet();

            Assert.IsTrue(facts.Contains("G"), "Should inherit Global");
            Assert.IsTrue(facts.Contains("L1"), "Should inherit Level1");
            Assert.IsTrue(facts.Contains("L2"), "Should have Level2");

            hamm.CurrentScope = "Level1";
            var facts1 = hamm.Store.GetFacts("Level1").Select(f => ((Symbol)f.Expression).Name).ToHashSet();
            Assert.IsTrue(facts1.Contains("G"));
            Assert.IsTrue(facts1.Contains("L1"));
            Assert.IsFalse(facts1.Contains("L2"), "Should NOT see child scope Level2");
        }
    }
}
