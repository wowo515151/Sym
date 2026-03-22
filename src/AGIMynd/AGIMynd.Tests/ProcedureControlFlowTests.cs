//Copyright Warren Harding 2026.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;

namespace AGIMynd.Tests
{
    [TestClass]
    public class ProcedureControlFlowTests
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
                try { Directory.Delete(_testMemoryRoot, true); } catch { }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_IfElseAndGoto_SelectsExpectedBranch()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("If"), new Symbol("true"), new Symbol("2"), new Symbol("1")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo BAD"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Goto"), new Symbol("4"), new Symbol("4"), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo SKIP"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo GOOD"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("CtrlIfGoto"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("CtrlIfGoto"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("run control");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("powershell", runner.Calls[0].Tool);
            Assert.AreEqual("echo GOOD", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_ForContinue_ReachesExitStep()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("For"), new Symbol("2"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Continue"), new Symbol(""), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("EndFor"), new Symbol(""), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo AFTER_LOOP"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("LoopContinue"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("LoopContinue"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("run loop");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo AFTER_LOOP", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_Break_ExitsLoopEarly()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("For"), new Symbol("5"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Break"), new Symbol(""), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("EndFor"), new Symbol(""), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo AFTER_BREAK"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("LoopBreak"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("LoopBreak"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("run break");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo AFTER_BREAK", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_CallReturn_ResumesCaller()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var callee = new Function("List",
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo INNER"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Return"), new Symbol(""), new Symbol(""), new Symbol(""))
            );
            var caller = new Function("List",
                new Function("Cmd", new Symbol("Call"), new Symbol("SubProc"), new Symbol("SubProc"), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo OUTER"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("SubProc"), callee), scope: "Procedures");
            agent._hamm.Remember(new Function("Procedure", new Symbol("MainProc"), caller), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("MainProc"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("epoch 1");
            await agent.RunEpochAsync("epoch 2");

            Assert.HasCount(2, runner.Calls);
            Assert.AreEqual("echo INNER", runner.Calls[0].Args);
            Assert.AreEqual("echo OUTER", runner.Calls[1].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_Parallel_ExecutesAllSubCommands()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var parallelXml = "<ToolCommandList>"
                            + "<ToolCommand><ToolName>powershell</ToolName><ToolInput>echo P1</ToolInput></ToolCommand>"
                            + "<ToolCommand><ToolName>git</ToolName><ToolInput>status</ToolInput></ToolCommand>"
                            + "</ToolCommandList>";

            var list = new Function("List",
                new Function("Cmd", new Symbol("Parallel"), new Symbol(parallelXml), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("ParProc"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("ParProc"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("parallel");

            Assert.HasCount(2, runner.Calls);
            Assert.IsTrue(runner.Calls.Any(c => c.Tool == "powershell" && c.Args == "echo P1"));
            Assert.IsTrue(runner.Calls.Any(c => c.Tool == "git" && c.Args == "status"));
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_GotoInvalidTarget_FallsThroughToNextStep()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("Goto"), new Symbol("does-not-exist"), new Symbol("does-not-exist"), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo FALLTHROUGH"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("GotoInvalid"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("GotoInvalid"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("goto invalid");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo FALLTHROUGH", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_IfFalse_UsesElseTarget()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("If"), new Symbol("0"), new Symbol("1"), new Symbol("2")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo TRUE_BRANCH"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo FALSE_BRANCH"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("IfFalse"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("IfFalse"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("if false");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo FALSE_BRANCH", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_IfExistsCondition_ResolvesByLabel()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            agent._hamm.Remember(new Symbol("GuardPresent"), scope: "Global");

            var list = new Function("List",
                new Function("Cmd", new Symbol("If"), new Symbol("Exists(GuardPresent)"), new Symbol("good"), new Symbol("bad")),
                new Function("Cmd", new Symbol("Label"), new Symbol("bad"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo BAD"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Label"), new Symbol("good"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo GOOD"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("IfExists"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("IfExists"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("if exists");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo GOOD", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_IfExistsCondition_ResolvesFromActiveScopeChain()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            // This guard is intentionally scoped (not Global) to validate Exists() uses the active scope chain.
            agent._hamm.CurrentScope = "Ops";
            agent._hamm.Remember(new Symbol("ScopedGuard"), scope: "Ops");

            var guardExpr = new Symbol("ScopedGuard");
            var visibleInOps = agent._hamm.Store.QueryV2(guardExpr, new HAMM.QueryOptions { Scope = "Ops", MinQualityScore = 0.0 }).ToList();
            var visibleInGlobal = agent._hamm.Store.QueryV2(guardExpr, new HAMM.QueryOptions { Scope = "Global", MinQualityScore = 0.0 }).ToList();
            Assert.HasCount(1, visibleInOps);
            Assert.HasCount(0, visibleInGlobal);

            var list = new Function("List",
                new Function("Cmd", new Symbol("If"), new Symbol("Exists(ScopedGuard)"), new Symbol("good"), new Symbol("bad")),
                new Function("Cmd", new Symbol("Label"), new Symbol("bad"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo BAD"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Label"), new Symbol("good"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo GOOD"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("IfExistsScoped"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("IfExistsScoped"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("if exists scoped");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo GOOD", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_ForMissingEndFor_FallsThrough()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("For"), new Symbol("3"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo AFTER_MISSING_ENDFOR"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("ForNoEnd"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("ForNoEnd"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("for missing endfor");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo AFTER_MISSING_ENDFOR", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_CallMissingTarget_FallsThrough()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("Call"), new Symbol("NoSuchProc"), new Symbol("NoSuchProc"), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo AFTER_BAD_CALL"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("CallBadTarget"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("CallBadTarget"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("call missing target");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo AFTER_BAD_CALL", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_ReturnWithoutCallFrame_RemovesPointer()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("Return"), new Symbol(""), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("ReturnNoFrame"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("ReturnNoFrame"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("return no frame");

            Assert.HasCount(0, runner.Calls);
            var activePointerQuery = new Function("ActivePointer", new Wild("?p"), new Wild("?s"));
            var activePointers = agent._hamm.Store.QueryV2(activePointerQuery, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            Assert.HasCount(0, activePointers);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_ParallelWithControlOnlyCommands_SkipsExecutionAndCompletes()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var parallelXml = "<ToolCommandList>"
                            + "<ToolCommand><ToolName>Goto</ToolName><ToolInput>1</ToolInput></ToolCommand>"
                            + "<ToolCommand><ToolName>Label</ToolName><ToolInput>only-label</ToolInput></ToolCommand>"
                            + "</ToolCommandList>";

            var list = new Function("List",
                new Function("Cmd", new Symbol("Parallel"), new Symbol(parallelXml), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("ParallelControlOnly"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("ParallelControlOnly"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("parallel control only");

            Assert.HasCount(0, runner.Calls);
            var activePointerQuery = new Function("ActivePointer", new Wild("?p"), new Wild("?s"));
            var activePointers = agent._hamm.Store.QueryV2(activePointerQuery, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            Assert.HasCount(0, activePointers);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_ForZeroIterations_SkipsBodyAndRunsAfterLoop()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("For"), new Symbol("0"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo BODY"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("EndFor"), new Symbol(""), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo AFTER_ZERO_LOOP"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("ForZero"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("ForZero"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("for zero");

            Assert.HasCount(1, runner.Calls);
            Assert.AreEqual("echo AFTER_ZERO_LOOP", runner.Calls[0].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_NestedForBreak_OnlyBreaksInnerLoop()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("For"), new Symbol("2"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("For"), new Symbol("3"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Break"), new Symbol(""), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("EndFor"), new Symbol(""), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo OUTER_BODY"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("EndFor"), new Symbol(""), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo DONE"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("NestedBreak"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("NestedBreak"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("nested break 1");
            await agent.RunEpochAsync("nested break 2");
            await agent.RunEpochAsync("nested break 3");

            Assert.HasCount(3, runner.Calls);
            Assert.AreEqual("echo OUTER_BODY", runner.Calls[0].Args);
            Assert.AreEqual("echo OUTER_BODY", runner.Calls[1].Args);
            Assert.AreEqual("echo DONE", runner.Calls[2].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_CallDepthTwo_UnwindsInOrder()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var procB = new Function("List",
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo B"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Return"), new Symbol(""), new Symbol(""), new Symbol(""))
            );
            var procA = new Function("List",
                new Function("Cmd", new Symbol("Call"), new Symbol("ProcB"), new Symbol("ProcB"), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo A"), new Symbol(""), new Symbol("")),
                new Function("Cmd", new Symbol("Return"), new Symbol(""), new Symbol(""), new Symbol(""))
            );
            var procMain = new Function("List",
                new Function("Cmd", new Symbol("Call"), new Symbol("ProcA"), new Symbol("ProcA"), new Symbol("")),
                new Function("Cmd", new Symbol("powershell"), new Symbol("echo MAIN"), new Symbol(""), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("ProcB"), procB), scope: "Procedures");
            agent._hamm.Remember(new Function("Procedure", new Symbol("ProcA"), procA), scope: "Procedures");
            agent._hamm.Remember(new Function("Procedure", new Symbol("ProcMain"), procMain), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("ProcMain"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("call depth 1");
            await agent.RunEpochAsync("call depth 2");
            await agent.RunEpochAsync("call depth 3");

            Assert.HasCount(3, runner.Calls);
            Assert.AreEqual("echo B", runner.Calls[0].Args);
            Assert.AreEqual("echo A", runner.Calls[1].Args);
            Assert.AreEqual("echo MAIN", runner.Calls[2].Args);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_MalformedActivePointer_IsInvalidated()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called when malformed pointer is handled.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            agent._hamm.Remember(new Function("ActivePointer", new Symbol("BadProc"), new Symbol("not-a-number")), scope: "Global");

            await agent.RunEpochAsync("malformed pointer");

            Assert.HasCount(0, runner.Calls);
            var activePointerQuery = new Function("ActivePointer", new Wild("?p"), new Wild("?s"));
            var activePointers = agent._hamm.Store.QueryV2(activePointerQuery, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            Assert.HasCount(0, activePointers);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_UnknownProcedurePointer_IsInvalidated()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while invalid pointer is being removed.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            agent._hamm.Remember(new Function("ActivePointer", new Symbol("NoSuchProcedure"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("unknown procedure pointer");

            Assert.HasCount(0, runner.Calls);
            var activePointerQuery = new Function("ActivePointer", new Wild("?p"), new Wild("?s"));
            var activePointers = agent._hamm.Store.QueryV2(activePointerQuery, new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            Assert.HasCount(0, activePointers);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_TransitionGuard_StopsInfiniteControlLoop()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("Goto"), new Symbol("0"), new Symbol("0"), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("InfiniteGoto"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("InfiniteGoto"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("infinite control loop");

            Assert.HasCount(0, runner.Calls);
            var activePointer = agent._hamm.Store.QueryV2(
                new Function("ActivePointer", new Symbol("InfiniteGoto"), new Number(0)),
                new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            Assert.HasCount(1, activePointer);
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcedureControl_TransitionGuard_RepeatedHitsInvalidateStuckPointer()
        {
            var llm = new MockLLM { ResponseFunc = _ => throw new Exception("LLM should not be called while procedure pointer is active.") };
            var runner = new MockToolRunner();
            var agent = new MyndAgent(_testMemoryRoot, llm, toolRunner: runner);

            var list = new Function("List",
                new Function("Cmd", new Symbol("Goto"), new Symbol("0"), new Symbol("0"), new Symbol(""))
            );

            agent._hamm.Remember(new Function("Procedure", new Symbol("InfiniteGotoEscalate"), list), scope: "Procedures");
            agent._hamm.Remember(new Function("ActivePointer", new Symbol("InfiniteGotoEscalate"), new Number(0)), scope: "Global");

            await agent.RunEpochAsync("infinite control loop epoch 1");
            await agent.RunEpochAsync("infinite control loop epoch 2");
            await agent.RunEpochAsync("infinite control loop epoch 3");

            Assert.HasCount(0, runner.Calls);
            var activePointer = agent._hamm.Store.QueryV2(
                new Function("ActivePointer", new Symbol("InfiniteGotoEscalate"), new Number(0)),
                new HAMM.QueryOptions { Scope = "Goals", MinQualityScore = 0.0 }).ToList();
            Assert.HasCount(0, activePointer);
        }
    }
}
