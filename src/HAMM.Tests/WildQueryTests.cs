// Copyright Warren Harding 2026
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HAMM;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;

namespace HAMM.Tests
{
    [TestClass]
    public class WildQueryTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void QueryV2_WithWildcards_FindsMatchingFacts()
        {
            // Arrange
            var store = new MemoryStore();
            var factExpr = new Equality(new Symbol("Status"), new Symbol("Ready"));
            store.AddFact(factExpr, scope: "Global");

            var queryExpr = new Equality(new Symbol("Status"), new Wild("?val"));

            // Act
            var results = store.QueryV2(queryExpr, new QueryOptions { Scope = "Global" }).ToList();

            // Assert
            Assert.AreEqual(1, results.Count, "Should find the matching equality fact.");
            Assert.AreEqual("(Status = Ready)", results[0].Expression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void QueryV2_WithWildcards_FindsMatchingFunctions()
        {
            // Arrange
            var store = new MemoryStore();
            var factExpr = new Function("Procedure", new Symbol("Test"), new Number(1));
            store.AddFact(factExpr, scope: "Global");

            var queryExpr = new Function("Procedure", new Wild("?name"), new Wild("?step"));

            // Act
            var results = store.QueryV2(queryExpr, new QueryOptions { Scope = "Global", MinQualityScore = 0.0 }).ToList();

            // Assert
            Assert.AreEqual(1, results.Count, "Should find the matching function fact.");
            Assert.AreEqual("Procedure(Test, 1)", results[0].Expression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void QueryV2_NestedPattern_MatchesCorrectly()
        {
            // Arrange
            var store = new MemoryStore();
            var procList = new Function("List", 
                new Function("Cmd", new Symbol("tool1"), new Symbol("input1")),
                new Function("Cmd", new Symbol("tool2"), new Symbol("input2"))
            );
            var factExpr = new Function("Procedure", new Symbol("StepByStep"), procList);
            store.AddFact(factExpr, scope: "Global");

            // Query for a Procedure that has a List as its second argument
            var queryExpr = new Function("Procedure", new Wild("?name"), new Function("List", new Wild("?cmd1"), new Wild("?cmd2")));

            // Act
            var results = store.QueryV2(queryExpr, new QueryOptions { Scope = "Global", MinQualityScore = 0.0 }).ToList();

            // Assert
            Assert.AreEqual(1, results.Count);
            var resultFunc = results[0].Expression as Function;
            Assert.IsNotNull(resultFunc);
            var nameSym = resultFunc.Arguments[0] as Symbol;
            Assert.IsNotNull(nameSym);
            Assert.AreEqual("StepByStep", nameSym.Name);
        }

        [TestMethod]
        [Timeout(10000)]
        public void QueryV2_MultipleWildcardsSameName_EnforcesEquality()
        {
            // Arrange
            var store = new MemoryStore();
            // Fact: Brother(John, John) - JOHN is his own brother (loop)
            store.AddFact(new Function("Brother", new Symbol("John"), new Symbol("John")), scope: "Global");
            // Fact: Brother(John, Mike)
            store.AddFact(new Function("Brother", new Symbol("John"), new Symbol("Mike")), scope: "Global");

            // Query: Brother(?x, ?x) - find all people who are their own brothers
            var queryExpr = new Function("Brother", new Wild("?x"), new Wild("?x"));

            // Act
            var results = store.QueryV2(queryExpr, new QueryOptions { Scope = "Global", MinQualityScore = 0.0 }).ToList();

            // Assert
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Brother(John, John)", results[0].Expression.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void QueryV2_ActivePointerMultiWild_MatchesCorrectly()
        {
            // Arrange
            var store = new MemoryStore();
            store.AddFact(new Function("ActivePointer", new Symbol("Test"), new Number(0)), scope: "Global");

            // ActivePointer is classified as a directive and auto-routed to Goals.
            Assert.AreEqual(1, store.GetFacts("Goals").Count(f => f.Expression.ToDisplayString() == "ActivePointer(Test, 0)"));

            var queryExpr = new Function("ActivePointer", new Wild("?p"), new Wild("?s"));

            // Act
            var results = store.QueryV2(queryExpr, new QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();

            // Assert
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        [Timeout(10000)]
        public void QueryV2_WithZero_MatchesCorrectly()
        {
            // Arrange
            var store = new MemoryStore();
            // Important for Instruction Pointer which often starts at 0
            store.AddFact(new Function("ActivePointer", new Symbol("Test"), new Number(0)), scope: "Global");

            // ActivePointer is classified as a directive and auto-routed to Goals.
            Assert.AreEqual(1, store.GetFacts("Goals").Count(f => f.Expression.ToDisplayString() == "ActivePointer(Test, 0)"));

            var queryExpr = new Function("ActivePointer", new Wild("?p"), new Number(0));

            // Act
            var results = store.QueryV2(queryExpr, new QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();

            // Assert
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        [Timeout(10000)]
        public void QueryV2_SequentialAdditions_MatchesCorrectly()
        {
            // Arrange
            var store = new MemoryStore();
            store.AddFact(new Function("Proc", new Symbol("A")), scope: "Global");
            
            // First query to trigger graph build
            var results1 = store.QueryV2(new Function("Proc", new Wild("?x")), new QueryOptions { Scope = "Global", MinQualityScore = 0.0 }).ToList();
            Assert.AreEqual(1, results1.Count);

            // Add another fact
            store.AddFact(new Function("Proc", new Symbol("B")), scope: "Global");

            // Second query should find both
            var results2 = store.QueryV2(new Function("Proc", new Wild("?x")), new QueryOptions { Scope = "Global", MinQualityScore = 0.0 }).ToList();
            
            // Assert
            Assert.AreEqual(2, results2.Count);
        }
    }
}
