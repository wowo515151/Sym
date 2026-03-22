// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymCobra.Core;
using SymCobra.Regions;
using SymCobra.Runtime;
using SymCobra.Telemetry;

namespace SymSolvers.Tests;

[TestClass]
public class CobraDiagnosticsSnapshotTests
{
    [TestMethod]
    public void CobraDiagnostics_CreateSnapshot_CapturesPhaseCountsFallbacksAndElapsed()
    {
        var diagnostics = new CobraDiagnostics();

        diagnostics.BeginPhase(CobraPhase.Match);
        Thread.Sleep(1);
        diagnostics.RecordFallback(CobraPhase.Match, CobraFallbackReason.MatchCompatibilityPath);
        diagnostics.RecordPhaseSource(CobraPhase.Match, "DirectExecution:Cuda", isGpuBacked: true);
        diagnostics.EndPhase(CobraPhase.Match);
        diagnostics.RecordSync(CobraSyncDirection.LegacyToCobra, CobraSyncReason.InitialGraphAuthority, isFullSync: true);

        var snapshot = diagnostics.CreateSnapshot();

        Assert.IsTrue(snapshot.HasFallbacks);
        Assert.AreEqual(1, snapshot.FallbackPhaseCount);
        Assert.AreEqual(1, snapshot.SyncEvents.Length);
        Assert.AreEqual(CobraSyncReason.InitialGraphAuthority, snapshot.SyncEvents[0].Reason);
        Assert.AreEqual(1, snapshot.FallbackEvents.Length);
        Assert.AreEqual(CobraFallbackReason.MatchCompatibilityPath, snapshot.FallbackEvents[0].Reason);
        Assert.AreEqual(1, snapshot.PhaseSourceEvents.Length);
        Assert.AreEqual("DirectExecution:Cuda", snapshot.PhaseSourceEvents[0].Source);
        Assert.IsTrue(snapshot.PhaseSourceEvents[0].IsGpuBacked);
        Assert.IsTrue(snapshot.TryGetPhase(CobraPhase.Match, out var phase));
        Assert.AreEqual(1, phase.ExecutionCount);
        Assert.AreEqual(1, phase.FallbackCount);
        Assert.IsTrue(phase.Elapsed >= TimeSpan.Zero);
    }

    [TestMethod]
    public void CobraDiagnostics_CreateSnapshot_CapturesKernelTelemetryDelta()
    {
        CobraCudaNative.ResetKernelTelemetryForTesting();
        var diagnostics = new CobraDiagnostics();

        CobraCudaNative.RecordKernelTelemetryForTesting("score_frontier_v3_by_id_cached", TimeSpan.FromMilliseconds(2));
        CobraCudaNative.RecordKernelTelemetryForTesting("score_frontier_v3_by_id_cached", TimeSpan.FromMilliseconds(3));

        var snapshot = diagnostics.CreateSnapshot();

        Assert.AreEqual(1, snapshot.KernelTelemetry.Length);
        Assert.AreEqual("score_frontier_v3_by_id_cached", snapshot.KernelTelemetry[0].KernelName);
        Assert.AreEqual(2, snapshot.KernelTelemetry[0].CallCount);
        Assert.IsTrue(snapshot.KernelTelemetry[0].Elapsed >= TimeSpan.FromMilliseconds(5));
    }

    [TestMethod]
    public void CobraMatcher_RequiresCompatibilityPath_FalseWhenNoEligibleCompatibilityCandidatesRemain()
    {
        var rule = new Rule(new Add(new Wild("x"), new Wild("y")), new Wild("x"));
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> remainingRulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>> { [7] = new[] { rule } };
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> eligibleNodesByClass =
            new Dictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>
            {
                [7] = new Dictionary<Rule, HashSet<ENode>>
                {
                    [rule] = []
                }
            };

        bool requiresCompatibilityPath = CobraMatcher.RequiresCompatibilityPath(remainingRulesByClass, eligibleNodesByClass);

        Assert.IsFalse(requiresCompatibilityPath);
    }

    [TestMethod]
    public void CobraRulePartitioner_BuildCompatibilityRulesByClass_RemovesDirectRules()
    {
        var directRule = new Rule(new Add(new Number(0), new Wild("x")), new Wild("x"));
        var unmatchedDirectRule = new Rule(new Add(new Number(1), new Wild("y")), new Wild("y"));
        var compatibilityRule = new Rule(new Add(new Add(new Wild("x"), new Add(new Wild("y"), new Wild("z"))), new Wild("w")), new Wild("x"));
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [1] = new[] { directRule, unmatchedDirectRule, compatibilityRule }
            };
        var directPlan = new CobraDirectMatchPlan(
            new Dictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>
            {
                [1] = new Dictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>
                {
                    [directRule] = [new CobraDirectMatchPair(1, directRule, new ENode("Add", [2, 3]))]
                }
            },
            CobraDirectMatchSource.CpuHeuristic);

        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(rulesByClass, directPlan);

        Assert.AreEqual(1, compatibilityRulesByClass.Count);
        CollectionAssert.AreEqual(new[] { unmatchedDirectRule, compatibilityRule }, compatibilityRulesByClass[1].ToArray());
    }

    [TestMethod]
    public void CobraRulePartitioner_BuildCompatibilityRulesByClass_RemovesExhaustivelyHandledDirectRules()
    {
        var handledDirectRule = new Rule(new Symbol("pi"), new Number(3));
        var compatibilityRule = new Rule(new Add(new Add(new Wild("x"), new Add(new Wild("y"), new Wild("z"))), new Wild("w")), new Wild("x"));
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [1] = new[] { handledDirectRule, compatibilityRule }
            };
        var directPlan = new CobraDirectMatchPlan(
            new Dictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>(),
            CobraDirectMatchSource.Cuda,
            new Dictionary<int, IReadOnlySet<Rule>>
            {
                [1] = new HashSet<Rule> { handledDirectRule }
            });

        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(rulesByClass, directPlan);

        Assert.AreEqual(1, compatibilityRulesByClass.Count);
        CollectionAssert.AreEqual(new[] { compatibilityRule }, compatibilityRulesByClass[1].ToArray());
    }

    [TestMethod]
    public void CobraMatcher_FilterRulesByPositiveCandidates_RemovesEmptyCandidateRules()
    {
        var matchingRule = new Rule(new Add(new Wild("x"), new Wild("y")), new Wild("x"));
        var emptyRule = new Rule(new Multiply(new Wild("x"), new Wild("y")), new Wild("x"));
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [3] = new[] { matchingRule, emptyRule }
            };
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> eligibleNodesByClass =
            new Dictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>
            {
                [3] = new Dictionary<Rule, HashSet<ENode>>
                {
                    [matchingRule] = [new ENode("Add", [1, 2])],
                    [emptyRule] = []
                }
            };

        var filteredRulesByClass = CobraMatcher.FilterRulesByPositiveCandidates(rulesByClass, eligibleNodesByClass);

        Assert.AreEqual(1, filteredRulesByClass.Count);
        CollectionAssert.AreEqual(new[] { matchingRule }, filteredRulesByClass[3].ToArray());
    }

    [TestMethod]
    public void CobraMatcher_FilterRulesByPositiveCandidates_CanBeSlicedAfterFullFrontierFiltering()
    {
        var addRule = new Rule(new Add(new Wild("x"), new Wild("y")), new Wild("x"));
        var mulRule = new Rule(new Multiply(new Wild("x"), new Wild("y")), new Wild("x"));
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [3] = [addRule],
                [5] = [mulRule]
            };
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>> eligibleNodesByClass =
            new Dictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>
            {
                [3] = new Dictionary<Rule, HashSet<ENode>>
                {
                    [addRule] = [new ENode("Add", [1, 2])]
                },
                [5] = new Dictionary<Rule, HashSet<ENode>>
                {
                    [mulRule] = []
                }
            };

        var fullFilteredRulesByClass = CobraMatcher.FilterRulesByPositiveCandidates(rulesByClass, eligibleNodesByClass);
        var slicedFilteredRulesByClass = CobraSolverStrategy.SliceRulesByClass(fullFilteredRulesByClass, [3]);

        Assert.AreEqual(1, slicedFilteredRulesByClass.Count);
        CollectionAssert.AreEqual(new[] { addRule }, slicedFilteredRulesByClass[3].ToArray());
    }

    [TestMethod]
    public void CobraMatcher_ExcludeDirectRules_CanBeSlicedAfterFullFrontierExclusion()
    {
        var addRule = new Rule(new Add(new Wild("x"), new Wild("y")), new Wild("x"));
        var mulRule = new Rule(new Multiply(new Wild("x"), new Wild("y")), new Wild("x"));
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [3] = [addRule],
                [5] = [mulRule]
            };
        var directPlan = new CobraDirectMatchPlan(
            new Dictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>
            {
                [5] = new Dictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>
                {
                    [mulRule] = [new CobraDirectMatchPair(5, mulRule, new ENode("Multiply", [1, 2]))]
                }
            },
            CobraDirectMatchSource.Cuda);

        var fullRemainingRulesByClass = CobraMatcher.ExcludeDirectRules(rulesByClass, directPlan);
        var slicedRemainingRulesByClass = CobraSolverStrategy.SliceRulesByClass(fullRemainingRulesByClass, [3]);

        Assert.AreEqual(1, slicedRemainingRulesByClass.Count);
        CollectionAssert.AreEqual(new[] { addRule }, slicedRemainingRulesByClass[3].ToArray());
    }

    [TestMethod]
    public void CobraSolverStrategy_RequiresLegacyBoundarySyncForRuleUnions_FalseWhenNoPendingRuleUnions()
    {
        var graphState = new CobraGraphState();
        graphState.AddExpression(new Add(new Number(0), new Symbol("x")));

        bool requiresSync = CobraSolverStrategy.RequiresLegacyBoundarySyncForRuleUnions(graphState, pendingRuleUnionCount: 0);

        Assert.IsFalse(requiresSync);
    }

    [TestMethod]
    public void CobraSolverStrategy_ShouldExecuteUnionApplication_FalseWhenPreparedUnionBatchIsEmpty()
    {
        var unionResult = new CobraUnionPreparationResult([], CobraUnionPreparationSource.CpuHeuristic);

        bool shouldExecute = CobraSolverStrategy.ShouldExecuteUnionApplication(unionResult);

        Assert.IsFalse(shouldExecute);
    }

    [TestMethod]
    public void CobraSolverStrategy_ShouldExecuteAnalysis_FalseWhenAnalysisClassListIsEmpty()
    {
        bool shouldExecute = CobraSolverStrategy.ShouldExecuteAnalysis([]);

        Assert.IsFalse(shouldExecute);
    }

    [TestMethod]
    public void CobraSolverStrategy_FilterFrontierClassIdsForCompatibility_RemovesClassesWithoutCompatibilityRules()
    {
        IReadOnlyList<int> frontierClassIds = [3, 5, 7];
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> compatibilityRulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [5] = [new Rule(new Add(new Wild("x"), new Wild("y")), new Wild("x"))],
                [7] = [new Rule(new Multiply(new Wild("x"), new Wild("y")), new Wild("x"))]
            };

        var filteredClassIds = CobraSolverStrategy.FilterFrontierClassIdsForCompatibility(frontierClassIds, compatibilityRulesByClass);

        CollectionAssert.AreEqual(new[] { 5, 7 }, filteredClassIds.ToArray());
    }

    [TestMethod]
    public void CobraSolverStrategy_SliceRulesByClass_UsesOnlyRequestedClasses()
    {
        var addRule = new Rule(new Add(new Wild("x"), new Wild("y")), new Wild("x"));
        var mulRule = new Rule(new Multiply(new Wild("x"), new Wild("y")), new Wild("x"));
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [3] = [addRule],
                [5] = [mulRule]
            };

        var slicedRulesByClass = CobraSolverStrategy.SliceRulesByClass(rulesByClass, [5]);

        Assert.AreEqual(1, slicedRulesByClass.Count);
        CollectionAssert.AreEqual(new[] { mulRule }, slicedRulesByClass[5].ToArray());
    }

    [TestMethod]
    public void CobraSolverStrategy_SelectPrimaryMatchFrontierClassIds_PrefersInteriorQueue()
    {
        var plan = new CobraFrontierPlan(
            orderedClassIds: [3, 5, 7],
            prioritySource: CobraFrontierPrioritySource.Cuda,
            interiorQueueClassIds: [5],
            boundaryQueueClassIds: [3],
            residualQueueClassIds: [7]);

        var primaryClassIds = CobraSolverStrategy.SelectPrimaryMatchFrontierClassIds(plan);

        CollectionAssert.AreEqual(new[] { 5 }, primaryClassIds.ToArray());
    }

    [TestMethod]
    public void CobraSolverStrategy_ShouldWidenMatchFrontier_TrueWhenInteriorStageIsIdle()
    {
        var plan = new CobraFrontierPlan(
            orderedClassIds: [5, 3, 7],
            prioritySource: CobraFrontierPrioritySource.Cuda,
            interiorQueueClassIds: [5],
            boundaryQueueClassIds: [3],
            residualQueueClassIds: [7]);

        bool shouldWiden = CobraSolverStrategy.ShouldWidenMatchFrontier(
            plan,
            plan.InteriorQueueClassIds,
            matchCount: 0,
            directMatchCount: 0,
            simpleDirectApplicationCount: 0);

        Assert.IsTrue(shouldWiden);
    }

    [TestMethod]
    public void CobraSolverStrategy_SelectDeferredMatchFrontierClassIds_UsesOnlyDeferredQueues()
    {
        var plan = new CobraFrontierPlan(
            orderedClassIds: [5, 3, 7, 11],
            prioritySource: CobraFrontierPrioritySource.Cuda,
            interiorQueueClassIds: [5],
            boundaryQueueClassIds: [3],
            residualQueueClassIds: [7],
            suppressedQueueClassIds: [11]);

        var deferredClassIds = CobraSolverStrategy.SelectDeferredMatchFrontierClassIds(plan);

        CollectionAssert.AreEqual(new[] { 3, 7, 11 }, deferredClassIds.ToArray());
    }

    [TestMethod]
    public void CobraSolverStrategy_ShouldWidenMatchFrontier_FalseWhenInteriorStageProducedDirectApplications()
    {
        var plan = new CobraFrontierPlan(
            orderedClassIds: [5, 3, 7],
            prioritySource: CobraFrontierPrioritySource.Cuda,
            interiorQueueClassIds: [5],
            boundaryQueueClassIds: [3],
            residualQueueClassIds: [7]);

        bool shouldWiden = CobraSolverStrategy.ShouldWidenMatchFrontier(
            plan,
            plan.InteriorQueueClassIds,
            matchCount: 0,
            directMatchCount: 0,
            simpleDirectApplicationCount: 1);

        Assert.IsFalse(shouldWiden);
    }

    [TestMethod]
    public void CobraNodeMatchCandidatePlanner_FilterRulesByPossibleCandidates_RemovesImpossibleDirectRules()
    {
        var graph = new EGraph();
        int classId = graph.Add(new Add(new Symbol("x"), new Symbol("y")));
        graph.Rebuild();

        var possibleRule = new Rule(new Add(new Wild("a"), new Wild("b")), new Wild("a"));
        var impossibleRule = new Rule(new Add(new Number(0), new Wild("b")), new Wild("b"));
        var snapshot = CobraPlannerSnapshot.Create(graph);

        var filteredRulesByClass = CobraNodeMatchCandidatePlanner.FilterRulesByPossibleCandidates(
            snapshot,
            [classId],
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [classId] = new[] { possibleRule, impossibleRule }
            });

        Assert.AreEqual(1, filteredRulesByClass.Count);
        CollectionAssert.AreEqual(new[] { possibleRule }, filteredRulesByClass[classId].ToArray());
    }

    [TestMethod]
    public void CobraNodeMatchCandidatePlanner_Build_WithSharedMetadataCache_PreservesEligibleNodes()
    {
        var graph = new EGraph();
        int classId = graph.Add(new Add(new Number(0), new Number(1)));
        graph.Rebuild();

        var rule1 = new Rule(new Add(new Number(0), new Wild("x")), new Wild("x"));
        var rule2 = new Rule(new Add(new Wild("x"), new Number(1)), new Wild("x"));
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass =
            new Dictionary<int, IReadOnlyList<Rule>>
            {
                [classId] = new[] { rule1, rule2 }
            };

        var snapshot = CobraPlannerSnapshot.Create(graph);
        var uncachedPlan = CobraNodeMatchCandidatePlanner.Build(graph, snapshot, [classId], rulesByClass);
        var sharedMetadataCache = new Dictionary<Rule, CobraRulePatternMetadata>();
        var cachedPlan = CobraNodeMatchCandidatePlanner.Build(graph, snapshot, [classId], rulesByClass, sharedMetadataCache);

        Assert.AreEqual(uncachedPlan.Source, cachedPlan.Source);
        CollectionAssert.AreEqual(
            uncachedPlan.EligibleNodesByClass[classId].Keys.Select(static rule => rule.Pattern.ToDisplayString()).ToArray(),
            cachedPlan.EligibleNodesByClass[classId].Keys.Select(static rule => rule.Pattern.ToDisplayString()).ToArray());
        Assert.AreEqual(uncachedPlan.EligibleNodesByClass[classId][rule1].Count, cachedPlan.EligibleNodesByClass[classId][rule1].Count);
        Assert.AreEqual(uncachedPlan.EligibleNodesByClass[classId][rule2].Count, cachedPlan.EligibleNodesByClass[classId][rule2].Count);
        Assert.AreEqual(2, sharedMetadataCache.Count);
    }

}
