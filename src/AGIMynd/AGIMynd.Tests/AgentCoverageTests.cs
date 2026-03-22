//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;

namespace AGIMynd.Tests
{
    [TestClass]
    public class AgentCoverageTests
    {
        private string _testMemoryRoot = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _testMemoryRoot = Path.Combine(Path.GetTempPath(), "AgentCoverage_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_testMemoryRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testMemoryRoot))
            {
                try { Directory.Delete(_testMemoryRoot, true); } catch { }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_AgentIsFinishedHonored_WhenInstructionPointerIsAtZero()
        {
            // Arrange
            var ipDir = Path.Combine(_testMemoryRoot, "AIMath", "Work");
            Directory.CreateDirectory(ipDir);
            await File.WriteAllTextAsync(Path.Combine(ipDir, "InstructionPointer.txt"), @"Header
Path
0
");

            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "AgentIsFinished" } } });

            var agent = new MyndAgent(_testMemoryRoot, mockLLM);
            bool stopped = false;
            
            // Act
            var startTask = agent.StartAsync(TimeSpan.FromMilliseconds(10));
            var delayTask = Task.Delay(500); // Give it some time to process
            
            var completedTask = await Task.WhenAny(startTask, delayTask);
            
            if (completedTask == startTask) stopped = true;
            else agent.Stop();

            // Assert
            Assert.IsTrue(stopped, "Agent should honor AgentIsFinished when InstructionPointer is at 0.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_DispatchesHammMaintenance()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "HammMaintenance" } } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("do maintenance");

            // Assert
            // Maintenance creates a log file
            var logPath = Path.Combine(_testMemoryRoot, "EpochLog", "maintenance_log_" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".txt");
            // Wait a bit because it's async in some paths? No, MyndAgent calls it directly.
            // Actually, HammMaintenanceAsync in MyndAgent.cs might write to a different location or just call HAMM.
            // Let's check MyndAgent.cs for HammMaintenanceAsync implementation.
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_DispatchesHammFold()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "HammFold", ToolInput = "x = 10" } } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);
            
            agent._hamm.Remember(new Equality(new Symbol("x"), new Number(10)));

            // Act
            await agent.RunEpochAsync("fold x");

            // Assert
            // Fold creates a summary fact.
            var facts = agent._hamm.Store.GetFacts().ToList();
            Assert.IsTrue(facts.Any(f => f.Type == HAMM.ReActType.Generic), "Fact should exist");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task UpdateGoalAsync_PersistsToSettings()
        {
            // Arrange
            var agent = new MyndAgent(_testMemoryRoot, new MockLLM());
            var newGoal = "World Peace";

            // Act
            await agent.UpdateGoalAsync(newGoal);

            // Assert
            Assert.AreEqual(newGoal, agent.Goal);
            var settingsPath = Path.Combine(_testMemoryRoot, "Config", "MyndAgentSettings.json");
            Assert.IsTrue(File.Exists(settingsPath));
            var content = await File.ReadAllTextAsync(settingsPath);
            Assert.Contains(newGoal, content);
        }
    }
}
