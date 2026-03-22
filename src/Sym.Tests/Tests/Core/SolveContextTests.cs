//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Atoms;
using System.Collections.Immutable;
using System.Collections.Generic; // For Dictionary

namespace SymTest
{
    [TestClass]
    public sealed class SolveContextTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Constructor_InitializesAllPropertiesCorrectly()
        {
            // Setup
            Symbol expectedTargetVariable = new Symbol("x");
            ImmutableList<Rule> expectedRules = ImmutableList.Create<Rule>(
                new Rule(new Wild("a"), new Wild("b"))
            );
            int expectedMaxIterations = 200;
            bool expectedEnableTracing = true;
            ImmutableDictionary<string, object> expectedAdditionalData = ImmutableDictionary.Create<string, object>()
                .Add("key1", "value1")
                .Add("key2", 123);

            // Act
            SolveContext context = new SolveContext(
                targetVariable: expectedTargetVariable,
                rules: expectedRules,
                maxIterations: expectedMaxIterations,
                enableTracing: expectedEnableTracing,
                additionalData: expectedAdditionalData
            );

            // Assert
            Assert.AreEqual(expectedTargetVariable, context.TargetVariable);
            CollectionAssert.AreEqual(expectedRules.ToArray(), context.Rules.ToArray());
            Assert.AreEqual(expectedMaxIterations, context.MaxIterations);
            Assert.AreEqual(expectedEnableTracing, context.EnableTracing);

            Assert.IsNotNull(context.AdditionalData);
            Assert.AreEqual(expectedAdditionalData.Count, context.AdditionalData.Count);
            foreach (KeyValuePair<string, object> entry in expectedAdditionalData)
            {
                Assert.IsTrue(context.AdditionalData.ContainsKey(entry.Key));
                Assert.AreEqual(entry.Value, context.AdditionalData[entry.Key]);
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_TargetVariable_CanBeNull()
        {
            SolveContext context = new SolveContext(targetVariable: null);
            Assert.IsNull(context.TargetVariable);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_TargetVariable_CanBeSymbol()
        {
            Symbol target = new Symbol("y");
            SolveContext context = new SolveContext(targetVariable: target);
            Assert.AreEqual(target, context.TargetVariable);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_Rules_DefaultsToEmptyListIfNull()
        {
            SolveContext context = new SolveContext(rules: null);
            Assert.IsNotNull(context.Rules);
            Assert.IsTrue(context.Rules.IsEmpty);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_Rules_AcceptsEmptyList()
        {
            ImmutableList<Rule> emptyRules = ImmutableList<Rule>.Empty;
            SolveContext context = new SolveContext(rules: emptyRules);
            Assert.IsNotNull(context.Rules);
            Assert.IsTrue(context.Rules.IsEmpty);
            Assert.AreEqual(0, context.Rules.Count);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_Rules_AcceptsMultipleRules()
        {
            ImmutableList<Rule> rules = ImmutableList.Create(
                new Rule(new Wild("a"), new Symbol("one")),
                new Rule(new Wild("b"), new Symbol("two"))
            );
            SolveContext context = new SolveContext(rules: rules);
            Assert.IsNotNull(context.Rules);
            Assert.AreEqual(2, context.Rules.Count);
            CollectionAssert.AreEqual(rules.ToArray(), context.Rules.ToArray());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_MaxIterations_DefaultsTo100()
        {
            SolveContext context = new SolveContext();
            Assert.AreEqual(100, context.MaxIterations);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_MaxIterations_CanBeOverridden()
        {
            SolveContext context = new SolveContext(maxIterations: 50);
            Assert.AreEqual(50, context.MaxIterations);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_EnableTracing_DefaultsToFalse()
        {
            SolveContext context = new SolveContext();
            Assert.IsFalse(context.EnableTracing);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_EnableTracing_CanBeOverridden()
        {
            SolveContext context = new SolveContext(enableTracing: true);
            Assert.IsTrue(context.EnableTracing);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_AdditionalData_CanBeNull()
        {
            SolveContext context = new SolveContext(additionalData: null);
            Assert.IsNull(context.AdditionalData);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Constructor_AdditionalData_CanBePopulated()
        {
            ImmutableDictionary<string, object> data = ImmutableDictionary.Create<string, object>()
                .Add("testKey", "testValue");
            SolveContext context = new SolveContext(additionalData: data);
            Assert.IsNotNull(context.AdditionalData);
            Assert.AreEqual(1, context.AdditionalData.Count);
            Assert.AreEqual("testValue", context.AdditionalData["testKey"]);
            Assert.AreSame(data, context.AdditionalData); // Ensure reference equality for immutability
        }

        [TestMethod]
        [Timeout(10000)]
        public void Immutability_PropertiesRemainStableWhenDerivedCollectionsChange()
        {
            var baseRules = ImmutableList.Create<Rule>(new Rule(new Wild("a"), new Symbol("a")));
            var baseData = ImmutableDictionary.Create<string, object>().Add("key", "value");
            var context = new SolveContext(rules: baseRules, additionalData: baseData);

            var appendedRules = context.Rules.Add(new Rule(new Wild("b"), new Symbol("b")));
            var appendedData = context.AdditionalData!.Add("extra", 123);

            Assert.AreEqual(baseRules.Count, context.Rules.Count, "Context rules should not change when derived copies mutate.");
            Assert.AreEqual(baseData.Count, context.AdditionalData!.Count, "Context additional data should remain unchanged.");
            Assert.AreEqual(baseRules.Count + 1, appendedRules.Count, "Derived rules list should reflect the appended rule.");
            Assert.AreEqual(baseData.Count + 1, appendedData.Count, "Derived additional data should reflect the appended entry.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void EdgeCases_CombinationOfParameters()
        {
            Symbol target = new Symbol("z");
            ImmutableList<Rule> rules = ImmutableList.Create(new Rule(new Wild("a"), new Wild("a")));
            ImmutableDictionary<string, object> data = ImmutableDictionary.Create<string, object>().Add("debug", true);

            // Test case 1: Only target variable provided
            SolveContext context1 = new SolveContext(targetVariable: target);
            Assert.AreEqual(target, context1.TargetVariable);
            Assert.IsTrue(context1.Rules.IsEmpty);
            Assert.AreEqual(100, context1.MaxIterations);
            Assert.IsFalse(context1.EnableTracing);
            Assert.IsNull(context1.AdditionalData);

            // Test case 2: Rules and enable tracing
            SolveContext context2 = new SolveContext(rules: rules, enableTracing: true);
            Assert.IsNull(context2.TargetVariable);
            CollectionAssert.AreEqual(rules.ToArray(), context2.Rules.ToArray());
            Assert.AreEqual(100, context2.MaxIterations);
            Assert.IsTrue(context2.EnableTracing);
            Assert.IsNull(context2.AdditionalData);

            // Test case 3: Max Iterations and additional data
            SolveContext context3 = new SolveContext(maxIterations: 500, additionalData: data);
            Assert.IsNull(context3.TargetVariable);
            Assert.IsTrue(context3.Rules.IsEmpty);
            Assert.AreEqual(500, context3.MaxIterations);
            Assert.IsFalse(context3.EnableTracing);
            Assert.IsNotNull(context3.AdditionalData);
            Assert.AreEqual(data["debug"], context3.AdditionalData["debug"]);

            // Test case 4: All defaults (covered implicitly by other specialized tests, but explicit for completeness)
            SolveContext context4 = new SolveContext();
            Assert.IsNull(context4.TargetVariable);
            Assert.IsTrue(context4.Rules.IsEmpty);
            Assert.AreEqual(100, context4.MaxIterations);
            Assert.IsFalse(context4.EnableTracing);
            Assert.IsNull(context4.AdditionalData);
        }
    }
}

