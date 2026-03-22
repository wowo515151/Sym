// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using HAMM;

namespace AGIMynd.Tests
{
    [TestClass]
    public class MyndHammIntegrationTests
    {
        private string _testMemoryRoot = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _testMemoryRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_testMemoryRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testMemoryRoot))
            {
                Directory.Delete(_testMemoryRoot, true);
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_IncludesHammFactsInPrompt()
        {
            // Arrange
            string? capturedPrompt = null;
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => 
            {
                capturedPrompt = prompt;
                return Common.ToXml(new ToolCommandList());
            };

            var agent = new MyndAgent(_testMemoryRoot, mockLLM);
            
            // Seed HAMM
            agent._hamm.Remember(new Equality(new Symbol("ImportantFact"), new Number(42)), certainty: 1.0);

            // Act
            await agent.RunEpochAsync("TestGoal");

            // Assert
            Assert.IsNotNull(capturedPrompt);
            Assert.Contains("ImportantFact", capturedPrompt);
            Assert.Contains("42", capturedPrompt);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_RecordsThoughtsAndActions()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var thought = "I should create a concept.";
            var toolCmd = new ToolCommand { ToolName = "CreateConcept", ToolInput = "Content", Path = "file.txt" };
            
            // LLM returns some text (thought) + XML (action)
            mockLLM.ResponseFunc = (prompt) => 
                $"{thought}\n{Common.ToXml(new ToolCommandList { Commands = { toolCmd } })}";

            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("TestGoal");

            // Assert
            // 1. Verify Thought
            // Recall thoughts.
            var thoughts = agent._hamm.Recall(new Symbol(thought), typeFilter: ReActType.Thought).ToList();
            // Note: Recall is fuzzy. If we search for exact string symbol, we might find it if tokenized properly?
            // MyndAgent does: _hamm.Think(new Symbol(llmResponse));
            // Symbol name is the full response string.
            // Recall(Symbol(response)) should find it.
            // Or iterate all thoughts.
            var allThoughts = agent._hamm.Store.GetAllFacts().Where(f => f.Type == ReActType.Thought).ToList();
            Assert.IsTrue(allThoughts.Any(f => f.Expression is Symbol s && s.Name.Contains(thought)), "HAMM should contain the thought.");

            // 2. Verify Action
            // MyndAgent does: _hamm.Act(new Symbol(cmd.ToolName + ":" + cmd.ToolInput));
            var expectedAction = "CreateConcept:Content";
            var allActions = agent._hamm.Store.GetAllFacts().Where(f => f.Type == ReActType.Action).ToList();
            Assert.IsTrue(allActions.Any(f => f.Expression is Symbol s && s.Name.Contains(expectedAction)), "HAMM should contain the action.");
        }
    }
}
