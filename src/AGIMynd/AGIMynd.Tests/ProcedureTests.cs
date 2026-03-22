//Copyright Warren Harding 2026.
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
    public class ProcedureTests
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
            if (Directory.Exists(_testMemoryRoot)) Directory.Delete(_testMemoryRoot, true);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task CreateProcedure_StoresProcedureAndExecutesAutonomously()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);
            
            // Simple procedure XML
            var procXml = @"<ToolCommandList><ToolCommand><ToolName>TargetTool</ToolName><ToolInput>TargetInput</ToolInput></ToolCommand></ToolCommandList>";

            // Act: Create Procedure via tool call simulation
            mockLLM.ResponseFunc = (p) => $@"<ToolCommandList><ToolCommand><ToolName>CreateProcedure</ToolName><Path>TestProc</Path><ToolInput><![CDATA[{procXml}]]></ToolInput></ToolCommand></ToolCommandList>";

            await agent.RunEpochAsync("test create proc");

            // Verify procedure fact exists in HAMM (direct check)
            var allFacts = agent._hamm.Store.GetFacts().ToList();
            Assert.IsTrue(allFacts.Any(f => f.Expression.ToDisplayString().Contains("Procedure")), "Procedure fact should exist in HAMM.");

            // Now run a second epoch. It should fetch and execute the first step of the procedure WITHOUT calling LLM.
            mockLLM.ResponseFunc = (p) => throw new Exception("LLM should not be called in second epoch because Procedure is active!");
            await agent.RunEpochAsync("run step 1");

            // Assert
            Assert.HasCount(1, runner.Calls, "Should have executed the procedure step.");
            Assert.AreEqual("TargetTool", runner.Calls[0].Tool);
            Assert.AreEqual("TargetInput", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task HammRememberAndQuery_ToolPipeline_Works()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);

            // Act 1: Remember a fact (using identifiers, not string literals, for simpler formatting)
            mockLLM.ResponseFunc = (p) => Common.ToXml(new ToolCommandList {
                Commands = new List<ToolCommand> {
                    new ToolCommand { ToolName = "HammRemember", ToolInput = "Equality(User, Admin)", Path = "Auth" }
                }
            });
            await agent.RunEpochAsync("remember user");

            // Act 2: Query the fact
            mockLLM.ResponseFunc = (p) => Common.ToXml(new ToolCommandList {
                Commands = new List<ToolCommand> {
                    new ToolCommand { ToolName = "HammQuery", ToolInput = "Equality(User, Wild(\"x\"))", Path = "Auth" }
                }
            });
            await agent.RunEpochAsync("query user");

            // Act 3: Capture prompt for next epoch
            string capturedPrompt = "";
            mockLLM.ResponseFunc = (p) => { capturedPrompt = p; return Common.ToXml(new ToolCommandList()); };
            await agent.RunEpochAsync("get results");

            // Assert
            StringAssert.Contains(capturedPrompt, "User = Admin", "Query results should be in the prompt.");
            StringAssert.Contains(capturedPrompt, "Results found: 1", "Query result count should be in the prompt.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task CreateProcedure_AutonomousExecution_MultipleSteps()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);
            
            var procXml = @"<ToolCommandList>
                <ToolCommand><ToolName>Step1</ToolName><ToolInput>Input1</ToolInput></ToolCommand>
                <ToolCommand><ToolName>Step2</ToolName><ToolInput>Input2</ToolInput></ToolCommand>
            </ToolCommandList>";

            mockLLM.ResponseFunc = (p) => $@"<ToolCommandList><ToolCommand><ToolName>CreateProcedure</ToolName><Path>MultiStep</Path><ToolInput><![CDATA[{procXml}]]></ToolInput></ToolCommand></ToolCommandList>";

            // Epoch 1: Create the procedure
            await agent.RunEpochAsync("start");

            // Verify IP exists using same query as agent
            var ipQuery = new Function("ActivePointer", new Wild("?proc"), new Wild("?step"));
            // ActivePointer is a directive in HAMM and is auto-routed to Goals.
            var activePointers = agent._hamm.Store.QueryV2(ipQuery, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            
            if (activePointers.Count == 0)
            {
                var allFacts = agent._hamm.Store.GetFacts().ToList();
                Console.WriteLine($"Total facts: {allFacts.Count}");
                foreach(var f in allFacts) Console.WriteLine($"- [{f.Scope}] {f.Kind}: {f.Expression.ToDisplayString()} (Cert: {f.Certainty}, Quality: {f.QualityScore})");
            }

            Assert.HasCount(1, activePointers, "ActivePointer should be findable by agent's query.");

            // Epoch 2: Execute step 1
            mockLLM.ResponseFunc = (p) => throw new Exception("LLM called during Step 1!");
            await agent.RunEpochAsync("run 1");
            Assert.AreEqual("Step1", runner.Calls.Last().Tool);

            // Epoch 3: Execute step 2
            mockLLM.ResponseFunc = (p) => throw new Exception("LLM called during Step 2!");
            await agent.RunEpochAsync("run 2");
            Assert.AreEqual("Step2", runner.Calls.Last().Tool);

            // Epoch 4: Procedure should be done, LLM should be called again
            bool llmCalled = false;
            mockLLM.ResponseFunc = (p) => { llmCalled = true; return Common.ToXml(new ToolCommandList()); };
            await agent.RunEpochAsync("done?");
            Assert.IsTrue(llmCalled, "LLM should be called after procedure finishes.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_ExecutesProcedureSteps_WithoutCallingLLM()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);
            
            // Manually setup a procedure in HAMM
            var procList = new Function("List", 
                new Function("Cmd", new Symbol("tool1"), new Symbol("input1"), new Symbol("path1"), new Symbol("tags1")),
                new Function("Cmd", new Symbol("tool2"), new Symbol("input2"), new Symbol("path2"), new Symbol("tags2"))
            );
            var procExpr = new Function("Procedure", new Symbol("StepByStep"), procList);
            agent._hamm.Remember(procExpr, scope: "Procedures");
            
            var ipExpr = new Function("ActivePointer", new Symbol("StepByStep"), new Number(0));
            agent._hamm.Remember(ipExpr, scope: "Global");

            // Set LLM to throw if called
            mockLLM.ResponseFunc = (p) => throw new Exception("LLM should not be called during procedure execution!");

            // Act: Run first epoch
            await agent.RunEpochAsync("run proc");

            // Assert: first tool run
            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("tool1", runner.Calls[0].Tool);
            Assert.AreEqual("input1", runner.Calls[0].Args);

            // Check IP updated
            var ipQuery = new Function("ActivePointer", new Symbol("StepByStep"), new Wild("?step"));
            // ActivePointer is a directive in HAMM and is auto-routed to Goals.
            var pointers = agent._hamm.Store.QueryV2(ipQuery, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            Assert.HasCount(1, pointers);
            var step = (pointers[0].Expression as Function)?.Arguments[1] as Number;
            Assert.IsNotNull(step);
            Assert.AreEqual(1m, step.Value, "IP should be incremented to 1.");

            // Act: Run second epoch
            await agent.RunEpochAsync("run proc step 2");

            // Assert: second tool run
            Assert.HasCount(2, runner.Calls);
            Assert.AreEqual("tool2", runner.Calls[1].Tool);
            Assert.AreEqual("input2", runner.Calls[1].Args);

            // Check IP removed (completed)
            pointers = agent._hamm.Store.QueryV2(ipQuery, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            Assert.IsEmpty(pointers, "IP should be removed after procedure completes.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task HammQuery_ReturnsResultsToLLM()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);
            
            agent._hamm.Remember(new Equality(new Symbol("Status"), new Symbol("Ready")), scope: "Test");

            // Act: LLM calls HammQuery
            mockLLM.ResponseFunc = (p) => Common.ToXml(new ToolCommandList {
                Commands = new List<ToolCommand> {
                    new ToolCommand { ToolName = "HammQuery", ToolInput = "Equality(Status, Wild(\"val\"))", Path = "Test" }
                }
            });

            await agent.RunEpochAsync("query status");

            // Capture next prompt to see if output is there
            string capturedPrompt = "";
            mockLLM.ResponseFunc = (p) => { capturedPrompt = p; return Common.ToXml(new ToolCommandList()); };
            await agent.RunEpochAsync("get results");

            // Assert
            StringAssert.Contains(capturedPrompt, "Status = Ready", "Query results should be in the prompt.");
            StringAssert.Contains(capturedPrompt, "Results found: 1", "Query result count should be in the prompt.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task CreateProcedure_MalformedXml_LogsError()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);
            
            // Malformed XML (missing closing tag)
            var procXml = @"<ToolCommandList><ToolCommand><ToolName>Test</ToolName></ToolCommand>"; 

            mockLLM.ResponseFunc = (p) => $@"<ToolCommandList><ToolCommand><ToolName>CreateProcedure</ToolName><Path>ErrorProc</Path><ToolInput><![CDATA[{procXml}]]></ToolInput></ToolCommand></ToolCommandList>";

            // Act
            await agent.RunEpochAsync("test error");

            // Assert: No procedure facts should exist
            var allFacts = agent._hamm.Store.GetFacts().ToList();
            Assert.IsFalse(allFacts.Any(f => f.Expression.ToDisplayString().Contains("Procedure(ErrorProc")), "Procedure should not have been created from malformed XML.");
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RunEpochAsync_ProcedurePointerOutOfBounds_RemovesPointer()
        {
            // Arrange
            var mockLLM = new MockLLM();
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, mockLLM, toolRunner: runner);
            
            // Setup a procedure with 1 step, but set IP to 5
            var procList = new Function("List", new Function("Cmd", new Symbol("tool"), new Symbol("input"), new Symbol(""), new Symbol("")));
            agent._hamm.Remember(new Function("Procedure", new Symbol("ShortProc"), procList), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("ShortProc"), new Number(5)), scope: "Global");

            // Act
            await agent.RunEpochAsync("run out of bounds");

            // Assert: IP should be removed, LLM should be called
            var pointers = agent._hamm.Store.GetFacts().Where(f => f.Expression.ToDisplayString().Contains("ActivePointer")).ToList();
            Assert.IsEmpty(pointers, "Invalid pointer should be removed.");
        }
    }
}
