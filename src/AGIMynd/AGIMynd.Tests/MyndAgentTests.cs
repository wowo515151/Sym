//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;

namespace AGIMynd.Tests
{
    public class MockToolRunner : IToolRunner
    {
        public List<(string Tool, string Args)> Calls = new();
        public Func<string, string, string> Handler = (_, _) => "ok";

        public Task<string> RunToolAsync(string toolName, string args)
        {
            Calls.Add((toolName, args));
            return Task.FromResult(Handler(toolName, args));
        }
    }

    [TestClass]
    public class MyndAgentTests
    {
        public TestContext? TestContext { get; set; }
        private string _testMemoryRoot = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _testMemoryRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_PromptIncludesToolBufferSections()
        {
            // Arrange
            var mockLLM = new MockLLM();
            
            // First epoch: run a tool to populate history
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "powershell", ToolInput = "echo history" } } });
            
            var runner = new MockToolRunner();
            runner.Handler = (_,_) => "HISTORY_OUTPUT";

            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);
            await agent.RunEpochAsync("prime history");

            // Second epoch: check prompt
            string? capturedPrompt = null;
            mockLLM.ResponseFunc = (prompt) =>
            {
                capturedPrompt = prompt;
                return Common.ToXml(new ToolCommandList());
            };

            // Act
            await agent.RunEpochAsync("test prompt sections");

            // Assert
            Assert.IsNotNull(capturedPrompt);
            Assert.Contains("<FromTools>", capturedPrompt);
            Assert.Contains("HISTORY_OUTPUT", capturedPrompt);
            
            // SavedFromTools checked via file
            var savedDir = Path.Combine(_testMemoryRoot, "SavedFromTools");
            Directory.CreateDirectory(savedDir);
            await File.WriteAllTextAsync(Path.Combine(savedDir, "saved.txt"), "saved content");
            
            // Re-run to pick up saved file
             mockLLM.ResponseFunc = (prompt) =>
            {
                capturedPrompt = prompt;
                return Common.ToXml(new ToolCommandList());
            };
            await agent.RunEpochAsync("check saved");
            
            Assert.Contains("<SavedFromTools>", capturedPrompt);
            Assert.Contains("saved.txt", capturedPrompt);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task StartAsync_AgentIsFinishedIgnored_WhenAIMathInstructionPointerNotDone()
        {
            // Arrange
            Directory.CreateDirectory(Path.Combine(_testMemoryRoot, "AIMath", "Work"));
            await File.WriteAllTextAsync(
                Path.Combine(_testMemoryRoot, "AIMath", "Work", "InstructionPointer.txt"),
                "This is the InstructionPointer\nAIMath/AIMathematician/AIMathematicianProcedure.txt\n1\n");

            int calls = 0;
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) =>
            {
                if (prompt.Contains("Generate 5-10 concise semantic tags")) return "";
                calls++;
                return Common.ToXml(new ToolCommandList
                {
                    Commands =
                    {
                        new ToolCommand { ToolName = "AgentIsFinished" }
                    }
                });
            };

            var agent = new MyndAgent(_testMemoryRoot, mockLLM)
            {
                MaxEpochs = 2
            };

            // Act
            await agent.StartAsync(delay: TimeSpan.Zero);

            // Assert
            Assert.AreEqual(2, calls, "Agent should not stop early when AIMath InstructionPointer is not at instruction 0.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_DispatchesAllCommandsInResponse()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) =>
            {
                return Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "powershell", ToolInput = "echo one" }, new ToolCommand { ToolName = "git", ToolInput = "status" } } });
            };

            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);

            // Act
            await agent.RunEpochAsync("dispatch multiple");

            // Assert
            Assert.HasCount(2, runner.Calls);
            Assert.AreEqual("powershell", runner.Calls[0].Tool);
            Assert.AreEqual("echo one", runner.Calls[0].Args);
            Assert.AreEqual("git", runner.Calls[1].Tool);
            Assert.AreEqual("status", runner.Calls[1].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_DispatchesHammRemember()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList 
            { 
                Commands = { new ToolCommand { ToolName = "HammRemember", ToolInput = "x = 10", Path = "TestScope" } } 
            });

            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("remember x");

            // Assert
            var facts = agent._hamm.Store.GetFacts("TestScope").ToList();
            Assert.IsTrue(facts.Any(f => f.Expression.ToDisplayString().Contains("x = 10")), "Fact should be remembered in HAMM");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testMemoryRoot))
            {
                try { Directory.Delete(_testMemoryRoot, true); } catch {}
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_CreatesFiles()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "CreateConcept", ToolInput = "Hello World" } } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            var before = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            var after = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);
            var created = after.Except(before).ToList();
            Assert.HasCount(1, created);
            Assert.AreEqual("Hello World", await File.ReadAllTextAsync(created[0]));
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_CreateConcept_UsesPathAndToolInput()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList
            {
                Commands =
                {
                    new ToolCommand
                    {
                        ToolName = "CreateConcept",
                        Path = "concepts/user.txt",
                        ToolInput = "line1\nline2"
                    }
                }
            });

            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            var createdPath = Path.Combine(_testMemoryRoot, "concepts", "user.txt");
            Assert.IsTrue(File.Exists(createdPath), "CreateConcept should create the file at the provided Path");
            Assert.AreEqual("line1\nline2", await File.ReadAllTextAsync(createdPath));
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_DeletesFiles()
        {
            // Arrange
            Directory.CreateDirectory(_testMemoryRoot);
            string filePath = Path.Combine(_testMemoryRoot, "to_delete.txt");
            await File.WriteAllTextAsync(filePath, "delete me");

            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "DeleteConcept", Path = "to_delete.txt" } } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            Assert.IsFalse(File.Exists(filePath));
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_DeleteConcept_UsesPath_ToolInputIgnored()
        {
            // Arrange
            Directory.CreateDirectory(_testMemoryRoot);
            var filePath = Path.Combine(_testMemoryRoot, "to_delete_with_input.txt");
            await File.WriteAllTextAsync(filePath, "delete me");

            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList
            {
                Commands =
                {
                    new ToolCommand
                    {
                        ToolName = "DeleteConcept",
                        Path = "to_delete_with_input.txt",
                        ToolInput = "SHOULD_BE_EMPTY_BUT_IGNORED"
                    }
                }
            });

            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            Assert.IsFalse(File.Exists(filePath), "DeleteConcept should delete the file at the provided Path even if ToolInput is set");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_CannotDeletePinnedFiles()
        {
            // Arrange
            string pinnedDir = Path.Combine(_testMemoryRoot, "Pinned");
            Directory.CreateDirectory(pinnedDir);
            string pinnedFile = Path.Combine(pinnedDir, "safe.txt");
            await File.WriteAllTextAsync(pinnedFile, "cannot delete me");

            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "DeleteConcept", Path = "Pinned/safe.txt" } } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            Assert.IsTrue(File.Exists(pinnedFile));
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_RespectsMaxDeletionsPerEpoch()
        {
            // Arrange
            Directory.CreateDirectory(_testMemoryRoot);
            for (int i = 0; i < 5; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(_testMemoryRoot, $"file{i}.txt"), "content");
            }

            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = {
                new ToolCommand { ToolName = "DeleteConcept", Path = "file0.txt" },
                new ToolCommand { ToolName = "DeleteConcept", Path = "file1.txt" },
                new ToolCommand { ToolName = "DeleteConcept", Path = "file2.txt" },
                new ToolCommand { ToolName = "DeleteConcept", Path = "file3.txt" },
                new ToolCommand { ToolName = "DeleteConcept", Path = "file4.txt" }
            } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);
            agent.MaxDeletionsPerEpoch = 2;

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            var files = Directory.GetFiles(_testMemoryRoot, "file*.txt");
            Assert.HasCount(3, files); // 5 - 2 = 3
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_LogsEpoch()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList());
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("test log");

            // Assert
            string logDir = Path.Combine(_testMemoryRoot, "EpochLog");
            Assert.IsTrue(Directory.Exists(logDir));
            var logFiles = Directory.GetFiles(logDir, "epoch_*.xml");
            Assert.IsGreaterThanOrEqualTo(1, logFiles.Length, "Expected at least one epoch log file");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task StartAsync_ReloadsGoalAndStops()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            await agent.UpdateGoalAsync("Initial Goal");

            int callCount = 0;
            string? lastGoal = null;
            mockLLM.ResponseFunc = (prompt) => 
            {
                callCount++;
                if (prompt.Contains("Initial Goal")) lastGoal = "Initial Goal";
                if (prompt.Contains("Updated Goal")) lastGoal = "Updated Goal";
                return Common.ToXml(new ToolCommandList());
            };

            // Act
            var startTask = agent.StartAsync(TimeSpan.FromMilliseconds(10));

            await Task.Delay(100);
            await agent.UpdateGoalAsync("Updated Goal");
            await Task.Delay(200);

            agent.Stop();
            await startTask;

            // Assert
            Assert.IsGreaterThanOrEqualTo(2, callCount);
            Assert.AreEqual("Updated Goal", lastGoal);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_CannotCreateFilesInPinned()
        {
            // Arrange
            string pinnedDir = Path.Combine(_testMemoryRoot, "Pinned");
            Directory.CreateDirectory(pinnedDir);

            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "CreateConcept", ToolInput = "I should not be able to do this" } } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            // CreateConcept always writes to the memory root (never under Pinned).
            var pinnedFiles = Directory.GetFiles(pinnedDir, "*", SearchOption.AllDirectories);
            Assert.HasCount(0, pinnedFiles);

            var rootTxt = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);
            Assert.IsGreaterThanOrEqualTo(1, rootTxt.Length, "CreateConcept should create a .txt file in memory root");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_LogsDetailedEpoch()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var responseXml = Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "CreateConcept", ToolInput = "data" } } });
            mockLLM.ResponseFunc = (prompt) => responseXml;
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("verify logging");

            // Assert
            string logDir = Path.Combine(_testMemoryRoot, "EpochLog");
            var logFiles = Directory.GetFiles(logDir, "epoch_*.xml");
            Assert.IsGreaterThanOrEqualTo(1, logFiles.Length, "Expected at least one epoch log file");

            string logContent = await File.ReadAllTextAsync(logFiles[0]);
            Assert.Contains("verify logging", logContent);
            Assert.Contains("CreateConcept", logContent);
            Assert.Contains("data", logContent);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_ExtractsFirstXmlBlock()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) =>
                "Here is some text before " + Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "CreateConcept", ToolInput = "found" } } }) + " and some text after.";
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            var before = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);

            // Act
            await agent.RunEpochAsync("test block extraction");

            // Assert
            var after = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);
            var created = after.Except(before).ToList();
            Assert.HasCount(1, created);
            Assert.AreEqual("found", await File.ReadAllTextAsync(created[0]));
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_PreventsPathTraversal()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "DeleteConcept", Path = "../traversal.txt" } } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Create a uniquely named file outside the memory root; a traversal attack must not be able to delete it.
            string parent = Path.GetDirectoryName(_testMemoryRoot)!;
            string traversalFile = Path.Combine(parent, $"traversal_{Guid.NewGuid():N}.txt");
            // Write with shared read/write to avoid external locking on CI machines
            using (var fs = new FileStream(traversalFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var w = new StreamWriter(fs))
            {
                await w.WriteAsync("do not delete");
            }
            Assert.IsTrue(File.Exists(traversalFile), "Test setup failure: traversal target file was not created");

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            Assert.IsTrue(File.Exists(traversalFile), "Should prevent deletion outside memory root");

            try { File.Delete(traversalFile); } catch { }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_ExtractsXmlFromChatter()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) =>
            {
                // Surround a valid XML block with extra text to simulate LLM chatter
                return "Certainly! Here is the XML: " + Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "CreateConcept", ToolInput = "fixed" } } }) + " Hope this helps!";
            };
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            var before = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            var after = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);
            var created = after.Except(before).ToList();
            Assert.HasCount(1, created);
            Assert.AreEqual("fixed", await File.ReadAllTextAsync(created[0]));
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task StartAsync_ThrowsIfAlreadyRunning()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList());
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);
            await agent.UpdateGoalAsync("goal");

            // Act & Assert
            var task1 = agent.StartAsync(TimeSpan.FromSeconds(1));
            try
            {
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => 
                    agent.StartAsync(TimeSpan.FromSeconds(1)));
            }
            finally
            {
                agent.Stop();
                await task1;
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_CreatesNestedDirectories()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => Common.ToXml(new ToolCommandList { Commands = { new ToolCommand { ToolName = "CreateConcept", ToolInput = "nested" } } });
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            var before = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            Assert.IsFalse(Directory.Exists(Path.Combine(_testMemoryRoot, "deep")), "CreateConcept should not create nested directories");

            var after = Directory.GetFiles(_testMemoryRoot, "*.txt", SearchOption.TopDirectoryOnly);
            var created = after.Except(before).ToList();
            Assert.HasCount(1, created);
            Assert.AreEqual("nested", await File.ReadAllTextAsync(created[0]));
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_HandlesGarbageResponse()
        {
            // Arrange
            var mockLLM = new MockLLM();
            mockLLM.ResponseFunc = (prompt) => "This is not XML at all and should not crash the agent.";
            var agent = new MyndAgent(_testMemoryRoot, mockLLM);

            // Act
            await agent.RunEpochAsync("test goal");

            // Assert
            // Should not throw, and no files should be created/deleted
            var files = Directory.GetFiles(_testMemoryRoot, "*.*", SearchOption.AllDirectories);
            // Only Pinned and EpochLog directories should exist, plus any created by constructor
            // But no new concept files.
            foreach(var file in files)
            {
                var rel = Path.GetRelativePath(_testMemoryRoot, file);
                Assert.IsTrue(rel.StartsWith("Pinned") || rel.StartsWith("EpochLog") || rel.StartsWith("SavedFromTools") || rel.Contains("HAMM.ops.jsonl"), $"Unexpected file: {rel}");
            }
        }
    }
}
