// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HAMM;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace HAMM.Tests
{
    [TestClass]
    public class HAMMTests
    {
        private const int TestTimeout = 10000;

        [TestMethod, Timeout(TestTimeout)]
        public void TestBasicRememberAndRecall()
        {
            var hamm = new HeuristicAssociativeMemoryModel();
            var factExpr = new Equality(new Symbol("A"), new Number(1));
            hamm.Remember(factExpr);

            var recalled = hamm.Recall(new Symbol("A")).ToList();
            Assert.AreEqual(1, recalled.Count);
            Assert.IsTrue(recalled[0].InternalEquals(factExpr));
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestRecallDiversity()
        {
            var hamm = new HeuristicAssociativeMemoryModel();
            hamm.Remember(new Equality(new Symbol("A"), new Number(1)));
            hamm.Remember(new Equality(new Symbol("A"), new Number(2))); // Redundant

            var recalled = hamm.Recall(new Symbol("A"), diversityWeight: 1.0, tokenLimit: 100).ToList();
            Assert.AreEqual(1, recalled.Count, "Diversity penalty should have limited redundant facts.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestCertaintyDecayStability()
        {
            var hamm = new HeuristicAssociativeMemoryModel();
            var factExpr = new Symbol("A");
            hamm.Remember(factExpr, certainty: 1.0);
            
            var fact = hamm.Store.GetFacts().First();
            fact.CreatedAt = DateTime.UtcNow.AddHours(-1);
            fact.LastDecayUpdate = fact.CreatedAt;

            hamm.Maintenance();
            double cert1 = fact.Certainty;
            
            hamm.Maintenance();
            double cert2 = fact.Certainty;

            Assert.AreEqual(cert1, cert2, 1e-7, "Certainty should not decay further without time passing.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestFolding()
        {
            var hamm = new HeuristicAssociativeMemoryModel();
            var a = new Symbol("A");
            var b = new Symbol("B");
            hamm.Remember(a);
            hamm.Remember(b);

            hamm.Fold(new[] { a, b });

            var active = hamm.Store.GetFacts().ToList();
            Assert.AreEqual(1, active.Count);
            Assert.IsTrue(active[0].Expression is Summary);
            
            var summary = (Summary)active[0].Expression;
            Assert.IsTrue(summary.Arguments.Any(arg => arg.InternalEquals(a)));
            Assert.IsTrue(summary.Arguments.Any(arg => arg.InternalEquals(b)));
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestInvalidationPropagation()
        {
            var hamm = new HeuristicAssociativeMemoryModel();
            var a = new Equality(new Symbol("A"), new Number(1));
            var b = new Equality(new Symbol("B"), new Symbol("A")); // B depends on A
            
            hamm.Remember(a, certainty: 0.5); // Lower certainty for a
            
            // Get the fact object
            var factA = hamm.Store.GetFacts().First(f => f.Expression.InternalEquals(a));
            hamm.Store.AddFact(b, dependencies: new[] { factA });

            // Contradict A with high certainty
            var a2 = new Equality(new Symbol("A"), new Number(2));
            hamm.Remember(a2, certainty: 1.0); // Should invalidate factA

            Assert.IsTrue(factA.IsInvalidated, "Fact A should be invalidated.");
            
            var active = hamm.Store.GetFacts().ToList();
            Assert.IsFalse(active.Any(f => f.Expression.InternalEquals(b)), "Fact B should be invalidated via propagation.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestBayesianUpdate()
        {
            var hamm = new HeuristicAssociativeMemoryModel();
            var a = new Symbol("A");
            hamm.Remember(a, certainty: 0.5);
            
            var fact = hamm.Store.GetFacts().First();
            
            hamm.Store.UpdateCertainty(fact, 0.9);
            Assert.IsTrue(fact.Certainty > 0.5, "Certainty should increase with supporting observation.");
            
            hamm.Store.UpdateCertainty(fact, 0.1);
            Assert.IsTrue(fact.Certainty < 0.9, "Certainty should decrease with contradicting observation.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestScopes()
        {
            var hamm = new HeuristicAssociativeMemoryModel();
            hamm.Remember(new Symbol("GlobalFact"), scope: "Global");
            hamm.Remember(new Symbol("LocalFact"), scope: "Local");

            hamm.CurrentScope = "Local";
            var recalledLocal = hamm.Recall(new Symbol("GlobalFact")).ToList();
            Assert.AreEqual(1, recalledLocal.Count, "Should see global facts from local scope.");

            var recalledLocal2 = hamm.Recall(new Symbol("LocalFact")).ToList();
            Assert.AreEqual(1, recalledLocal2.Count, "Should see local facts from local scope.");

            hamm.CurrentScope = "Other";
            var recalledOther = hamm.Recall(new Symbol("LocalFact")).ToList();
            Assert.AreEqual(0, recalledOther.Count, "Should NOT see local facts from other scope.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestSubscribers()
        {
            var hamm = new HeuristicAssociativeMemoryModel();
            // Subscriber listens ONLY to 'Local'. It should NOT get 'Secret'.
            var subscriber = new TestSubscriber("TestSub", new[] { "Local" });
            hamm.Subscribe(subscriber);

            hamm.Remember(new Symbol("A"), scope: "Global");
            // AddFact logic says: if sub listens to Global OR sub listens to scope, they get it.
            // Since sub doesn't listen to Global, but fact is Global... Wait.
            // In MemoryStore: if (sub.SubscribedScopes.Contains("Global") || sub.SubscribedScopes.Contains(scope))
            // sub.SubscribedScopes is ["Local"]. scope is "Global". Neither match.
            Assert.AreEqual(0, subscriber.AddedFacts.Count, "Subscriber NOT listening to Global should NOT receive Global facts.");

            hamm.Remember(new Symbol("B"), scope: "Local");
            Assert.AreEqual(1, subscriber.AddedFacts.Count, "Subscriber should receive facts in its scope.");

            hamm.Remember(new Symbol("C"), scope: "Secret");
            Assert.AreEqual(1, subscriber.AddedFacts.Count, "Subscriber should NOT receive facts in other scopes.");
            
            // Now add a subscriber that listens to Global
            var globalSub = new TestSubscriber("GlobalSub", new[] { "Global" });
            hamm.Subscribe(globalSub);
            hamm.Remember(new Symbol("D"), scope: "AnyScope");
            Assert.AreEqual(1, globalSub.AddedFacts.Count, "Subscriber listening to Global should receive everything.");
        }

        [TestMethod, Timeout(TestTimeout)]
        public void TestPhiSigmoid()
        {
            Assert.AreEqual(1.0, MemoryStore.PhiSigmoid(0.9), 1e-9);
            Assert.AreEqual(0.5, MemoryStore.PhiSigmoid(0.5), 1e-9);
            Assert.AreEqual(0.0, MemoryStore.PhiSigmoid(0.2), 1e-9);
        }

        private class TestSubscriber : IMemorySubscriber
        {
            public string Name { get; }
            public IEnumerable<string> SubscribedScopes { get; }
            public List<MemoryFact> AddedFacts { get; } = new List<MemoryFact>();
            public List<MemoryFact> InvalidatedFacts { get; } = new List<MemoryFact>();

            public TestSubscriber(string name, IEnumerable<string> scopes)
            {
                Name = name;
                SubscribedScopes = scopes;
            }

            public void OnFactAdded(MemoryFact fact) => AddedFacts.Add(fact);
            public void OnFactInvalidated(MemoryFact fact) => InvalidatedFacts.Add(fact);
        }
    }
}
