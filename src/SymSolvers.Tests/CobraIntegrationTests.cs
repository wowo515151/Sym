// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core.EGraph;
using Sym.Operations;
using Sym.Core;
using SymCobra.Regions;
using SymCobra.Core;
using SymCobra.Regression;
using SymCobra.Runtime;
using SymSolvers.EGraphSolver;
using WordsToSym;

namespace SymSolvers.Tests;

[TestClass]
public class CobraIntegrationTests
{
    [TestMethod]
    [Timeout(10000)]
    public void SolveWithEGraph_CobraBackend_PreservesSolveBehavior()
    {
        string script = @"
<Options>
  EGraphBackend: Cobra
  Target: x
</Options>
x + 5 = 10
";

        var wrapper = new ProblemScriptEGraphWrapper();
        string result = wrapper.SolveWithEGraph(script);

        Assert.IsFalse(result.StartsWith("Error:", StringComparison.Ordinal), result);
        Assert.IsTrue(result.Contains("x = 5", StringComparison.Ordinal), result);
    }

    [TestMethod]
    [Timeout(10000)]
    public void SolveWithEGraph_CobraRegressionMode_ReturnsBestExpression()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"cobra-regression-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            csvPath,
            string.Join(Environment.NewLine, new[]
            {
                "x,y",
                "1,2",
                "2,4",
                "3,6",
                "4,8"
            }));

        try
        {
            string script = $@"
<Options>
  EGraphBackend: Cobra
  RegressionMode: true
  RegressionDataset: {csvPath}
  RegressionTarget: y
  RegressionFeatures: x
  RegressionLoss: MSE
  RegressionComplexityPenalty: 0.001
  RegressionMaxCandidates: 32
</Options>
";

            var wrapper = new ProblemScriptEGraphWrapper();
            string result = wrapper.SolveWithEGraph(script);

            Assert.IsFalse(result.StartsWith("Error:", StringComparison.Ordinal), result);
            Assert.IsTrue(result.Contains("x", StringComparison.Ordinal), result);
            Assert.IsTrue(result.Contains("Score:", StringComparison.Ordinal), result);
        }
        finally
        {
            if (File.Exists(csvPath))
            {
                File.Delete(csvPath);
            }
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRegressionEngine_ResolvesFeatureNamesCaseInsensitively()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"cobra-regression-case-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            csvPath,
            string.Join(Environment.NewLine, new[]
            {
                "x,Y",
                "1,2",
                "2,4",
                "3,6",
                "4,8"
            }));

        try
        {
            var engine = new CobraRegressionEngine();
            var result = engine.SolveTabular(
                new CobraRegressionOptions(
                    csvPath,
                    "Y",
                    ["X"],
                    0.001,
                    32,
                    "MSE"));

            Assert.IsTrue(result.BestExpression.ToDisplayString().Contains("X", StringComparison.Ordinal));
            Assert.IsTrue(result.BestScore < 0.01, $"Expected a near-perfect fit but got {result.BestScore}.");
        }
        finally
        {
            if (File.Exists(csvPath))
            {
                File.Delete(csvPath);
            }
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRuntimeInfo_DetectsCudaWhenNativeRuntimeIsPresent()
    {
        var info = CobraRuntimeInfo.Detect();
        Assert.IsTrue(info.IsCudaAvailable, info.StatusMessage);
        Assert.AreEqual("CUDA", info.RuntimeKind, info.StatusMessage);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRegionDetector_UsesCudaScoringForSaturationRegions()
    {
        var graph = new EGraph();
        var expr = new Relu(new TensorAdd(
            new MatMul(new Symbol("A"), new Symbol("B")),
            new MatMul(new Symbol("A"), new Symbol("C"))));

        graph.Add(expr);
        graph.Rebuild();

        var regions = CobraRegionDetector.Detect(graph);

        Assert.IsTrue(regions.Count > 0, "Expected at least one COBRA region.");
        Assert.IsTrue(regions.Any(r => r.ScoreSource == CobraScoreSource.Cuda),
            "Expected CUDA-based region scoring to be active.");
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraConflictResolver_UsesCudaRegionSelectionOrdering()
    {
        var regions = new[]
        {
            new CobraRegion(0, CobraRegionFamily.BilinearOverlap, new[] { 1 }, new[] { 2, 3, 4 }, 5.0, 8.0, CobraScoreSource.Cuda, true, false, "cold"),
            new CobraRegion(1, CobraRegionFamily.SharedSink, new[] { 1 }, new[] { 2 }, 9.0, 2.0, CobraScoreSource.Cuda, false, false, "hot")
        };

        var plan = CobraConflictResolver.BuildPlan(regions);

        Assert.AreEqual(1, plan.Regions[0].RegionId);
        Assert.IsTrue(plan.HotClassIds.Contains(1));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraMatchPlanner_UsesCudaPriorityForSaturationMatches()
    {
        var graph = new EGraph();
        int rootId = graph.Add(new Add(
            new MatMul(new Symbol("A"), new Symbol("B")),
            new MatMul(new Symbol("A"), new Symbol("C"))));
        graph.Rebuild();

        var plan = CobraConflictResolver.BuildPlan(CobraRegionDetector.Detect(graph));
        var matches = new List<Match>
        {
            new(new Rule(new Wild("x"), new Wild("x")), rootId, System.Collections.Immutable.ImmutableDictionary<string, int>.Empty)
        };

        var result = CobraMatchPlanner.Prioritize(matches, plan);

        Assert.AreEqual(CobraMatchPrioritySource.Cuda, result.PrioritySource);
        Assert.AreEqual(1, result.Matches.Count);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraMatchPlanner_UsesCudaToPreferMoreSpecificRules()
    {
        var matches = new List<Match>
        {
            new(new Rule(new Wild("z"), new Wild("z")), 1, System.Collections.Immutable.ImmutableDictionary<string, int>.Empty),
            new(new Rule(new Add(new Number(0), new Wild("x")), new Wild("x")), 1, System.Collections.Immutable.ImmutableDictionary<string, int>.Empty)
        };

        var plan = new CobraRegionPlan(
            new[] { new CobraRegion(0, CobraRegionFamily.SharedSink, new[] { 1 }, Array.Empty<int>(), 8, 1, CobraScoreSource.Cuda, false, false, "hot") },
            new HashSet<int> { 1 },
            new HashSet<int>(),
            new HashSet<int>(),
            new HashSet<int>());
        var result = CobraMatchPlanner.Prioritize(matches, plan);

        Assert.AreEqual(CobraMatchPrioritySource.Cuda, result.PrioritySource);
        Assert.AreEqual("Add", ENode.GetHead(result.Matches[0].Rule.Pattern));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraFrontierPlanner_UsesCudaPriorityForSaturationClasses()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(new Symbol("A"), new Symbol("B")),
            new MatMul(new Symbol("A"), new Symbol("C"))));
        graph.Add(new Relu(new Symbol("x")));
        graph.Rebuild();

        var regionPlan = CobraConflictResolver.BuildPlan(CobraRegionDetector.Detect(graph));
        var frontierPlan = CobraFrontierPlanner.Build(graph, regionPlan);

        Assert.AreEqual(CobraFrontierPrioritySource.Cuda, frontierPlan.PrioritySource);
        Assert.IsTrue(frontierPlan.OrderedClassIds.Count > 0);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraFrontierPlanner_UsesCudaToPreferClassesInMoreHotRegions()
    {
        var graph = new EGraph();
        int a = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int b = graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        graph.Rebuild();

        var regionPlan = new CobraRegionPlan(
            new[]
            {
                new CobraRegion(0, CobraRegionFamily.SharedSink, new[] { a }, new[] { b }, 8, 1, CobraScoreSource.Cuda, false, false, "r1"),
                new CobraRegion(1, CobraRegionFamily.BilinearOverlap, new[] { a }, Array.Empty<int>(), 7, 2, CobraScoreSource.Cuda, false, false, "r2")
            },
            new HashSet<int> { a },
            new HashSet<int> { b },
            new HashSet<int>(),
            new HashSet<int>());

        var frontierPlan = CobraFrontierPlanner.Build(graph, regionPlan);

        Assert.AreEqual(CobraFrontierPrioritySource.Cuda, frontierPlan.PrioritySource);
        Assert.AreEqual(a, frontierPlan.OrderedClassIds[0]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraFrontierPlanner_BuildsExplicitQueuesAndRoutingReasons()
    {
        var graph = new EGraph();
        int interior = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int boundary = graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        int residual = graph.Add(new Relu(new Symbol("x")));
        int suppressed = graph.Add(new Symbol("z"));
        graph.Rebuild();

        var regionPlan = new CobraRegionPlan(
            new[]
            {
                new CobraRegion(0, CobraRegionFamily.SharedSink, new[] { interior }, new[] { boundary }, 8, 1, CobraScoreSource.Cuda, false, false, "hot")
            },
            new HashSet<int> { interior },
            new HashSet<int> { boundary },
            new HashSet<int> { residual },
            new HashSet<int> { suppressed });

        var snapshot = CobraPlannerSnapshot.Create(graph);
        var frontierPlan = CobraFrontierPlanner.Build(
            graph,
            new[] { interior, boundary, residual, suppressed },
            snapshot.NodeCounts,
            snapshot.Generations,
            regionPlan);

        CollectionAssert.AreEqual(new[] { interior }, frontierPlan.InteriorQueueClassIds.ToArray());
        CollectionAssert.AreEqual(new[] { boundary }, frontierPlan.BoundaryQueueClassIds.ToArray());
        CollectionAssert.AreEqual(new[] { residual }, frontierPlan.ResidualQueueClassIds.ToArray());
        CollectionAssert.AreEqual(new[] { suppressed }, frontierPlan.SuppressedQueueClassIds.ToArray());
        CollectionAssert.AreEqual(new[] { interior, boundary, residual, suppressed }, frontierPlan.OrderedClassIds.ToArray());
        Assert.AreEqual("interior-hot", frontierPlan.RoutingReasonsByClassId[interior]);
        Assert.AreEqual("boundary-deferred", frontierPlan.RoutingReasonsByClassId[boundary]);
        Assert.AreEqual("residual-deferred", frontierPlan.RoutingReasonsByClassId[residual]);
        Assert.AreEqual("suppressed-conflict", frontierPlan.RoutingReasonsByClassId[suppressed]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_UsesCudaAndDeduplicatesPairs()
    {
        var result = CobraUnionPreparationPlanner.Prepare(
            [
                (7, 3),
                (3, 7),
                (4, 4),
                (9, 2),
                (2, 9)
            ]);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, result.PreparationSource);
        Assert.AreEqual(2, result.PreparedUnions.Count);

        CollectionAssert.AreEquivalent(
            new[]
            {
                ((ulong)(uint)3 << 32) | 7u,
                ((ulong)(uint)2 << 32) | 9u
            },
            result.PreparedUnions.Select(p => p.PairKey).ToArray());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_UsesCudaToResolveRootsFromGraphSnapshot()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int c = graph.Add(new Symbol("c"));
        graph.Union(a, b);

        var result = CobraUnionPreparationPlanner.Prepare(graph, [(a, c), (b, c)]);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, result.PreparationSource);
        Assert.AreEqual(1, result.PreparedUnions.Count);
        Assert.AreEqual(graph.Find(a), result.PreparedUnions[0].LeftId);
        Assert.AreEqual(graph.Find(c), result.PreparedUnions[0].RightId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_UsesCudaToResolveRootsFromCobraGraphState()
    {
        var legacyGraph = new EGraph();
        int a = legacyGraph.Add(new Symbol("a"));
        int b = legacyGraph.Add(new Symbol("b"));
        int c = legacyGraph.Add(new Symbol("c"));
        legacyGraph.Union(a, b);

        var graphState = new CobraGraphState();
        graphState.SyncFromLegacyGraph(legacyGraph);

        var result = CobraUnionPreparationPlanner.Prepare(graphState, [(a, c), (b, c)]);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, result.PreparationSource);
        Assert.AreEqual(1, result.PreparedUnions.Count);
        Assert.AreEqual(graphState.Find(a), result.PreparedUnions[0].LeftId);
        Assert.AreEqual(graphState.Find(c), result.PreparedUnions[0].RightId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_CanReuseWarmCachedRootsFromCobraGraphState()
    {
        var legacyGraph = new EGraph();
        int a = legacyGraph.Add(new Symbol("a"));
        int b = legacyGraph.Add(new Symbol("b"));
        int c = legacyGraph.Add(new Symbol("c"));
        legacyGraph.Union(a, b);

        var graphState = new CobraGraphState();
        graphState.SyncFromLegacyGraph(legacyGraph);
        Assert.IsTrue(CobraCudaNative.TryWarmGraphCaches(graphState));

        var result = CobraUnionPreparationPlanner.Prepare(graphState, [(a, c), (b, c)], preferCachedParentSnapshot: true);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, result.PreparationSource);
        Assert.AreEqual(1, result.PreparedUnions.Count);
        Assert.AreEqual(graphState.Find(a), result.PreparedUnions[0].LeftId);
        Assert.AreEqual(graphState.Find(c), result.PreparedUnions[0].RightId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ReusesCachedGraphStateForRepeatedUnionAndRepairCalls()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int c = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Union(a, b);

        var parents = graph.GetParentSnapshot();
        Assert.IsTrue(CobraCudaNative.TryResolveUnionRoots(parents, [a], [c], out var firstLeft, out _, out _));
        Assert.IsTrue(CobraCudaNative.TryResolveUnionRoots(parents, [b], [c], out var secondLeft, out _, out _));
        Assert.AreEqual(firstLeft[0], secondLeft[0]);

        var snapshot = graph.GetRepairSnapshot();
        Assert.IsTrue(CobraCudaNative.TryMarkRepairCandidates(snapshot.Parents, snapshot.ChildStarts, snapshot.ChildCounts, snapshot.ChildIds, out var firstDirty));
        Assert.IsTrue(CobraCudaNative.TryMarkRepairCandidates(snapshot.Parents, snapshot.ChildStarts, snapshot.ChildCounts, snapshot.ChildIds, out var secondDirty));
        CollectionAssert.AreEqual(firstDirty, secondDirty);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_UpdatesCachedParentSnapshotAfterBatchUnion()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int c = graph.Add(new Symbol("c"));

        var initialParents = graph.GetParentSnapshot();
        Assert.IsTrue(CobraCudaNative.TryResolveUnionRoots(initialParents, [a], [c], out _, out _, out _));

        var batchResult = graph.UnionBatchDetailed([[graph.Find(a), graph.Find(b)]], [graph.Find(a)], assumeCanonicalRoots: true);
        Assert.IsTrue(batchResult.Changed);
        Assert.IsTrue(batchResult.UpdatedClassIds.Count > 0);
        Assert.IsTrue(CobraCudaNative.TryApplyParentUpdates(batchResult.UpdatedClassIds.ToArray(), batchResult.UpdatedParentIds.ToArray()));

        Assert.IsTrue(CobraCudaNative.TryResolveUnionRootsFromCache([a], [b], out var resolvedLeft, out var resolvedRight, out _));
        Assert.AreEqual(graph.Find(a), resolvedLeft[0]);
        Assert.AreEqual(graph.Find(b), resolvedRight[0]);

        var freshParents = graph.GetParentSnapshot();
        Assert.IsTrue(CobraCudaNative.TryResolveUnionRoots(freshParents, [a], [b], out var refreshedLeft, out var refreshedRight, out _));
        CollectionAssert.AreEqual(resolvedLeft, refreshedLeft);
        CollectionAssert.AreEqual(resolvedRight, refreshedRight);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_UpdatesCachedClassMetricsAfterBatchUnion()
    {
        var graph = new EGraph();
        int a = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int b = graph.Add(new Symbol("c"));

        var nodeCounts = Enumerable.Range(0, graph.ClassCount)
            .Select(id => graph.GetClass(id).Nodes.Count)
            .ToArray();
        var generations = Enumerable.Range(0, graph.ClassCount)
            .Select(id => graph.GetClass(id).Generation)
            .ToArray();

        Assert.IsTrue(CobraCudaNative.TryScoreExtractClasses(nodeCounts, generations, out var beforeScores));

        var batchResult = graph.UnionBatchDetailed([[graph.Find(a), graph.Find(b)]], [graph.Find(a)], assumeCanonicalRoots: true);
        Assert.IsTrue(batchResult.Changed);
        Assert.IsTrue(CobraCudaNative.TryApplyClassMetricUpdates(
            batchResult.MetricClassIds.ToArray(),
            batchResult.MetricNodeCounts.ToArray(),
            batchResult.MetricGenerations.ToArray()));

        int[] refreshedNodeCounts = Enumerable.Range(0, graph.ClassCount)
            .Select(id => graph.GetClass(id).Nodes.Count)
            .ToArray();
        int[] refreshedGenerations = Enumerable.Range(0, graph.ClassCount)
            .Select(id => graph.GetClass(id).Generation)
            .ToArray();

        Assert.IsTrue(CobraCudaNative.TryScoreExtractClasses(refreshedNodeCounts, refreshedGenerations, out var afterScores));
        Assert.IsTrue(afterScores.Zip(beforeScores, static (after, before) => after != before).Any(static changed => changed));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_WarmsGraphCachesAfterRebuild()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Union(a, b);
        graph.Rebuild();

        Assert.IsTrue(CobraCudaNative.TryWarmGraphCaches(graph));

        var parents = graph.GetParentSnapshot();
        Assert.IsTrue(CobraCudaNative.TryResolveUnionRootsFromCache([a], [b], out var left, out var right, out _));
        Assert.IsTrue(CobraCudaNative.TryResolveUnionRoots(parents, [a], [b], out var freshLeft, out var freshRight, out _));
        CollectionAssert.AreEqual(left, freshLeft);
        CollectionAssert.AreEqual(right, freshRight);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_CachesRegionDetectionMasks()
    {
        var graph = new EGraph();
        graph.Add(new Relu(new TensorAdd(
            new MatMul(new Symbol("A"), new Symbol("B")),
            new MatMul(new Symbol("A"), new Symbol("C")))));
        graph.Rebuild();

        var headBuckets = CobraNodeMatchEncoding.BuildClassHeadBucketMasks(graph);
        var exactHeads = CobraNodeMatchEncoding.BuildClassExactHeadMasks(graph);
        Assert.IsTrue(CobraCudaNative.TryDetectRegionsBatch(headBuckets, exactHeads, out var freshFamilyCodes));

        Assert.IsTrue(CobraCudaNative.TryWarmRegionDetectionMasks(headBuckets, exactHeads));
        Assert.IsTrue(CobraCudaNative.TryDetectRegionsBatchCached(graph.ClassCount, out var cachedFamilyCodes));

        CollectionAssert.AreEqual(freshFamilyCodes, cachedFamilyCodes);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ReusesCachedClassMetricsForRepeatedOrderingCalls()
    {
        int[] nodeCounts = [3, 5, 2];
        int[] generations = [10, 20, 5];
        int[] repairCounts = [1, 4, 0];
        int[] unresolvedFlags = [0, 1, 1];
        int[] memberIds = [1, 0, 2];

        Assert.IsTrue(CobraCudaNative.TryScoreRebuildWithRepair(nodeCounts, generations, repairCounts, out var rebuildScores1));
        Assert.IsTrue(CobraCudaNative.TryScoreRebuildWithRepair(nodeCounts, generations, repairCounts, out var rebuildScores2));
        CollectionAssert.AreEqual(rebuildScores1, rebuildScores2);

        Assert.IsTrue(CobraCudaNative.TryScoreAnalysisWithRepair(nodeCounts, generations, unresolvedFlags, repairCounts, out var analysisScores1));
        Assert.IsTrue(CobraCudaNative.TryScoreAnalysisWithRepair(nodeCounts, generations, unresolvedFlags, repairCounts, out var analysisScores2));
        CollectionAssert.AreEqual(analysisScores1, analysisScores2);

        Assert.IsTrue(CobraCudaNative.TryScoreUnionMembers(memberIds, nodeCounts, generations, out var unionMemberScores1));
        Assert.IsTrue(CobraCudaNative.TryScoreUnionMembers(memberIds, nodeCounts, generations, out var unionMemberScores2));
        CollectionAssert.AreEqual(unionMemberScores1, unionMemberScores2);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ScoresRebuildClassesByIdFromCachedMetrics()
    {
        int[] nodeCounts = [3, 5, 2, 7];
        int[] generations = [10, 20, 5, 1];
        int[] repairCounts = [4, 1, 0];
        int[] classIds = [3, 0, 2];

        Assert.IsTrue(CobraCudaNative.TryScoreRebuildWithRepair(
            classIds.Select(id => nodeCounts[id]).ToArray(),
            classIds.Select(id => generations[id]).ToArray(),
            repairCounts,
            out var expectedScores));
        Assert.IsTrue(CobraCudaNative.TryScoreRebuildWithRepairById(classIds, nodeCounts, generations, repairCounts, out var cachedScores));
        CollectionAssert.AreEqual(expectedScores, cachedScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ScoresAnalysisClassesByIdFromCachedMetrics()
    {
        int[] nodeCounts = [3, 5, 2, 7];
        int[] generations = [10, 20, 5, 1];
        int[] unresolvedFlags = [1, 0, 1];
        int[] repairCounts = [4, 1, 0];
        int[] classIds = [3, 0, 2];

        Assert.IsTrue(CobraCudaNative.TryScoreAnalysisWithRepair(
            classIds.Select(id => nodeCounts[id]).ToArray(),
            classIds.Select(id => generations[id]).ToArray(),
            unresolvedFlags,
            repairCounts,
            out var expectedScores));
        Assert.IsTrue(CobraCudaNative.TryScoreAnalysisWithRepairById(classIds, nodeCounts, generations, unresolvedFlags, repairCounts, out var cachedScores));
        CollectionAssert.AreEqual(expectedScores, cachedScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ScoresFrontierClassesByIdFromCachedMetrics()
    {
        int[] nodeCounts = [3, 5, 2, 7];
        int[] generations = [10, 20, 5, 1];
        int[] classIds = [3, 0, 2];
        int[] hotFlags = [1, 0, 0];
        int[] boundaryFlags = [0, 1, 0];
        int[] residualFlags = [0, 0, 1];
        int[] hotRegionCounts = [2, 1, 0];
        int[] boundaryRegionCounts = [1, 0, 3];

        Assert.IsTrue(CobraCudaNative.TryScoreFrontierV2(
            classIds.Select(id => nodeCounts[id]).ToArray(),
            classIds.Select(id => generations[id]).ToArray(),
            hotFlags,
            boundaryFlags,
            residualFlags,
            hotRegionCounts,
            boundaryRegionCounts,
            out var expectedScores));
        Assert.IsTrue(CobraCudaNative.TryScoreFrontierV2ById(
            classIds,
            nodeCounts,
            generations,
            hotFlags,
            boundaryFlags,
            residualFlags,
            hotRegionCounts,
            boundaryRegionCounts,
            out var cachedScores));
        CollectionAssert.AreEqual(expectedScores, cachedScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ScoresRepairCandidatesByIdFromCachedMetrics()
    {
        int[] nodeCounts = [3, 5, 2, 7];
        int[] generations = [10, 20, 5, 1];
        int[] classIds = [3, 0, 2];
        int[] childCounts = [2, 1, 4];
        int[] boundaryFlags = [1, 0, 1];

        Assert.IsTrue(CobraCudaNative.TryScoreRepairCandidatesV2(
            classIds,
            childCounts,
            classIds.Select(id => generations[id]).ToArray(),
            classIds.Select(id => nodeCounts[id]).ToArray(),
            boundaryFlags,
            out var expectedScores));
        Assert.IsTrue(CobraCudaNative.TryScoreRepairCandidatesV2ById(
            classIds,
            childCounts,
            nodeCounts,
            generations,
            boundaryFlags,
            out var cachedScores));
        CollectionAssert.AreEqual(expectedScores, cachedScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ScoresDirectPairsByIdFromCachedMetrics()
    {
        int[] nodeCounts = [3, 5, 2, 7];
        int[] generations = [10, 20, 5, 1];
        int[] classIds = [3, 0, 2];
        int[] nodeArities = [2, 1, 4];
        int[] nestedFlags = [1, 0, 1];

        Assert.IsTrue(CobraCudaNative.TryScoreDirectPairsV2(
            classIds,
            nodeArities,
            classIds.Select(id => generations[id]).ToArray(),
            classIds.Select(id => nodeCounts[id]).ToArray(),
            nestedFlags,
            out var expectedScores));
        Assert.IsTrue(CobraCudaNative.TryScoreDirectPairsV2ById(
            classIds,
            nodeArities,
            nestedFlags,
            nodeCounts,
            generations,
            out var cachedScores));
        CollectionAssert.AreEqual(expectedScores, cachedScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ReusesCachedClassMetricsForRepeatedDirectScoringCalls()
    {
        int[] nodeCounts = [3, 5, 2];
        int[] generations = [10, 20, 5];
        int[] classIds = [1, 0, 2];
        int[] nodeArities = [2, 2, 1];
        int[] nestedFlags = [1, 0, 1];
        int[] pairCounts = [4, 2, 1];
        int[] nestedPairCounts = [3, 0, 1];

        Assert.IsTrue(CobraCudaNative.TryScoreDirectPairsV2(classIds, nodeArities, generations, nodeCounts, nestedFlags, out var directPairScores1));
        Assert.IsTrue(CobraCudaNative.TryScoreDirectPairsV2(classIds, nodeArities, generations, nodeCounts, nestedFlags, out var directPairScores2));
        CollectionAssert.AreEqual(directPairScores1, directPairScores2);

        Assert.IsTrue(CobraCudaNative.TryScoreDirectClassesV2(pairCounts, nestedPairCounts, generations, nodeCounts, out var directClassScores1));
        Assert.IsTrue(CobraCudaNative.TryScoreDirectClassesV2(pairCounts, nestedPairCounts, generations, nodeCounts, out var directClassScores2));
        CollectionAssert.AreEqual(directClassScores1, directClassScores2);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ScoresDirectClassesByIdFromCachedMetrics()
    {
        int[] nodeCounts = [3, 5, 2, 7];
        int[] generations = [10, 20, 5, 12];
        int[] classIds = [1, 3, 0];
        int[] pairCounts = [4, 2, 1];
        int[] nestedPairCounts = [3, 1, 0];

        Assert.IsTrue(CobraCudaNative.TryScoreDirectClassesV2(
            pairCounts,
            nestedPairCounts,
            classIds.Select(id => generations[id]).ToArray(),
            classIds.Select(id => nodeCounts[id]).ToArray(),
            out var expectedScores));
        Assert.IsTrue(CobraCudaNative.TryScoreDirectClassesV2ById(
            classIds,
            pairCounts,
            nestedPairCounts,
            nodeCounts,
            generations,
            out var cachedScores));
        CollectionAssert.AreEqual(expectedScores, cachedScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ReusesCachedClassMetricsForRepeatedExtractionScoringCalls()
    {
        int[] nodeCounts = [3, 5, 2];
        int[] generations = [10, 20, 5];
        int[] headCodes = [3, 4, 9];
        int[] arities = [2, 1, 0];
        int[] classIds = [1, 0, 2];

        Assert.IsTrue(CobraCudaNative.TryScoreExtractClasses(nodeCounts, generations, out var classScores1));
        Assert.IsTrue(CobraCudaNative.TryScoreExtractClasses(nodeCounts, generations, out var classScores2));
        CollectionAssert.AreEqual(classScores1, classScores2);

        Assert.IsTrue(CobraCudaNative.TryScoreExtractNodes(headCodes, arities, classIds, nodeCounts, generations, out var nodeScores1));
        Assert.IsTrue(CobraCudaNative.TryScoreExtractNodes(headCodes, arities, classIds, nodeCounts, generations, out var nodeScores2));
        CollectionAssert.AreEqual(nodeScores1, nodeScores2);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ScoresExtractNodesWithCachedClassMetrics()
    {
        int[] nodeCounts = [3, 5, 2];
        int[] generations = [10, 20, 5];
        int[] headCodes = [3, 4, 9];
        int[] arities = [2, 1, 0];
        int[] classIds = [1, 0, 2];

        Assert.IsTrue(CobraCudaNative.TryScoreExtractNodes(headCodes, arities, classIds, nodeCounts, generations, out var expectedScores));
        Assert.IsTrue(CobraCudaNative.TryScoreExtractNodesWithCachedClassMetrics(headCodes, arities, classIds, nodeCounts, generations, out var cachedScores));
        CollectionAssert.AreEqual(expectedScores, cachedScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ScoresExtractClassesByIdFromCachedMetrics()
    {
        int[] nodeCounts = [3, 5, 2, 7];
        int[] generations = [10, 20, 5, 1];
        int[] classIds = [3, 0, 2];

        Assert.IsTrue(CobraCudaNative.TryScoreExtractClasses(nodeCounts, generations, out _));
        Assert.IsTrue(CobraCudaNative.TryScoreExtractClassesById(classIds, nodeCounts, generations, out var subsetScores));

        CollectionAssert.AreEqual(
            classIds.Select(id => (nodeCounts[id] * 32) + (generations[id] * 4)).ToArray(),
            subsetScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ReusesCachedClassMetricsForRepeatedPreparedUnionScoringCalls()
    {
        int[] nodeCounts = [3, 5, 2];
        int[] generations = [10, 20, 5];
        int[] leftIds = [1, 0, 2];
        int[] rightIds = [0, 2, 1];

        Assert.IsTrue(CobraCudaNative.TryScorePreparedUnionsByClassId(leftIds, rightIds, nodeCounts, generations, out var preparedUnionScores1));
        Assert.IsTrue(CobraCudaNative.TryScorePreparedUnionsByClassId(leftIds, rightIds, nodeCounts, generations, out var preparedUnionScores2));
        CollectionAssert.AreEqual(preparedUnionScores1, preparedUnionScores2);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ReusesCachedNodeRuleSnapshotsForRepeatedCandidateScoring()
    {
        int[] nodeHeadCodes = [CobraNodeMatchEncoding.EncodeHeadCode("Add")];
        int[] nodeArities = [2];
        int[] nodeChildStarts = [0];
        int[] nodeChildIds = [1, 2];
        int[] classConstraintMasks = [0, 0, 0];
        int[] classHeadBucketMasks = [0, 0, 0];
        int[] classExactHeadMasks = [0, 0, 0];
        int[] classChildEqualityMasks = [0, 0, 0];
        int[] classChildAtomBucketMasks = new int[12];
        int[] classChildConstraintMasks = new int[12];
        int[] classChildReferenceBloomMasks = new int[12];
        int[] ruleHeadCodes = [CobraNodeMatchEncoding.EncodeHeadCode("Add")];
        int[] ruleArities = [2];
        int[] wildcardFlags = [0];
        int[] directWildcardFlags = [1];
        int[] ruleArgStarts = [0];
        int[] ruleArgGroupIds = [0, 1];
        int[] ruleArgConstraintMasks = [0, 0];
        int[] ruleArgKinds = [0, 0];
        int[] ruleArgHeadBuckets = [0, 0];
        int[] ruleArgExactHeadMasks = [0, 0];
        int[] ruleArgNestedRepeatMasks = [0, 0];
        int[] ruleArgNestedAtomBucketMasks = new int[8];
        int[] ruleArgNestedConstraintMasks = new int[8];
        int[] ruleArgNestedTopLevelReferenceMasks = Enumerable.Repeat(-1, 8).ToArray();

        Assert.IsTrue(CobraCudaNative.TryScoreNodeRuleCandidates(
            nodeHeadCodes,
            nodeArities,
            nodeChildStarts,
            nodeChildIds,
            classConstraintMasks,
            classHeadBucketMasks,
            classExactHeadMasks,
            classChildEqualityMasks,
            classChildAtomBucketMasks,
            classChildConstraintMasks,
            classChildReferenceBloomMasks,
            ruleHeadCodes,
            ruleArities,
            wildcardFlags,
            directWildcardFlags,
            ruleArgStarts,
            ruleArgGroupIds,
            ruleArgConstraintMasks,
            ruleArgKinds,
            ruleArgHeadBuckets,
            ruleArgExactHeadMasks,
            ruleArgNestedRepeatMasks,
            ruleArgNestedAtomBucketMasks,
            ruleArgNestedConstraintMasks,
            ruleArgNestedTopLevelReferenceMasks,
            out var firstScores));

        Assert.IsTrue(CobraCudaNative.TryScoreNodeRuleCandidates(
            nodeHeadCodes,
            nodeArities,
            nodeChildStarts,
            nodeChildIds,
            classConstraintMasks,
            classHeadBucketMasks,
            classExactHeadMasks,
            classChildEqualityMasks,
            classChildAtomBucketMasks,
            classChildConstraintMasks,
            classChildReferenceBloomMasks,
            ruleHeadCodes,
            ruleArities,
            wildcardFlags,
            directWildcardFlags,
            ruleArgStarts,
            ruleArgGroupIds,
            ruleArgConstraintMasks,
            ruleArgKinds,
            ruleArgHeadBuckets,
            ruleArgExactHeadMasks,
            ruleArgNestedRepeatMasks,
            ruleArgNestedAtomBucketMasks,
            ruleArgNestedConstraintMasks,
            ruleArgNestedTopLevelReferenceMasks,
            out var secondScores));

        CollectionAssert.AreEqual(firstScores, secondScores);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ExtractsCompactCompatibleNodeRulePairsFromCachedSnapshots()
    {
        int[] nodeHeadCodes = [CobraNodeMatchEncoding.EncodeHeadCode("Add")];
        int[] nodeArities = [2];
        int[] nodeChildStarts = [0];
        int[] nodeChildIds = [1, 2];
        int[] classConstraintMasks = [0, 0, 0];
        int[] classHeadBucketMasks = [0, 0, 0];
        int[] classExactHeadMasks = [0, 0, 0];
        int[] classChildEqualityMasks = [0, 0, 0];
        int[] classChildAtomBucketMasks = new int[12];
        int[] classChildConstraintMasks = new int[12];
        int[] classChildReferenceBloomMasks = new int[12];
        int[] ruleHeadCodes = [CobraNodeMatchEncoding.EncodeHeadCode("Add")];
        int[] ruleArities = [2];
        int[] wildcardFlags = [0];
        int[] directWildcardFlags = [1];
        int[] ruleArgStarts = [0];
        int[] ruleArgGroupIds = [0, 1];
        int[] ruleArgConstraintMasks = [0, 0];
        int[] ruleArgKinds = [0, 0];
        int[] ruleArgHeadBuckets = [0, 0];
        int[] ruleArgExactHeadMasks = [0, 0];
        int[] ruleArgNestedRepeatMasks = [0, 0];
        int[] ruleArgNestedAtomBucketMasks = new int[8];
        int[] ruleArgNestedConstraintMasks = new int[8];
        int[] ruleArgNestedTopLevelReferenceMasks = Enumerable.Repeat(-1, 8).ToArray();

        Assert.IsTrue(CobraCudaNative.TryExtractCompatibleNodeRulePairsCached(
            nodeHeadCodes,
            nodeArities,
            nodeChildStarts,
            nodeChildIds,
            classConstraintMasks,
            classHeadBucketMasks,
            classExactHeadMasks,
            classChildEqualityMasks,
            classChildAtomBucketMasks,
            classChildConstraintMasks,
            classChildReferenceBloomMasks,
            ruleHeadCodes,
            ruleArities,
            wildcardFlags,
            directWildcardFlags,
            ruleArgStarts,
            ruleArgGroupIds,
            ruleArgConstraintMasks,
            ruleArgKinds,
            ruleArgHeadBuckets,
            ruleArgExactHeadMasks,
            ruleArgNestedRepeatMasks,
            ruleArgNestedAtomBucketMasks,
            ruleArgNestedConstraintMasks,
            ruleArgNestedTopLevelReferenceMasks,
            out var pairs));

        Assert.AreEqual(1, pairs.Length);
        Assert.AreEqual(0, pairs[0].NodeIndex);
        Assert.AreEqual(0, pairs[0].RuleIndex);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_BuildBatchPlan_UsesCudaToGroupTransitivePairs()
    {
        var prepared = new[]
        {
            new CobraPreparedUnion(1, 2, ((ulong)(uint)1 << 32) | 2u),
            new CobraPreparedUnion(2, 3, ((ulong)(uint)2 << 32) | 3u),
            new CobraPreparedUnion(7, 8, ((ulong)(uint)7 << 32) | 8u)
        };

        var batchPlan = CobraUnionPreparationPlanner.BuildBatchPlan(prepared, 8);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, batchPlan.Source);
        Assert.AreEqual(2, batchPlan.Groups.Count);
        Assert.IsTrue(batchPlan.Groups.Any(group => group.MemberIds.SequenceEqual(new[] { 1, 2, 3 })));
        Assert.IsTrue(batchPlan.Groups.Any(group => group.MemberIds.SequenceEqual(new[] { 7, 8 })));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_BuildBatchPlanWithGraph_UsesCudaToChooseAnchorOrder()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Add(new Symbol("x"), new Symbol("y")));
        int c = graph.Add(new Multiply(new Symbol("m"), new Symbol("n")));
        graph.Rebuild();
        graph.GetClass(b).Generation = 10;

        var prepared = new[]
        {
            new CobraPreparedUnion(a, b, ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b)),
            new CobraPreparedUnion(b, c, ((ulong)(uint)Math.Min(b, c) << 32) | (uint)Math.Max(b, c))
        };

        var batchPlan = CobraUnionPreparationPlanner.BuildBatchPlan(graph, prepared);
        var mergedGroup = batchPlan.Groups.Single(group => group.MemberIds.Count == 3);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, batchPlan.Source);
        Assert.AreEqual(b, mergedGroup.AnchorId);
        CollectionAssert.AreEquivalent(new[] { a, b, c }, mergedGroup.MemberIds.ToArray());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_BuildBatchPlanWithCobraGraphState_UsesCudaToChooseAnchorOrder()
    {
        var legacyGraph = new EGraph();
        int a = legacyGraph.Add(new Symbol("a"));
        int b = legacyGraph.Add(new Add(new Symbol("x"), new Symbol("y")));
        int c = legacyGraph.Add(new Multiply(new Symbol("m"), new Symbol("n")));
        legacyGraph.Rebuild();

        var graphState = new CobraGraphState();
        graphState.SyncFromLegacyGraph(legacyGraph);
        graphState.GetClass(b).Generation = 10;

        var prepared = new[]
        {
            new CobraPreparedUnion(a, b, ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b)),
            new CobraPreparedUnion(b, c, ((ulong)(uint)Math.Min(b, c) << 32) | (uint)Math.Max(b, c))
        };

        var batchPlan = CobraUnionPreparationPlanner.BuildBatchPlan(graphState, prepared);
        var mergedGroup = batchPlan.Groups.Single(group => group.MemberIds.Count == 3);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, batchPlan.Source);
        Assert.AreEqual(b, mergedGroup.AnchorId);
        CollectionAssert.AreEquivalent(new[] { a, b, c }, mergedGroup.MemberIds.ToArray());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_BuildBatchPlanWithGraph_UsesCudaToOrderHotGroupsFirst()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Add(new Symbol("x"), new Symbol("y")));
        int c = graph.Add(new Symbol("c"));
        int d = graph.Add(new Multiply(new Symbol("m"), new Symbol("n")));
        graph.Rebuild();
        graph.GetClass(b).Generation = 20;
        graph.GetClass(d).Generation = 2;

        var prepared = new[]
        {
            new CobraPreparedUnion(a, b, ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b)),
            new CobraPreparedUnion(c, d, ((ulong)(uint)Math.Min(c, d) << 32) | (uint)Math.Max(c, d))
        };

        var batchPlan = CobraUnionPreparationPlanner.BuildBatchPlan(graph, prepared);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, batchPlan.Source);
        Assert.AreEqual(b, batchPlan.Groups[0].AnchorId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRuleCompatibilityPlanner_UsesCudaAndFiltersImpossibleRules()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Wild("x"), new Wild("y")), new Wild("x")),
            new(new Multiply(new Wild("x"), new Wild("y")), new Wild("x")),
            new(new Wild("z"), new Wild("z"))
        };

        var rootIds = graph.GetRootIds();
        var plan = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);

        Assert.AreEqual(CobraRuleCompatibilitySource.Cuda, plan.Source);
        int addRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add"));
        var classRules = plan.RulesByClassId[addRootId];
        Assert.IsTrue(classRules.Contains(rules[0]));
        Assert.IsFalse(classRules.Contains(rules[1]));
        Assert.IsTrue(classRules.Contains(rules[2]));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRuleCompatibilityPlanner_UsesCudaToOrderSpecificRulesFirst()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Wild("z"), new Wild("z")),
            new(new Add(new Wild("x"), new Wild("y")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        int addRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add"));
        var plan = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);

        Assert.AreEqual(CobraRuleCompatibilitySource.Cuda, plan.Source);
        Assert.AreEqual(rules[1], plan.RulesByClassId[addRootId][0]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRuleCompatibilityPlanner_FiltersSameHeadArityMismatches()
    {
        var graph = new EGraph();
        graph.Add(new Vector(new Symbol("a"), new Symbol("b")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Vector(new Wild("x"), new Wild("y")), new Wild("x")),
            new(new Vector(new Wild("x"), new Wild("y"), new Wild("z")), new Wild("x")),
            new(new Wild("v"), new Wild("v"))
        };

        var rootIds = graph.GetRootIds();
        int vectorRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Vector"));
        var plan = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var classRules = plan.RulesByClassId[vectorRootId];

        Assert.AreEqual(CobraRuleCompatibilitySource.Cuda, plan.Source);
        Assert.IsTrue(classRules.Contains(rules[0]));
        Assert.IsFalse(classRules.Contains(rules[1]));
        Assert.IsTrue(classRules.Contains(rules[2]));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRuleCompatibilityPlanner_FiltersSameBucketButDifferentExactHead()
    {
        var graph = new EGraph();
        graph.Add(new Number(0));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Number(0), new Number(0)),
            new(new Number(1), new Number(1)),
            new(new Wild("n"), new Wild("n"))
        };

        var rootIds = graph.GetRootIds();
        int zeroRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Num:0"));
        var plan = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var classRules = plan.RulesByClassId[zeroRootId];

        Assert.AreEqual(CobraRuleCompatibilitySource.Cuda, plan.Source);
        Assert.IsTrue(classRules.Contains(rules[0]));
        Assert.IsFalse(classRules.Contains(rules[1]));
        Assert.IsTrue(classRules.Contains(rules[2]));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRuleCompatibilityPlanner_Slice_PreservesSourceAndRuleOrdering()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Wild("z"), new Wild("z")),
            new(new Add(new Wild("x"), new Wild("y")), new Wild("x")),
            new(new Multiply(new Wild("m"), new Wild("n")), new Wild("m"))
        };

        var rootIds = graph.GetRootIds();
        int addRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add"));
        var fullPlan = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);

        var slicedPlan = CobraRuleCompatibilityPlanner.Slice(fullPlan, [addRootId]);

        Assert.AreEqual(fullPlan.Source, slicedPlan.Source);
        Assert.AreEqual(1, slicedPlan.RulesByClassId.Count);
        CollectionAssert.AreEqual(fullPlan.RulesByClassId[addRootId].ToArray(), slicedPlan.RulesByClassId[addRootId].ToArray());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraNodeMatchCandidatePlanner_UsesCudaAndFiltersNodesByRuleShape()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        graph.Rebuild();

        var rootIds = graph.GetRootIds();
        var rules = new List<Rule>
        {
            new(new Add(new Wild("x"), new Wild("y")), new Wild("x")),
            new(new Multiply(new Wild("x"), new Wild("y")), new Wild("x"))
        };

        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        int addRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add"));
        var nodePlan = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);

        Assert.AreEqual(CobraNodeMatchCandidateSource.Cuda, nodePlan.Source);
        var addRuleMap = nodePlan.EligibleNodesByClass[addRootId];
        Assert.IsTrue(addRuleMap[rules[0]].Any(node => node.Head == "Add"));
        Assert.IsFalse(addRuleMap.ContainsKey(rules[1]));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraNodeMatchCandidatePlanner_UsesCudaToRejectRepeatedWildcardMismatches()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Add(new Add(new Symbol("c"), new Symbol("c")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Wild("x"), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var nodePlan = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int repeatedRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && node.Children.Count == 2 && node.Children[0] == node.Children[1]));
        int mixedRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && node.Children.Count == 2 && node.Children[0] != node.Children[1]));

        Assert.AreEqual(CobraNodeMatchCandidateSource.Cuda, nodePlan.Source);
        Assert.IsTrue(nodePlan.EligibleNodesByClass[repeatedRootId][rules[0]].Any());
        Assert.IsFalse(nodePlan.EligibleNodesByClass[mixedRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraNodeMatchCandidatePlanner_UsesCudaToFilterConstrainedWildcardOperations()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new Symbol("A", new Shape(ImmutableArray.Create(2, 2))),
            new Symbol("B", new Shape(ImmutableArray.Create(2, 2)))));
        graph.Add(new Add(
            new Symbol("s1", Shape.Scalar),
            new Symbol("s2", Shape.Scalar)));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(
                new Wild("m1", WildConstraint.Matrix),
                new Wild("m2", WildConstraint.Matrix)),
                new Wild("m1"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var nodePlan = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int matrixRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && node.Children.All(childId => graph.GetClass(childId).Data is Shape shape && shape.IsMatrix)));
        int scalarRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && node.Children.All(childId => graph.GetClass(childId).Data is Shape shape && shape.IsScalar)));

        Assert.AreEqual(CobraNodeMatchCandidateSource.Cuda, nodePlan.Source);
        Assert.IsTrue(nodePlan.EligibleNodesByClass[matrixRootId][rules[0]].Any());
        Assert.IsFalse(nodePlan.EligibleNodesByClass[scalarRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraNodeMatchCandidatePlanner_UsesCudaToFilterFlatAtomWildcardPatterns()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(0), new Symbol("x")));
        graph.Add(new Add(new Symbol("a"), new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var nodePlan = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int zeroRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Num:0")));
        int symbolRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head.StartsWith("Sym:", StringComparison.Ordinal))));

        Assert.AreEqual(CobraNodeMatchCandidateSource.Cuda, nodePlan.Source);
        Assert.IsTrue(nodePlan.EligibleNodesByClass[zeroRootId][rules[0]].Any());
        Assert.IsFalse(nodePlan.EligibleNodesByClass[symbolRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraNodeMatchEncoding_BuildsNestedMasksForCompatibilityCandidateFiltering()
    {
        var nestedReferencePattern = new Add(new Add(new Number(0), new Wild("x")), new Wild("x"));
        var repeatedNestedPattern = new Add(new Add(new Wild("x"), new Wild("x")), new Wild("y"));

        var nestedAtomMasks = CobraNodeMatchEncoding.BuildNestedAtomBucketMasks(nestedReferencePattern);
        var nestedReferenceMasks = CobraNodeMatchEncoding.BuildNestedTopLevelReferenceMasks(nestedReferencePattern);
        var repeatedInfos = CobraNodeMatchEncoding.BuildFlatArgumentInfos(repeatedNestedPattern);

        Assert.AreEqual(1 << CobraNodeMatchEncoding.BucketNumber, nestedAtomMasks[0]);
        Assert.AreEqual(1, nestedReferenceMasks[1]);
        Assert.AreEqual(
            CobraNodeMatchEncoding.EncodeChildEqualityPairMask(0, 1),
            repeatedInfos[0].NestedRepeatMask);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraNodeMatchCandidatePlanner_UsesCudaToOrderSmallerCandidateSetsFirst()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(0), new Symbol("x")));
        graph.Add(new Add(new Symbol("a"), new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Wild("z"), new Wild("z")),
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        int zeroRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Num:0")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var nodePlan = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var orderedRules = nodePlan.EligibleNodesByClass[zeroRootId].Keys.ToArray();

        Assert.AreEqual(CobraNodeMatchCandidateSource.Cuda, nodePlan.Source);
        Assert.AreEqual(rules[1], orderedRules[0]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraNodeMatchCandidatePlanner_Slice_PreservesEligibleNodes()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(0), new Symbol("x")));
        graph.Add(new Multiply(new Symbol("a"), new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Number(0), new Wild("x")), new Wild("x")),
            new(new Multiply(new Wild("a"), new Wild("b")), new Wild("a"))
        };

        var rootIds = graph.GetRootIds();
        int addRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add"));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var fullPlan = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);

        var slicedPlan = CobraNodeMatchCandidatePlanner.Slice(fullPlan, [addRootId]);

        Assert.AreEqual(fullPlan.Source, slicedPlan.Source);
        Assert.AreEqual(1, slicedPlan.EligibleNodesByClass.Count);
        CollectionAssert.AreEqual(
            fullPlan.EligibleNodesByClass[addRootId].Keys.Select(static rule => rule.Pattern.ToDisplayString()).ToArray(),
            slicedPlan.EligibleNodesByClass[addRootId].Keys.Select(static rule => rule.Pattern.ToDisplayString()).ToArray());
        Assert.AreEqual(
            fullPlan.EligibleNodesByClass[addRootId][rules[0]].Count,
            slicedPlan.EligibleNodesByClass[addRootId][rules[0]].Count);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToExtractFlatDirectPairs()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(0), new Symbol("x")));
        graph.Add(new Add(new Symbol("a"), new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int zeroRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Num:0")));
        int symbolRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head.StartsWith("Sym:", StringComparison.Ordinal))));

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass[zeroRootId][rules[0]].Any());
        Assert.IsFalse(plan.PairsByClass[symbolRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToExtractOneLevelNestedPairs()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(new Symbol("A"), new Symbol("B")),
            new Symbol("x")));
        graph.Add(new Add(
            new Symbol("s"),
            new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(
                new MatMul(new Wild("a"), new Wild("b")),
                new Wild("x")),
                new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int nestedRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "MatMul")));
        int plainRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add" && graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head.StartsWith("Sym:", StringComparison.Ordinal))));

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass.ContainsKey(nestedRootId));
        Assert.IsTrue(plan.PairsByClass[nestedRootId].ContainsKey(rules[0]));
        Assert.IsFalse(plan.PairsByClass[plainRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesSimpleApplicationsForDepthThreeNestedPatterns()
    {
        var graph = new EGraph();
        graph.Add(new Relu(
            new Transpose(
                new MatMul(
                    new Transpose(new Symbol("A")),
                    new Symbol("B")))));
        graph.Add(new Relu(
            new Transpose(
                new MatMul(
                    new Symbol("A"),
                    new Symbol("B")))));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Relu(
                    new Transpose(
                        new MatMul(
                            new Transpose(new Wild("a")),
                            new Wild("b")))),
                new Wild("b"))
        };

        var rootIds = graph.GetRootIds();
        int matchingRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Relu" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child =>
                child.Head == "Transpose" &&
                graph.GetClass(child.Children[0]).Nodes.Any(grandChild =>
                    grandChild.Head == "MatMul" &&
                    graph.GetClass(grandChild.Children[0]).Nodes.Any(greatGrandChild => greatGrandChild.Head == "Transpose")))));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(matchingRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsTrue(execution.SimpleApplications[0].Bindings.ContainsKey("a"));
        Assert.IsTrue(execution.SimpleApplications[0].Bindings.ContainsKey("b"));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesSimpleApplicationsForDepthFourNestedPatterns()
    {
        var graph = new EGraph();
        graph.Add(new Relu(
            new Transpose(
                new MatMul(
                    new Transpose(
                        new Relu(new Symbol("A"))),
                    new Symbol("B")))));
        graph.Add(new Relu(
            new Transpose(
                new MatMul(
                    new Transpose(new Symbol("A")),
                    new Symbol("B")))));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Relu(
                    new Transpose(
                        new MatMul(
                            new Transpose(
                                new Relu(new Wild("a"))),
                            new Wild("b")))),
                new Wild("b"))
        };

        var rootIds = graph.GetRootIds();
        int matchingRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Relu" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child =>
                child.Head == "Transpose" &&
                graph.GetClass(child.Children[0]).Nodes.Any(grandChild =>
                    grandChild.Head == "MatMul" &&
                    graph.GetClass(grandChild.Children[0]).Nodes.Any(greatGrandChild =>
                        greatGrandChild.Head == "Transpose" &&
                        graph.GetClass(greatGrandChild.Children[0]).Nodes.Any(depthFourChild =>
                            depthFourChild.Head == "Relu"))))));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(matchingRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsTrue(execution.SimpleApplications[0].Bindings.ContainsKey("a"));
        Assert.IsTrue(execution.SimpleApplications[0].Bindings.ContainsKey("b"));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToRejectNestedRepeatedWildcardMismatches()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(new Symbol("A"), new Symbol("A")),
            new Symbol("x")));
        graph.Add(new Add(
            new MatMul(new Symbol("B"), new Symbol("C")),
            new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(
                new MatMul(new Wild("m"), new Wild("m")),
                new Wild("x")),
                new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int mixedRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Add" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "MatMul" && child.Children.Count == 2 && child.Children[0] != child.Children[1])));

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsFalse(plan.PairsByClass[mixedRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToRejectNestedAtomBucketMismatches()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(new Number(0), new Symbol("B")),
            new Symbol("x")));
        graph.Add(new Add(
            new MatMul(new Symbol("A"), new Symbol("C")),
            new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new MatMul(new Number(0), new Wild("b")), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int symbolicRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Add" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child =>
                child.Head == "MatMul" &&
                graph.GetClass(child.Children[0]).Nodes.Any(grandchild => grandchild.Head.StartsWith("Sym:", StringComparison.Ordinal)))));

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsFalse(plan.PairsByClass[symbolicRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToRejectNestedWildcardConstraintMismatches()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(
                new Matrix(ImmutableArray.Create(1, 1), ImmutableList.Create<IExpression>(new Number(1m))),
                new Symbol("b")),
            new Symbol("x")));
        graph.Add(new Add(
            new MatMul(new Number(2), new Symbol("c")),
            new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new MatMul(new Wild("m", WildConstraint.Matrix), new Wild("b")), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int scalarRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Add" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child =>
                child.Head == "MatMul" &&
                graph.GetClass(child.Children[0]).Nodes.Any(grandchild => grandchild.Head == "Num:2"))));

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsFalse(plan.PairsByClass[scalarRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToRejectNestedTopLevelReferenceMismatches()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(new Symbol("A"), new Symbol("B")),
            new Symbol("A")));
        graph.Add(new Add(
            new MatMul(new Symbol("C"), new Symbol("D")),
            new Symbol("A")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new MatMul(new Wild("a"), new Wild("b")), new Wild("a")), new Wild("a"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        int mismatchRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Add" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child =>
                child.Head == "MatMul" &&
                graph.GetClass(child.Children[0]).Nodes.Any(grandchild => grandchild.Head == "Sym:C"))));

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsFalse(plan.PairsByClass[mismatchRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToPreferNestedDirectClasses()
    {
        var graph = new EGraph();
        int nested = graph.Add(new Add(new MatMul(new Symbol("A"), new Symbol("B")), new Symbol("x")));
        int flat = graph.Add(new Add(new Number(0), new Symbol("y")));
        graph.Rebuild();
        graph.GetClass(graph.Find(nested)).Generation = 1;
        graph.GetClass(graph.Find(flat)).Generation = 1;

        var rules = new List<Rule>
        {
            new(new Add(new MatMul(new Wild("a"), new Wild("b")), new Wild("x")), new Wild("x")),
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var orderedClasses = plan.PairsByClass.Keys.ToArray();

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        CollectionAssert.Contains(orderedClasses, graph.Find(nested));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToOrderHotClassesFirst()
    {
        var graph = new EGraph();
        int low = graph.Add(new Add(new Number(0), new Symbol("x")));
        int high = graph.Add(new Add(new Number(0), new Symbol("y")));
        graph.Rebuild();
        graph.GetClass(graph.Find(high)).Generation = 25;

        var rules = new List<Rule>
        {
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var orderedClasses = plan.PairsByClass.Keys.ToArray();

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(graph.Find(high), orderedClasses[0]);
        CollectionAssert.Contains(orderedClasses, graph.Find(low));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaToOrderDirectRulesWithinAClass()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(0), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Wild("a"), new Wild("b")), new Wild("a")),
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        int rootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add"));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var orderedRules = plan.PairsByClass[rootId].Keys.ToArray();

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(rules[1], orderedRules[0]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_BuildsMatchesFromCudaPairs()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(0), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var matches = CobraDirectMatchMaterializer.Materialize(graph, plan);

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual(rules[0], matches[0].Rule);
        Assert.IsTrue(matches[0].Bindings.ContainsKey("x"));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_SplitsSimpleRulesIntoDirectApplications()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(0), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.IsTrue(execution.SimpleApplications[0].Bindings.ContainsKey("x"));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesSimpleApplicationsForArbitraryConditions()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(0), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Add(new Number(0), new Wild("x")),
                new Wild("x"),
                condition: bindings => bindings.ContainsKey("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesSimpleApplicationsForAssumptionConditions()
    {
        var graph = new EGraph();
        graph.Add(new Divide(new Symbol("x"), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Divide(new Wild("x"), new Wild("x")),
                new Number(1),
                assumptionCondition: (bindings, assumptions) =>
                    bindings.TryGetValue("x", out var value) &&
                    value is Symbol symbol &&
                    assumptions.IsPositive(symbol.Name))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredConstantAddTransform()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(2), new Number(3)));
        graph.Add(new Add(new Number(2), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Add(
                    new Wild("a", WildConstraint.Constant),
                    new Wild("b", WildConstraint.Constant)),
                new Number(0),
                transform: RuleTransforms.ConstantAdd("a", "b"))
        };

        var rootIds = graph.GetRootIds();
        int constantRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Add" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Num:2") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Num:3")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(constantRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNotNull(execution.SimpleApplications[0].TransformedExpression);
        Assert.IsTrue(execution.SimpleApplications[0].TransformedExpression!.InternalEquals(new Number(5)));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredConstantComparisonTransform()
    {
        var graph = new EGraph();
        graph.Add(new Function("gt", new Number(5), new Number(3)));
        graph.Add(new Function("gt", new Number(2), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Function(
                    "gt",
                    new Wild("a", WildConstraint.Constant),
                    new Wild("b", WildConstraint.Constant)),
                new Number(0),
                transform: RuleTransforms.ConstantGreaterThan("a", "b"))
        };

        var rootIds = graph.GetRootIds();
        int constantRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Func:gt" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Num:5") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Num:3")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(constantRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNotNull(execution.SimpleApplications[0].TransformedExpression);
        Assert.IsTrue(execution.SimpleApplications[0].TransformedExpression!.InternalEquals(new Symbol("true")));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredBooleanConstantTransform()
    {
        var graph = new EGraph();
        graph.Add(new Function("and", new Symbol("true"), new Symbol("false")));
        graph.Add(new Function("and", new Symbol("true"), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Function("and", new Wild("a"), new Wild("b")),
                new Number(0),
                transform: RuleTransforms.ConstantAnd("a", "b"))
        };

        var rootIds = graph.GetRootIds();
        int constantRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Func:and" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Sym:true") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Sym:false")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(constantRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNotNull(execution.SimpleApplications[0].TransformedExpression);
        Assert.IsTrue(execution.SimpleApplications[0].TransformedExpression!.InternalEquals(new Symbol("false")));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredUnaryNumericTransform()
    {
        var graph = new EGraph();
        graph.Add(new Function("sin", new Number(0)));
        graph.Add(new Function("sin", new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Function("sin", new Wild("a", WildConstraint.Constant)),
                new Number(0),
                transform: RuleTransforms.ConstantSin("a"))
        };

        var rootIds = graph.GetRootIds();
        int constantRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Func:sin" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Num:0")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(constantRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNotNull(execution.SimpleApplications[0].TransformedExpression);
        Assert.IsTrue(execution.SimpleApplications[0].TransformedExpression!.InternalEquals(new Number(0)));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredConstantPowerTransform()
    {
        var graph = new EGraph();
        graph.Add(new Power(new Number(2), new Number(0.5m)));
        graph.Add(new Power(new Number(2), new Symbol("x")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Power(
                    new Wild("a", WildConstraint.Constant),
                    new Wild("b", WildConstraint.Constant)),
                new Number(0),
                transform: RuleTransforms.ConstantPower("a", "b"))
        };

        var rootIds = graph.GetRootIds();
        int constantRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Pow" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Num:2") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Num:0.5")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(constantRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNotNull(execution.SimpleApplications[0].TransformedExpression);
        Assert.AreEqual((double)Math.Sqrt(2d), (double)((Number)execution.SimpleApplications[0].TransformedExpression!).Value, 1e-12);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredSqrtPowerTransform()
    {
        var graph = new EGraph();
        graph.Add(new Power(new Number(2), new Number(0.5m)));
        graph.Add(new Power(new Symbol("x"), new Number(0.5m)));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Power(new Wild("a", WildConstraint.Constant), new Number(0.5m)),
                new Number(0),
                transform: RuleTransforms.ConstantSquareRoot("a"))
        };

        var rootIds = graph.GetRootIds();
        int constantRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Pow" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Num:2") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Num:0.5")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(constantRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNotNull(execution.SimpleApplications[0].TransformedExpression);
        Assert.AreEqual((double)Math.Sqrt(2d), (double)((Number)execution.SimpleApplications[0].TransformedExpression!).Value, 1e-12);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredStringLiteralCondition()
    {
        var graph = new EGraph();
        graph.Add(new Function("cs_eq_str", new Symbol("userInput"), new Symbol("str:admin")));
        graph.Add(new Function("cs_eq_str", new Symbol("userInput"), new Symbol("otherValue")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Function("cs_eq_str", new Wild("x"), new Wild("c")),
                new Function("cs_guard_sql_validated", new Wild("x")),
                condition: RuleBindingConditions.StringLiteral("c"))
        };

        var rootIds = graph.GetRootIds();
        int literalRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Func:cs_eq_str" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Sym:userInput") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Sym:str:admin")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(literalRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNull(execution.SimpleApplications[0].TransformedExpression);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredMethodSymbolCondition()
    {
        var graph = new EGraph();
        graph.Add(new Function(
            "cs_eq_str",
            new Function("cs_call", new Symbol("System.IO.Path.GetFileName"), new Symbol("userPath")),
            new Symbol("userPath")));
        graph.Add(new Function(
            "cs_eq_str",
            new Function("cs_call", new Symbol("System.Console.WriteLine"), new Symbol("userPath")),
            new Symbol("userPath")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Function(
                    "cs_eq_str",
                    new Function("cs_call", new Wild("m"), new Wild("x")),
                    new Wild("x")),
                new Function("cs_guard_path_valid", new Wild("x")),
                condition: RuleBindingConditions.SymbolContainsAnyIgnoreCase(
                    "m",
                    "System.IO.Path.GetFileName",
                    "System.IO.Path.GetFileNameWithoutExtension"))
        };

        var rootIds = graph.GetRootIds();
        int matchingRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Func:cs_eq_str" &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Sym:userPath") &&
            graph.GetClass(node.Children[0]).Nodes.Any(child =>
                child.Head == "Func:cs_call" &&
                graph.GetClass(child.Children[0]).Nodes.Any(grandChild => grandChild.Head == "Sym:System.IO.Path.GetFileName"))));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(matchingRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNull(execution.SimpleApplications[0].TransformedExpression);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesStructuredTryParseMethodCondition()
    {
        var graph = new EGraph();
        graph.Add(new Function(
            "cs_call",
            new Symbol("System.Int32.TryParse"),
            new Symbol("userInput"),
            new Symbol("parsedValue")));
        graph.Add(new Function(
            "cs_call",
            new Symbol("System.Console.WriteLine"),
            new Symbol("userInput"),
            new Symbol("parsedValue")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Function("cs_call", new Wild("m"), new Wild("x"), new Wild("y")),
                new Function("cs_guard_sql_validated", new Wild("x")),
                condition: RuleBindingConditions.SymbolContainsAnyIgnoreCase(
                    "m",
                    "Guid.TryParse",
                    "Int32.TryParse",
                    "Int64.TryParse",
                    "UInt32.TryParse",
                    "UInt64.TryParse",
                    "Int16.TryParse",
                    "UInt16.TryParse",
                    "Byte.TryParse",
                    "SByte.TryParse",
                    "Double.TryParse",
                    "Single.TryParse",
                    "Decimal.TryParse",
                    "Boolean.TryParse",
                    "DateTime.TryParse",
                    "DateTimeOffset.TryParse",
                    "TimeSpan.TryParse"))
        };

        var rootIds = graph.GetRootIds();
        int matchingRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Func:cs_call" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Sym:System.Int32.TryParse") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Sym:userInput")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(matchingRootId, execution.SimpleApplications[0].RootClassId);
        Assert.IsNull(execution.SimpleApplications[0].TransformedExpression);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_KeepsOpaqueTransformRulesOnManagedPath()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Number(2), new Number(3)));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Add(new Wild("a"), new Wild("b")),
                new Number(0),
                transform: bindings => bindings.TryGetValue("a", out var left) && left is Number
                    ? new Number(123)
                    : null)
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(1, execution.ManagedMatches.Count);
        Assert.AreEqual(0, execution.SimpleApplications.Count);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesExactShapeCompatibilityForStructuredConditions()
    {
        var graph = new EGraph();
        var matrix2x2 = new Shape(ImmutableArray.Create(2, 2));
        var matrix2x3 = new Shape(ImmutableArray.Create(2, 3));

        graph.Add(new Add(
            new Symbol("A", matrix2x2),
            new Symbol("B", matrix2x2)));
        graph.Add(new Add(
            new Symbol("C", matrix2x2),
            new Symbol("D", matrix2x3)));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Add(
                    new Wild("x", WildConstraint.Matrix),
                    new Wild("y", WildConstraint.Matrix)),
                new TensorAdd(new Wild("x"), new Wild("y")),
                condition: RuleShapeConditions.ElementWiseCompatible("x", "y"))
        };

        var rootIds = graph.GetRootIds();
        int compatibleRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Add" &&
            node.Children.All(childId => graph.GetClass(childId).Data is Shape shape && shape.Equals(matrix2x2))));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(compatibleRootId, execution.SimpleApplications[0].RootClassId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesExactMatMulCompatibilityForStructuredConditions()
    {
        var graph = new EGraph();
        var matrix2x3 = new Shape(ImmutableArray.Create(2, 3));
        var matrix3x4 = new Shape(ImmutableArray.Create(3, 4));
        var matrix5x4 = new Shape(ImmutableArray.Create(5, 4));

        graph.Add(new MatMul(
            new Symbol("A", matrix2x3),
            new Symbol("B", matrix3x4)));
        graph.Add(new MatMul(
            new Symbol("C", matrix2x3),
            new Symbol("D", matrix5x4)));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new MatMul(
                    new Wild("x", WildConstraint.Matrix),
                    new Wild("y", WildConstraint.Matrix)),
                new FusedMatMulAdd(new Wild("x"), new Wild("y"), new Number(0)),
                condition: RuleShapeConditions.MatMulCompatible("x", "y"))
        };

        var rootIds = graph.GetRootIds();
        int compatibleRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "MatMul" &&
            graph.GetClass(node.Children[0]).Data is Shape leftShape &&
            graph.GetClass(node.Children[1]).Data is Shape rightShape &&
            leftShape.Equals(matrix2x3) &&
            rightShape.Equals(matrix3x4)));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(compatibleRootId, execution.SimpleApplications[0].RootClassId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesExactSameShapeForStructuredConditions()
    {
        var graph = new EGraph();
        var matrix2x2 = new Shape(ImmutableArray.Create(2, 2));
        var matrix1x2 = new Shape(ImmutableArray.Create(1, 2));

        graph.Add(new Add(
            new Symbol("A", matrix2x2),
            new Symbol("B", matrix2x2)));
        graph.Add(new Add(
            new Symbol("C", matrix2x2),
            new Symbol("D", matrix1x2)));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Add(
                    new Wild("x", WildConstraint.Matrix),
                    new Wild("y", WildConstraint.Matrix)),
                new TensorAdd(new Wild("x"), new Wild("y")),
                condition: RuleShapeConditions.SameShape("x", "y"))
        };

        var rootIds = graph.GetRootIds();
        int sameShapeRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Add" &&
            graph.GetClass(node.Children[0]).Data is Shape leftShape &&
            graph.GetClass(node.Children[1]).Data is Shape rightShape &&
            leftShape.Equals(matrix2x2) &&
            rightShape.Equals(matrix2x2)));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.AreEqual(0, execution.ManagedMatches.Count);
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(sameShapeRootId, execution.SimpleApplications[0].RootClassId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaForRootWildcardRules()
    {
        var graph = new EGraph();
        graph.Add(new Symbol("a"));
        graph.Add(new Number(0));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Wild("x"), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        int rootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head.StartsWith("Sym:", StringComparison.Ordinal)));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);
        var matches = CobraDirectMatchMaterializer.Materialize(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass.ContainsKey(rootId));
        Assert.IsTrue(plan.PairsByClass[rootId].ContainsKey(rules[0]));
        Assert.IsTrue(plan.PairsByClass[rootId][rules[0]].Any());
        Assert.IsFalse(compatibilityRulesByClass.ContainsKey(rootId));
        Assert.AreEqual(2, matches.Count);
        Assert.AreEqual(rules[0], matches[0].Rule);
        Assert.IsTrue(matches.All(match => match.Bindings.ContainsKey("x")));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaForRootAtomRules()
    {
        var graph = new EGraph();
        graph.Add(new Symbol("a"));
        graph.Add(new Symbol("b"));
        graph.Add(new Number(0));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Symbol("a"), new Number(1))
        };

        var rootIds = graph.GetRootIds();
        int symbolRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Sym:a"));
        int otherRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Sym:b"));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);
        var matches = CobraDirectMatchMaterializer.Materialize(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass.ContainsKey(symbolRootId));
        Assert.IsTrue(plan.PairsByClass[symbolRootId][rules[0]].Any());
        Assert.IsFalse(plan.PairsByClass.ContainsKey(otherRootId) && plan.PairsByClass[otherRootId][rules[0]].Any());
        Assert.IsFalse(compatibilityRulesByClass.ContainsKey(symbolRootId));
        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual(rules[0], matches[0].Rule);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaForArithmeticEqualityIsolationRules()
    {
        var graph = new EGraph();
        graph.Add(new Equality(
            new Add(new Symbol("x"), new Number(5)),
            new Number(10)));
        graph.Add(new Equality(
            new Symbol("x"),
            new Number(10)));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Equality(
                    new Add(new Wild("a"), new Wild("b")),
                    new Wild("c")),
                new Equality(
                    new Wild("a"),
                    new Subtract(new Wild("c"), new Wild("b"))))
        };

        var rootIds = graph.GetRootIds();
        int arithmeticEqualityRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Equality" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Add")));
        int plainEqualityRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Equality" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head.StartsWith("Sym:", StringComparison.Ordinal))));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass.ContainsKey(arithmeticEqualityRootId));
        Assert.IsTrue(plan.PairsByClass[arithmeticEqualityRootId][rules[0]].Any());
        Assert.IsFalse(compatibilityRulesByClass.ContainsKey(arithmeticEqualityRootId));
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.IsTrue(execution.SimpleApplications[0].Bindings.ContainsKey("a"));
        Assert.IsTrue(execution.SimpleApplications[0].Bindings.ContainsKey("b"));
        Assert.IsTrue(execution.SimpleApplications[0].Bindings.ContainsKey("c"));
        Assert.IsFalse(plan.PairsByClass.ContainsKey(plainEqualityRootId) && plan.PairsByClass[plainEqualityRootId][rules[0]].Any());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_SolvesArithmeticEqualityWithDirectHandledGpuRuleClass()
    {
        var x = new Symbol("x");
        var solver = new CobraSolverStrategy();
        var problem = new Equality(
            new Add(x, new Number(5)),
            new Number(10));
        var rules = ImmutableList.Create(
            new Rule(
                new Equality(
                    new Add(new Wild("a"), new Wild("b")),
                    new Wild("c")),
                new Equality(
                    new Wild("a"),
                    new Subtract(new Wild("c"), new Wild("b")))));
        var additionalData = ImmutableDictionary<string, object>.Empty
            .Add(SolverOptionKeys.CobraSkipCompatibilityForDirectHandledRules, true);
        var context = new SolveContext(
            targetVariable: x,
            rules: rules,
            maxIterations: 4,
            enableTracing: false,
            additionalData: additionalData,
            maxConcurrency: 1);

        var result = solver.SolveWithDiagnostics(problem, context, out var diagnostics);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);
        Assert.AreEqual(new Equality(x, new Number(5)).Canonicalize().ToDisplayString(), result.ResultExpression!.Canonicalize().ToDisplayString());
        Assert.AreEqual(0, diagnostics.FallbackPhaseCount);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaForEqualitySwapRules()
    {
        var graph = new EGraph();
        graph.Add(new Equality(new Symbol("y"), new Add(new Symbol("x"), new Number(5))));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Equality(new Wild("a"), new Wild("b")), new Equality(new Wild("b"), new Wild("a"))) { Name = "SwapEquality" }
        };

        var rootIds = graph.GetRootIds();
        int equalityRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Equality"));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass.ContainsKey(equalityRootId));
        Assert.IsTrue(plan.PairsByClass[equalityRootId][rules[0]].Any());
        Assert.IsFalse(compatibilityRulesByClass.ContainsKey(equalityRootId));
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_SolvesBenchmarkEquationWithGpuBenchmarkRuleSet()
    {
        var x = new Symbol("x");
        var y = new Symbol("y");
        var solver = new CobraSolverStrategy();
        var problem = new Equality(y, new Add(x, new Number(5)));
        var rules = ImmutableList.Create(
            new Rule(new Equality(new Wild("a"), new Wild("b")), new Equality(new Wild("b"), new Wild("a"))) { Name = "SwapEquality" },
            new Rule(
                new Equality(
                    new Add(new Wild("a"), new Wild("b")),
                    new Wild("c")),
                new Equality(
                    new Wild("a"),
                    new Subtract(new Wild("c"), new Wild("b")))) { Name = "IsolateAddLeft" });
        var additionalData = ImmutableDictionary<string, object>.Empty
            .Add(SolverOptionKeys.CobraSkipCompatibilityForDirectHandledRules, true);
        var context = new SolveContext(
            targetVariable: x,
            rules: rules,
            maxIterations: 4,
            enableTracing: false,
            additionalData: additionalData,
            maxConcurrency: 1);

        var result = solver.SolveWithDiagnostics(problem, context, out var diagnostics);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);
        Assert.AreEqual(new Equality(x, new Subtract(y, new Number(5)).Canonicalize()).Canonicalize().ToDisplayString(), result.ResultExpression!.Canonicalize().ToDisplayString());
        Assert.AreEqual(0, diagnostics.FallbackPhaseCount);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_BenchmarkArithmeticRuleSetLeavesNoCompatibilityForGeneratorShapes()
    {
        var y = new Symbol("y");
        var x = new Symbol("x");
        var equations = new IExpression[]
        {
            new Equality(y, new Add(x, new Number(5)).Canonicalize()),
            new Equality(y, new Add(new Number(5), x).Canonicalize()),
            new Equality(y, new Subtract(x, new Number(5)).Canonicalize()),
            new Equality(y, new Subtract(new Number(5), x).Canonicalize()),
            new Equality(y, new Multiply(x, new Number(5)).Canonicalize()),
            new Equality(y, new Multiply(new Number(5), x).Canonicalize()),
            new Equality(y, new Divide(x, new Number(5)).Canonicalize())
        };
        var rules = ImmutableList.Create(
            new Rule(new Equality(new Wild("a"), new Wild("b")), new Equality(new Wild("b"), new Wild("a"))) { Name = "SwapEquality" },
            new Rule(new Equality(new Add(new Wild("a"), new Wild("b")), new Wild("c")), new Equality(new Wild("a"), new Subtract(new Wild("c"), new Wild("b")))) { Name = "IsolateAddLeft" },
            new Rule(new Equality(new Add(new Wild("a"), new Wild("b")), new Wild("c")), new Equality(new Wild("b"), new Subtract(new Wild("c"), new Wild("a")))) { Name = "IsolateAddRight" },
            new Rule(new Equality(new Multiply(new Wild("a"), new Wild("b")), new Wild("c")), new Equality(new Wild("a"), new Divide(new Wild("c"), new Wild("b")))) { Name = "IsolateMulLeft" },
            new Rule(new Equality(new Multiply(new Wild("a"), new Wild("b")), new Wild("c")), new Equality(new Wild("b"), new Divide(new Wild("c"), new Wild("a")))) { Name = "IsolateMulRight" });

        foreach (var equation in equations)
        {
            var graph = new EGraph();
            graph.Add(equation);
            graph.Rebuild();

            var rootIds = graph.GetRootIds();
            int equalityRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Equality"));
            var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
            var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
            var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);

            if (compatibilityRulesByClass.TryGetValue(equalityRootId, out var remainingRules))
            {
                Assert.Fail($"Equation {equation.ToDisplayString()} still had compatibility rules: {string.Join(", ", remainingRules.Select(rule => rule.Name ?? rule.Pattern.ToDisplayString()))}");
            }
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraBenchmarkDiagnostics_ReportsNoRemainingCompatibilityRulesForBenchmarkEquation()
    {
        var x = new Symbol("x");
        var y = new Symbol("y");
        var additionalData = ImmutableDictionary<string, object>.Empty
            .Add(SolverOptionKeys.CobraSkipCompatibilityForDirectHandledRules, true)
            .Add(SolverOptionKeys.ArithmeticBenchmarkRuleProfile, true);
        var context = new SolveContext(targetVariable: x, additionalData: additionalData, maxConcurrency: 1);
        var rules = RuleProvider.BuildRules(context);
        var graph = new EGraph();
        graph.Add(new Equality(y, new Add(x, new Number(5))).Canonicalize());
        graph.Rebuild();

        var snapshot = CobraPlannerSnapshot.Create(graph);
        var frontierPlan = CobraFrontierPlanner.Build(graph, snapshot, regionPlan: null);
        var compatibilityPlan = CobraRuleCompatibilityPlanner.Build(graph, frontierPlan.OrderedClassIds, rules.ToList());
        var directPlan = CobraDirectMatchPlanner.Build(graph, snapshot, frontierPlan.OrderedClassIds, compatibilityPlan.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibilityPlan.RulesByClassId, directPlan);
        var compatibilityClassIds = CobraSolverStrategy.FilterFrontierClassIdsForCompatibility(frontierPlan.OrderedClassIds, compatibilityRulesByClass);
        var candidatePlan = CobraNodeMatchCandidatePlanner.Build(graph, snapshot, compatibilityClassIds, compatibilityRulesByClass);
        var filteredRulesByClass = CobraMatcher.FilterRulesByPositiveCandidates(compatibilityRulesByClass, candidatePlan.EligibleNodesByClass);
        var remainingRuleNames = filteredRulesByClass
            .SelectMany(static kvp => kvp.Value)
            .Select(rule => string.IsNullOrWhiteSpace(rule.Name) ? rule.Pattern.ToDisplayString() : rule.Name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Console.WriteLine("BENCHMARK-REMAINING-RULES: " + string.Join(", ", remainingRuleNames));
        Assert.AreEqual(0, remainingRuleNames.Length);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_Slice_PreservesPairsAndHandledRules()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Wild("x"), new Wild("y")), new Wild("x")),
            new(new Multiply(new Wild("m"), new Wild("n")), new Wild("m")),
            new(new Symbol("pi"), new Number(3))
        };

        var rootIds = graph.GetRootIds();
        int addRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node => node.Head == "Add"));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var fullPlan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);

        var slicedPlan = CobraDirectMatchPlanner.Slice(fullPlan, [addRootId]);

        Assert.AreEqual(fullPlan.Source, slicedPlan.Source);
        Assert.AreEqual(1, slicedPlan.PairsByClass.Count);
        CollectionAssert.AreEqual(
            fullPlan.PairsByClass[addRootId].Keys.Select(static rule => rule.Pattern.ToDisplayString()).ToArray(),
            slicedPlan.PairsByClass[addRootId].Keys.Select(static rule => rule.Pattern.ToDisplayString()).ToArray());
        if (fullPlan.ExhaustivelyHandledRulesByClass is not null &&
            fullPlan.ExhaustivelyHandledRulesByClass.TryGetValue(addRootId, out var handledRules))
        {
            Assert.IsTrue(slicedPlan.ExhaustivelyHandledRulesByClass is not null);
            CollectionAssert.AreEquivalent(
                handledRules.Select(static rule => rule.Pattern.ToDisplayString()).ToArray(),
                slicedPlan.ExhaustivelyHandledRulesByClass[addRootId].Select(static rule => rule.Pattern.ToDisplayString()).ToArray());
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaForUnaryStructuralEqualityRules()
    {
        var graph = new EGraph();
        graph.Add(new Equality(
            new Function("sin", new Symbol("x")),
            new Function("sin", new Symbol("y"))));
        graph.Add(new Equality(
            new Function("sin", new Symbol("x")),
            new Function("cos", new Symbol("y"))));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Equality(
                    new Function("sin", new Wild("a")),
                    new Function("sin", new Wild("b"))),
                new Equality(new Wild("a"), new Wild("b")))
        };

        var rootIds = graph.GetRootIds();
        int matchingRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Equality" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Func:sin") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Func:sin")));
        int nonMatchingRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Equality" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Func:sin") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Func:cos")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass.ContainsKey(matchingRootId));
        Assert.IsTrue(plan.PairsByClass[matchingRootId][rules[0]].Any());
        Assert.IsFalse(compatibilityRulesByClass.ContainsKey(matchingRootId));
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(matchingRootId, execution.SimpleApplications[0].RootClassId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaForVectorizingStructuralEqualityRules()
    {
        var graph = new EGraph();
        graph.Add(new Equality(
            new Add(new Symbol("a"), new Symbol("b")),
            new Add(new Symbol("c"), new Symbol("d"))));
        graph.Add(new Equality(
            new Add(new Symbol("a"), new Symbol("b")),
            new Multiply(new Symbol("c"), new Symbol("d"))));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Equality(
                    new Add(new Wild("a"), new Wild("b")),
                    new Add(new Wild("c"), new Wild("d"))),
                new Vector(
                    new Equality(new Wild("a"), new Wild("c")),
                    new Equality(new Wild("b"), new Wild("d"))))
        };

        var rootIds = graph.GetRootIds();
        int matchingRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Equality" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Add") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Add")));
        int nonMatchingRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Equality" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "Add") &&
            graph.GetClass(node.Children[1]).Nodes.Any(child => child.Head == "Mul")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass.ContainsKey(matchingRootId));
        Assert.IsTrue(plan.PairsByClass[matchingRootId][rules[0]].Any());
        Assert.IsFalse(compatibilityRulesByClass.ContainsKey(matchingRootId));
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(matchingRootId, execution.SimpleApplications[0].RootClassId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_UsesCudaForTwoLevelNestedStructuralRules()
    {
        var graph = new EGraph();
        graph.Add(new Relu(
            new MatMul(
                new Add(
                    new Symbol("a"),
                    new Symbol("b")),
                new Symbol("d"))));
        graph.Add(new Relu(
            new Symbol("s")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Relu(
                    new MatMul(
                        new Add(
                            new Wild("x"),
                            new Wild("c")),
                        new Wild("d"))),
                new Wild("d"))
        };

        var rootIds = graph.GetRootIds();
        int nestedRootId = rootIds.Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Relu" &&
            graph.GetClass(node.Children[0]).Nodes.Any(child =>
                child.Head == "MatMul" &&
                graph.GetClass(child.Children[0]).Nodes.Any(grandChild => grandChild.Head == "Add"))));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);
        var execution = CobraDirectMatchMaterializer.MaterializeForExecution(graph, plan);

        Assert.AreEqual(CobraDirectMatchSource.Cuda, plan.Source);
        Assert.IsTrue(plan.PairsByClass.ContainsKey(nestedRootId));
        Assert.IsTrue(plan.PairsByClass[nestedRootId].ContainsKey(rules[0]));
        Assert.IsTrue(plan.PairsByClass[nestedRootId][rules[0]].Any());
        Assert.IsFalse(compatibilityRulesByClass.ContainsKey(nestedRootId));
        Assert.AreEqual(1, execution.SimpleApplications.Count);
        Assert.AreEqual(rules[0], execution.SimpleApplications[0].Rule);
        Assert.AreEqual(nestedRootId, execution.SimpleApplications[0].RootClassId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchPlanner_LeavesTooDeepRulesOnCompatibilityPath()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(
                new Add(
                    new Multiply(
                        new Transpose(new Relu(new Symbol("a"))),
                        new Symbol("b")),
                    new Symbol("c")),
                new Symbol("d")),
            new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Add(
                    new MatMul(
                        new Add(
                            new Multiply(
                                new Transpose(new Relu(new Wild("x"))),
                                new Wild("b")),
                            new Wild("c")),
                        new Wild("d")),
                    new Wild("r")),
                new Wild("r"))
        };

        int rootId = graph.GetRootIds().Single(id => graph.GetClass(id).Nodes.Any(node =>
            node.Head == "Add" &&
            node.Children.Count == 2 &&
            graph.GetClass(node.Children[0]).Nodes.Any(child => child.Head == "MatMul")));
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, graph.GetRootIds(), rules);
        var plan = CobraDirectMatchPlanner.Build(graph, graph.GetRootIds(), compatibility.RulesByClassId);
        var compatibilityRulesByClass = CobraRulePartitioner.BuildCompatibilityRulesByClass(compatibility.RulesByClassId, plan);

        Assert.IsFalse(plan.PairsByClass.ContainsKey(rootId));
        Assert.IsTrue(compatibilityRulesByClass.ContainsKey(rootId));
        CollectionAssert.Contains(compatibilityRulesByClass[rootId].ToArray(), rules[0]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraDirectMatchMaterializer_UsesCudaToOrderHotPairsFirst()
    {
        var graph = new EGraph();
        int low = graph.Add(new Add(new Number(0), new Symbol("x")));
        int high = graph.Add(new Add(new Number(0), new Symbol("y")));
        graph.Rebuild();
        graph.GetClass(graph.Find(high)).Generation = 22;

        var rules = new List<Rule>
        {
            new(new Add(new Number(0), new Wild("x")), new Wild("x"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var plan = CobraDirectMatchPlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var matches = CobraDirectMatchMaterializer.Materialize(graph, plan);

        Assert.IsTrue(matches.Count >= 2);
        Assert.AreEqual(graph.Find(high), matches[0].RootClassId);
        CollectionAssert.Contains(matches.Select(match => match.RootClassId).ToArray(), graph.Find(low));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraMatchCompatibilityPath_MatchesNestedRulesEquivalentToLegacyMatcher()
    {
        var graph = new EGraph();
        graph.Add(new Add(
            new MatMul(
                new Add(new Number(0), new Symbol("a")),
                new Symbol("b")),
            new Symbol("y")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(
                new Add(
                    new MatMul(
                        new Add(new Number(0), new Wild("x")),
                        new Wild("m")),
                    new Wild("r")),
                new Wild("r"))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var candidates = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var history = new MatchHistory();

        var legacyMatches = EGraphMatcher.FindMatches(
            graph,
            rules,
            rootIds,
            compatibility.RulesByClassId,
            candidates.EligibleNodesByClass,
            history,
            maxConcurrency: 1,
            ct: default);

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(graph);
        history.Clear();

        var cobraMatches = CobraMatchCompatibilityPath.FindMatches(
            cobraGraphState,
            graph,
            rules,
            rootIds,
            compatibility.RulesByClassId,
            candidates.EligibleNodesByClass,
            history,
            maxConcurrency: 1,
            ct: default);

        CollectionAssert.AreEqual(
            legacyMatches.Select(ToMatchKey).ToList(),
            cobraMatches.Select(ToMatchKey).ToList());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraMatchCompatibilityPath_MatchesRepeatedWildcardsEquivalentToLegacyMatcher()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("z"), new Symbol("z")));
        graph.Add(new Add(new Symbol("z"), new Symbol("w")));
        graph.Rebuild();

        var rules = new List<Rule>
        {
            new(new Add(new Wild("x"), new Wild("x")), new Multiply(new Number(2), new Wild("x")))
        };

        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, rules);
        var candidates = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var history = new MatchHistory();

        var legacyMatches = EGraphMatcher.FindMatches(
            graph,
            rules,
            rootIds,
            compatibility.RulesByClassId,
            candidates.EligibleNodesByClass,
            history,
            maxConcurrency: 1,
            ct: default);

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(graph);
        history.Clear();

        var cobraMatches = CobraMatchCompatibilityPath.FindMatches(
            cobraGraphState,
            graph,
            rules,
            rootIds,
            compatibility.RulesByClassId,
            candidates.EligibleNodesByClass,
            history,
            maxConcurrency: 1,
            ct: default);

        CollectionAssert.AreEqual(
            legacyMatches.Select(ToMatchKey).ToList(),
            cobraMatches.Select(ToMatchKey).ToList());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraMatchCompatibilityPath_DoesNotEnumerateGlobalRulesWhenPlannedRulesAreProvided()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("x"), new Symbol("y")));
        graph.Rebuild();

        var plannedRule = new Rule(new Add(new Wild("x"), new Wild("y")), new Wild("x"));
        var rootIds = graph.GetRootIds();
        var compatibility = CobraRuleCompatibilityPlanner.Build(graph, rootIds, [plannedRule]);
        var candidates = CobraNodeMatchCandidatePlanner.Build(graph, rootIds, compatibility.RulesByClassId);
        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(graph);

        var matches = CobraMatchCompatibilityPath.FindMatches(
            cobraGraphState,
            graph,
            new ThrowingEnumerable<Rule>(),
            rootIds,
            compatibility.RulesByClassId,
            candidates.EligibleNodesByClass,
            new MatchHistory(),
            maxConcurrency: 1,
            ct: default);

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual(plannedRule, matches[0].Rule);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRebuildPreparationPlanner_UsesCudaForRebuildOrdering()
    {
        var graph = new EGraph();
        int addId = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int mulId = graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        graph.Union(addId, mulId);
        graph.Rebuild();

        var plan = CobraRebuildPreparationPlanner.Build(graph);

        Assert.AreEqual(CobraRebuildPreparationSource.Cuda, plan.Source);
        Assert.AreEqual(graph.GetRootIds().Count, plan.OrderedClassIds.Count);
        CollectionAssert.AreEquivalent(graph.GetRootIds(), plan.OrderedClassIds.ToList());
    }

    private sealed class ThrowingEnumerable<T> : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator()
        {
            throw new InvalidOperationException("This enumerable should not be iterated.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraAnalysisPreparationPlanner_UsesCudaForAnalysisOrdering()
    {
        var graph = new EGraph();
        graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Add(new Symbol("x", new Shape(ImmutableArray.Create(2, 2))));
        graph.Rebuild();

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(graph);
        var plan = CobraAnalysisPreparationPlanner.Build(cobraGraphState);
        var snapshot = CobraPlannerSnapshot.Create(cobraGraphState);

        Assert.AreEqual(CobraAnalysisPreparationSource.Cuda, plan.Source);
        Assert.AreEqual(snapshot.RootIds.Length, plan.OrderedClassIds.Count);
        CollectionAssert.AreEquivalent(snapshot.RootIds, plan.OrderedClassIds.ToList());
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraExtractionPlanner_UsesCudaForExtractionOrderingWithoutExplicitGraphWarmup()
    {
        var graph = new EGraph();
        int simple = graph.Add(new Number(2));
        int complex = graph.Add(new Add(new MatMul(new Symbol("A"), new Symbol("B")), new Symbol("x")));
        graph.Rebuild();
        graph.GetClass(graph.Find(complex)).Generation = 20;

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(graph);

        var plan = CobraExtractionPlanner.Build(cobraGraphState);

        Assert.AreEqual(CobraExtractionSource.Cuda, plan.Source);
        Assert.AreEqual(cobraGraphState.Find(simple), plan.OrderedClassIds[0]);
        Assert.IsTrue(plan.OrderedNodesByClass.ContainsKey(cobraGraphState.Find(simple)));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraExtractor_ExtractBestEffort_UsesAuthoritativeGraphStateForUnsyncedExpressions()
    {
        var legacyGraph = new EGraph();
        legacyGraph.Add(new Symbol("x"));
        legacyGraph.Add(new Symbol("y"));
        legacyGraph.Rebuild();

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);

        int rootId = cobraGraphState.AddExpression(new Add(new Symbol("x"), new Symbol("y")));
        var diagnostics = new CobraDiagnostics();

        var extracted = CobraExtractor.ExtractBestEffort(
            cobraGraphState,
            rootId,
            null,
            static _ => 1,
            [],
            new Dictionary<int, IReadOnlyList<int>>(),
            default,
            default,
            diagnostics);

        Assert.AreEqual("(x + y)", extracted.ToDisplayString());
        Assert.AreEqual(0, diagnostics.ExtractionFallbackCount);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraExtractor_ExtractBest_UsesAuthoritativeGraphStateForUnsyncedCompositeClasses()
    {
        var legacyGraph = new EGraph();
        legacyGraph.Add(new Symbol("x"));
        legacyGraph.Add(new Symbol("y"));
        legacyGraph.Rebuild();

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);

        int rootId = cobraGraphState.AddExpression(new Add(new Symbol("x"), new Symbol("y")));
        var diagnostics = new CobraDiagnostics();

        var extracted = CobraExtractor.ExtractBest(
            cobraGraphState,
            rootId,
            CancellationToken.None,
            diagnostics);

        Assert.AreEqual("(x + y)", extracted.ToDisplayString());
        Assert.AreEqual(0, diagnostics.ExtractionFallbackCount);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraGraphState_SyncClassMetadataFromLegacy_UpdatesShapeAndGenerationWithoutReplacingNodes()
    {
        var legacyGraph = new EGraph();
        int classId = legacyGraph.Add(new Symbol("M"));
        legacyGraph.Rebuild();
        int rootId = legacyGraph.Find(classId);
        legacyGraph.GetClass(rootId).Data = new Shape(ImmutableArray.Create(2, 2));
        legacyGraph.GetClass(rootId).Generation = 17;
        legacyGraph.GetClass(rootId).Metadata["tag"] = 9d;

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);
        int nodeCountBefore = cobraGraphState.GetClass(rootId).NodeIds.Count;

        legacyGraph.GetClass(rootId).Data = new Shape(ImmutableArray.Create(4, 4));
        legacyGraph.GetClass(rootId).Generation = 23;
        legacyGraph.GetClass(rootId).Metadata["tag"] = 11d;

        cobraGraphState.SyncClassMetadataFromLegacy(legacyGraph, [rootId]);

        var cobraClass = cobraGraphState.GetClass(rootId);
        Assert.AreEqual(nodeCountBefore, cobraClass.NodeIds.Count);
        Assert.AreEqual(23, cobraClass.Generation);
        Assert.AreEqual(11d, (double)cobraClass.Metadata["tag"]);
        Assert.AreEqual(new Shape(ImmutableArray.Create(4, 4)), cobraClass.Metadata["shape"]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_PostUnionMaintenance_UsesSingleDecisionWhenInequalitiesAndGraphChangesOverlap()
    {
        Assert.IsTrue(CobraSolverStrategy.RequiresPostUnionMaintenance(graphChanged: true, resolvedInequalities: true));
        Assert.IsTrue(CobraSolverStrategy.RequiresPostUnionMaintenance(graphChanged: true, resolvedInequalities: false));
        Assert.IsTrue(CobraSolverStrategy.RequiresPostUnionMaintenance(graphChanged: false, resolvedInequalities: true));
        Assert.IsFalse(CobraSolverStrategy.RequiresPostUnionMaintenance(graphChanged: false, resolvedInequalities: false));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_DiscoverPendingEqualityUnions_UsesAuthoritativeCobraGraphState()
    {
        var legacyGraph = new EGraph();
        int xId = legacyGraph.Add(new Symbol("x"));
        int yId = legacyGraph.Add(new Symbol("y"));
        legacyGraph.Rebuild();

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);
        cobraGraphState.AddExpression(new Equality(new Symbol("x"), new Symbol("y")));

        var pendingEqualityUnions = CobraSolverStrategy.DiscoverPendingEqualityUnions(cobraGraphState);

        Assert.AreEqual(1, pendingEqualityUnions.Count);
        Assert.AreEqual(cobraGraphState.Find(xId), cobraGraphState.Find(pendingEqualityUnions[0].LeftId));
        Assert.AreEqual(cobraGraphState.Find(yId), cobraGraphState.Find(pendingEqualityUnions[0].RightId));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_ExtractEqualityUnions_FindsPendingPairs()
    {
        Assert.IsTrue(CobraCudaNative.TryExtractEqualityUnions(
            [6, 1],
            [0, 2],
            [2, 0],
            [4, 9],
            out var leftIds,
            out var rightIds));

        CollectionAssert.AreEqual(new[] { 4 }, leftIds);
        CollectionAssert.AreEqual(new[] { 9 }, rightIds);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_SimpleCompatibilityRewrite_SkipsBindingMaterialization()
    {
        var graphState = new CobraGraphState();
        int rootId = graphState.AddExpression(new Add(
            new MatMul(
                new Add(new Number(0), new Symbol("a")),
                new Symbol("b")),
            new Symbol("y")));
        int targetId = graphState.AddExpression(new Symbol("y"));
        var rule = new Rule(
            new Add(
                new MatMul(
                    new Add(new Number(0), new Wild("x")),
                    new Wild("m")),
                new Wild("r")),
            new Wild("r"));
        var match = new Match(
            rule,
            rootId,
            ImmutableDictionary<string, int>.Empty
                .Add("x", graphState.AddExpression(new Symbol("a")))
                .Add("m", graphState.AddExpression(new Symbol("b")))
                .Add("r", targetId));
        var diagnostics = new CobraDiagnostics();
        var pendingRuleUnions = new List<(int LeftId, int RightId)>();

        bool handled = CobraSolverStrategy.TryInstantiateSimpleRewriteMatch(graphState, match, pendingRuleUnions, diagnostics);

        Assert.IsTrue(handled);
        Assert.AreEqual(0, diagnostics.BindingMaterializationCount);
        Assert.AreEqual(1, diagnostics.SkippedSimpleBindingMaterializationCount);
        Assert.AreEqual(1, pendingRuleUnions.Count);
        Assert.AreEqual(graphState.Find(rootId), graphState.Find(pendingRuleUnions[0].LeftId));
        Assert.AreEqual(graphState.Find(targetId), graphState.Find(pendingRuleUnions[0].RightId));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_SimpleDirectApplication_WithArbitraryCondition_MaterializesBindingsOnce()
    {
        var legacyGraph = new EGraph();
        int rootId = legacyGraph.Add(new Add(new Number(0), new Symbol("x")));
        legacyGraph.Rebuild();

        int canonicalRootId = legacyGraph.Find(rootId);
        var addNode = legacyGraph.GetClass(canonicalRootId).Nodes.Single(node => node.Head == "Add");
        int xId = legacyGraph.Find(addNode.Children[1]);

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);

        var rule = new Rule(
            new Add(new Number(0), new Wild("x")),
            new Wild("x"),
            condition: bindings => bindings.TryGetValue("x", out var value) &&
                                   value is Symbol symbol &&
                                   symbol.Name == "x");
        var application = new CobraDirectRuleApplication(
            canonicalRootId,
            rule,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["x"] = xId
            });
        var context = new SolveContext(maxIterations: 1, maxConcurrency: 1, sharedEGraph: legacyGraph);
        var currentCostFunction = CostModelFactory.GetCostModel(context).GetCostFunction(context, legacyGraph);
        var diagnostics = new CobraDiagnostics();
        var pendingRuleUnions = new List<(int LeftId, int RightId)>();
        var bindingExtractionCache = new Dictionary<int, IExpression>();
        var bindingDictionaryCache = new Dictionary<string, ImmutableDictionary<string, IExpression>>(StringComparer.Ordinal);

        bool handled = CobraSolverStrategy.TryInstantiateSimpleDirectApplication(
            legacyGraph,
            context,
            0,
            currentCostFunction,
            cobraGraphState,
            application,
            pendingRuleUnions,
            diagnostics,
            bindingExtractionCache,
            bindingDictionaryCache);

        Assert.IsTrue(handled);
        Assert.IsTrue(diagnostics.BindingMaterializationCount > 0);
        Assert.AreEqual(0, diagnostics.SkippedSimpleBindingMaterializationCount);
        Assert.AreEqual(1, pendingRuleUnions.Count);
        Assert.AreEqual(cobraGraphState.Find(canonicalRootId), cobraGraphState.Find(pendingRuleUnions[0].LeftId));
        Assert.AreEqual(cobraGraphState.Find(xId), cobraGraphState.Find(pendingRuleUnions[0].RightId));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_SolvesAssumptionConditionRuleOnDirectPath()
    {
        var x = new Symbol("x");
        var solver = new CobraSolverStrategy();
        var problem = new Divide(x, x);
        var rules = ImmutableList.Create(
            new Rule(
                new Divide(new Wild("x"), new Wild("x")),
                new Number(1),
                assumptionCondition: (bindings, assumptions) =>
                    bindings.TryGetValue("x", out var value) &&
                    value is Symbol symbol &&
                    assumptions.IsPositive(symbol.Name)));
        var additionalData = ImmutableDictionary<string, object>.Empty
            .Add(SolverOptionKeys.CobraSkipCompatibilityForDirectHandledRules, true)
            .Add("AssumePositive", new[] { "x" });
        var context = new SolveContext(
            rules: rules,
            maxIterations: 3,
            enableTracing: false,
            additionalData: additionalData,
            maxConcurrency: 1);

        var result = solver.SolveWithDiagnostics(problem, context, out var diagnostics);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsNotNull(result.ResultExpression);
        Assert.AreEqual(new Number(1).Canonicalize().ToDisplayString(), result.ResultExpression!.Canonicalize().ToDisplayString());
        Assert.IsTrue(diagnostics.BindingMaterializationCount > 0);
        Assert.AreEqual(0, diagnostics.FallbackPhaseCount);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_WithNoIterations_UsesMetadataSyncInsteadOfFinalFullRefresh()
    {
        var solver = new CobraSolverStrategy();
        var problem = new Symbol("x");
        var context = new SolveContext(maxIterations: 0, maxConcurrency: 1);

        _ = solver.SolveWithDiagnostics(problem, context, out var diagnostics);

        Assert.AreEqual(1, diagnostics.FullSyncCount);
        Assert.AreEqual(0, diagnostics.IncrementalSyncCount);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_ResolveBindingExpression_ReusesPerIterationCache()
    {
        var legacyGraph = new EGraph();
        int classId = legacyGraph.Add(new Add(new Symbol("x"), new Number(1)));
        legacyGraph.Rebuild();

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);
        var diagnostics = new CobraDiagnostics();
        var cache = new Dictionary<int, IExpression>();
        var context = new SolveContext(maxIterations: 1, maxConcurrency: 1, sharedEGraph: legacyGraph);
        Func<ENode, long> costFunction = static _ => 1;

        IExpression first = CobraSolverStrategy.ResolveBindingExpression(
            classId,
            legacyGraph,
            context,
            costFunction,
            cobraGraphState,
            diagnostics,
            cache,
            CancellationToken.None);

        IExpression second = CobraSolverStrategy.ResolveBindingExpression(
            classId,
            legacyGraph,
            context,
            costFunction,
            cobraGraphState,
            diagnostics,
            cache,
            CancellationToken.None);

        Assert.AreEqual(first.ToDisplayString(), second.ToDisplayString());
        Assert.AreEqual(1, diagnostics.BindingExtractionCacheMissCount);
        Assert.AreEqual(1, diagnostics.BindingExtractionCacheHitCount);
        Assert.AreEqual(1, cache.Count);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraSolverStrategy_TryMaterializeBindings_ReusesBindingDictionaryCache()
    {
        var legacyGraph = new EGraph();
        int left = legacyGraph.Add(new Symbol("x"));
        int right = legacyGraph.Add(new Number(1));
        int add = legacyGraph.Add(new Add(new Symbol("x"), new Number(1)));
        legacyGraph.Rebuild();

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);
        var diagnostics = new CobraDiagnostics();
        var expressionCache = new Dictionary<int, IExpression>();
        var bindingCache = new Dictionary<string, ImmutableDictionary<string, IExpression>>(StringComparer.Ordinal);
        var context = new SolveContext(maxIterations: 1, maxConcurrency: 1, sharedEGraph: legacyGraph);
        Func<ENode, long> costFunction = static _ => 1;
        var rule = new Rule(new Add(new Wild("a"), new Wild("b")), new Wild("a"));
        var bindings = ImmutableDictionary<string, int>.Empty
            .Add("a", legacyGraph.Find(left))
            .Add("b", legacyGraph.Find(right));
        var match1 = new Match(rule, legacyGraph.Find(add), bindings);
        var match2 = new Match(rule, legacyGraph.Find(add), bindings);

        bool firstOk = CobraSolverStrategy.TryMaterializeBindings(
            legacyGraph,
            context,
            extractionTimeoutSeconds: 0,
            costFunction,
            match1,
            out var materialized1,
            cobraGraphState,
            diagnostics,
            expressionCache,
            bindingCache);

        bool secondOk = CobraSolverStrategy.TryMaterializeBindings(
            legacyGraph,
            context,
            extractionTimeoutSeconds: 0,
            costFunction,
            match2,
            out var materialized2,
            cobraGraphState,
            diagnostics,
            expressionCache,
            bindingCache);

        Assert.IsTrue(firstOk);
        Assert.IsTrue(secondOk);
        Assert.AreEqual(materialized1["a"].ToDisplayString(), materialized2["a"].ToDisplayString());
        Assert.AreEqual(1, diagnostics.BindingDictionaryCacheMissCount);
        Assert.AreEqual(1, diagnostics.BindingDictionaryCacheHitCount);
        Assert.AreEqual(1, diagnostics.BindingMaterializationCount);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRepairCandidatePlanner_UsesCudaToMarkDirtyNodes()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int add = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Union(a, b);

        var plan = CobraRepairCandidatePlanner.Build(graph);

        Assert.AreEqual(CobraRepairCandidateSource.Cuda, plan.Source);
        Assert.IsTrue(plan.Candidates.Any(candidate => candidate.ClassId == graph.Find(add)));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRepairCandidatePlanner_UsesCudaToOrderHotCandidatesFirst()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int hotAdd = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int coldMul = graph.Add(new Multiply(new Symbol("a"), new Symbol("b")));
        graph.Union(a, b);
        graph.GetClass(graph.Find(hotAdd)).Generation = 30;
        graph.GetClass(graph.Find(coldMul)).Generation = 3;

        var plan = CobraRepairCandidatePlanner.Build(graph);

        Assert.AreEqual(CobraRepairCandidateSource.Cuda, plan.Source);
        Assert.IsTrue(plan.Candidates.Count > 0);
        Assert.AreEqual(graph.Find(hotAdd), plan.Candidates[0].ClassId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRepairCandidatePlanner_UsesCudaToBoostBoundaryCandidates()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int boundary = graph.Add(new Transpose(new Add(new Symbol("a"), new Symbol("b"))));
        int plain = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Union(a, b);
        graph.GetClass(graph.Find(boundary)).Generation = 1;
        graph.GetClass(graph.Find(plain)).Generation = 1;

        var plan = CobraRepairCandidatePlanner.Build(graph);

        Assert.AreEqual(CobraRepairCandidateSource.Cuda, plan.Source);
        Assert.IsTrue(plan.Candidates.Count > 0);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRepairCandidatePlanner_CanUseAuthoritativeCobraGraphState()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int add = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        graph.Rebuild();

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(graph);
        cobraGraphState.Union(a, b);
        CobraCudaNative.TryWarmGraphCaches(cobraGraphState);

        var plan = CobraRepairCandidatePlanner.Build(cobraGraphState, CobraPlannerSnapshot.Create(cobraGraphState));

        Assert.AreEqual(CobraRepairCandidateSource.Cuda, plan.Source);
        Assert.IsTrue(plan.Candidates.Any(candidate => candidate.ClassId == cobraGraphState.Find(add)));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRepairApplicationPlanner_UsesCudaToGroupCanonicalRepairTargets()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int addMixed = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int addRepeated = graph.Add(new Add(new Symbol("b"), new Symbol("b")));
        graph.Union(a, b);

        var candidatePlan = CobraRepairCandidatePlanner.Build(graph);
        var applicationPlan = CobraRepairApplicationPlanner.Build(graph, candidatePlan);

        Assert.AreEqual(CobraRepairApplicationSource.Cuda, applicationPlan.Source);
        Assert.IsTrue(applicationPlan.Groups.Any(group =>
            group.Candidates.Count >= 2 &&
            group.Candidates.Any(candidate => candidate.ClassId == graph.Find(addMixed)) &&
            group.Candidates.Any(candidate => candidate.ClassId == graph.Find(addRepeated))));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraCudaNative_HashesEquivalentRepairTargetsConsistently()
    {
        int[] headHashes =
        [
            StringComparer.Ordinal.GetHashCode("Add"),
            StringComparer.Ordinal.GetHashCode("Add"),
            StringComparer.Ordinal.GetHashCode("Multiply")
        ];
        int[] childStarts = [0, 2, 4];
        int[] childCounts = [2, 2, 2];
        int[] canonicalChildIds = [3, 5, 3, 5, 3, 7];

        Assert.IsTrue(CobraCudaNative.TryHashRepairTargets(headHashes, childStarts, childCounts, canonicalChildIds, out var hashes));
        Assert.AreEqual(hashes[0], hashes[1]);
        Assert.AreNotEqual(hashes[0], hashes[2]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRepairApplicationPlanner_GuidesGroupedRebuildRepair()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int addMixed = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int addRepeated = graph.Add(new Add(new Symbol("b"), new Symbol("b")));
        graph.Union(a, b);

        var candidatePlan = CobraRepairCandidatePlanner.Build(graph);
        var applicationPlan = CobraRepairApplicationPlanner.Build(graph, candidatePlan);
        graph.Rebuild(null, applicationPlan.OrderedCandidates);

        int repairedRoot = graph.Find(addMixed);
        var repairedClass = graph.GetClass(repairedRoot);

        Assert.IsTrue(repairedClass.Nodes.Any(node =>
            node.Head == "Add" &&
            node.Children.Count == 2 &&
            node.Children[0] == node.Children[1]));
        Assert.AreEqual(repairedRoot, graph.Find(addRepeated));
    }

    [TestMethod]
    [Timeout(10000)]
    public void EGraph_RebuildWithRepairGroups_PreservesCudaPlannedGrouping()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int addMixed = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int addRepeated = graph.Add(new Add(new Symbol("b"), new Symbol("b")));
        graph.Union(a, b);

        var candidatePlan = CobraRepairCandidatePlanner.Build(graph);
        var applicationPlan = CobraRepairApplicationPlanner.Build(graph, candidatePlan);
        graph.Rebuild(
            prioritizedRootIds: null,
            exactCandidateGroups: applicationPlan.GroupCandidates);

        int repairedRoot = graph.Find(addMixed);
        var repairedClass = graph.GetClass(repairedRoot);

        Assert.IsTrue(repairedClass.Nodes.Any(node =>
            node.Head == "Add" &&
            node.Children.Count == 2 &&
            node.Children[0] == node.Children[1]));
        Assert.AreEqual(repairedRoot, graph.Find(addRepeated));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRebuildEngine_RebuildsAuthoritativeGraphStateWithoutLegacyRefresh()
    {
        var legacyGraph = new EGraph();
        int a = legacyGraph.Add(new Symbol("a"));
        int b = legacyGraph.Add(new Symbol("b"));
        int addMixed = legacyGraph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int addRepeated = legacyGraph.Add(new Add(new Symbol("b"), new Symbol("b")));
        legacyGraph.Union(a, b);

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);
        var repairSnapshot = CobraPlannerSnapshot.Create(cobraGraphState);
        var repairPlan = CobraRepairCandidatePlanner.Build(cobraGraphState, repairSnapshot);
        var repairApplicationPlan = CobraRepairApplicationPlanner.Build(cobraGraphState, repairSnapshot, repairPlan);
        var rebuildPlan = CobraRebuildPreparationPlanner.Build(
            cobraGraphState,
            repairSnapshot.RootIds,
            repairSnapshot.NodeCounts,
            repairSnapshot.Generations,
            repairPlan);

        CobraRebuildEngine.Rebuild(
            cobraGraphState,
            legacyGraph,
            rebuildPlan.OrderedClassIds,
            repairApplicationPlan.GroupCandidates,
            System.Threading.CancellationToken.None);

        int cobraRepairedRoot = cobraGraphState.Find(addMixed);
        var cobraRepairedClass = cobraGraphState.GetClass(cobraRepairedRoot);
        Assert.IsTrue(cobraRepairedClass.NodeIds
            .Select(cobraGraphState.GetNode)
            .Any(node => node.Head == "Add" &&
                         node.CanonicalChildIds.Length == 2 &&
                         node.CanonicalChildIds[0] == node.CanonicalChildIds[1]));
        Assert.AreEqual(cobraRepairedRoot, cobraGraphState.Find(addRepeated));
        Assert.AreEqual(cobraGraphState.NodeCount, cobraGraphState.SyncState.LastLegacySyncedNodeCount);
        Assert.AreEqual(0, cobraGraphState.SyncState.DirtyLegacyClassIds.Count);

        var legacySnapshot = CobraPlannerSnapshot.Create(legacyGraph);
        var cobraSnapshot = CobraPlannerSnapshot.Create(cobraGraphState);
        CollectionAssert.AreEqual(legacySnapshot.RootIds, cobraSnapshot.RootIds);
        CollectionAssert.AreEqual(legacySnapshot.NodeCounts, cobraSnapshot.NodeCounts);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRebuildPreparationPlanner_UsesRepairPressureToBoostHotClasses()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int hotAdd = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int coldMul = graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        graph.Union(a, b);
        graph.GetClass(graph.Find(hotAdd)).Generation = 20;
        graph.GetClass(graph.Find(coldMul)).Generation = 1;

        var repairPlan = CobraRepairCandidatePlanner.Build(graph);
        var rebuildPlan = CobraRebuildPreparationPlanner.Build(graph, repairPlan);

        Assert.AreEqual(CobraRebuildPreparationSource.Cuda, rebuildPlan.Source);
        Assert.AreEqual(graph.Find(hotAdd), rebuildPlan.OrderedClassIds[0]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraRebuildPreparationPlanner_UsesCobraGraphStateRootsWhenLegacyGraphIsStale()
    {
        var legacyGraph = new EGraph();
        int a = legacyGraph.Add(new Symbol("a"));
        int b = legacyGraph.Add(new Symbol("b"));
        int hotAdd = legacyGraph.Add(new Add(new Symbol("a"), new Symbol("b")));
        legacyGraph.Rebuild();

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(legacyGraph);
        cobraGraphState.Union(a, b);

        var snapshot = CobraPlannerSnapshot.Create(cobraGraphState);
        var repairPlan = CobraRepairCandidatePlanner.Build(cobraGraphState, snapshot);
        var rebuildPlan = CobraRebuildPreparationPlanner.Build(cobraGraphState, snapshot, repairPlan);

        CollectionAssert.AreEquivalent(snapshot.RootIds, rebuildPlan.OrderedClassIds.ToArray());
        Assert.AreNotEqual(legacyGraph.GetRootIds().Count, rebuildPlan.OrderedClassIds.Count);
        Assert.IsTrue(rebuildPlan.OrderedClassIds.Contains(cobraGraphState.Find(hotAdd)));
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraAnalysisPreparationPlanner_UsesRepairPressureToBoostHotClasses()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Symbol("b"));
        int hotAdd = graph.Add(new Add(new Symbol("a"), new Symbol("b")));
        int coldMul = graph.Add(new Multiply(new Symbol("c"), new Symbol("d")));
        graph.Union(a, b);
        graph.GetClass(graph.Find(hotAdd)).Generation = 20;
        graph.GetClass(graph.Find(coldMul)).Generation = 1;

        var cobraGraphState = new CobraGraphState();
        cobraGraphState.SyncFromLegacyGraph(graph);
        var repairPlan = CobraRepairCandidatePlanner.Build(cobraGraphState);
        var analysisPlan = CobraAnalysisPreparationPlanner.Build(cobraGraphState, repairPlan);

        Assert.AreEqual(CobraAnalysisPreparationSource.Cuda, analysisPlan.Source);
        Assert.AreEqual(cobraGraphState.Find(hotAdd), analysisPlan.OrderedClassIds[0]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CobraUnionPreparationPlanner_PrepareWithGraph_UsesCudaToOrderHotPreparedUnionsFirst()
    {
        var graph = new EGraph();
        int a = graph.Add(new Symbol("a"));
        int b = graph.Add(new Add(new Symbol("x"), new Symbol("y")));
        int c = graph.Add(new Symbol("c"));
        int d = graph.Add(new Multiply(new Symbol("m"), new Symbol("n")));
        graph.Rebuild();
        graph.GetClass(graph.Find(b)).Generation = 40;
        graph.GetClass(graph.Find(d)).Generation = 2;

        var result = CobraUnionPreparationPlanner.Prepare(graph, [(a, b), (c, d)]);

        Assert.AreEqual(CobraUnionPreparationSource.Cuda, result.PreparationSource);
        Assert.AreEqual(graph.Find(a), result.PreparedUnions[0].LeftId);
        Assert.AreEqual(graph.Find(b), result.PreparedUnions[0].RightId);
    }

    [TestMethod]
    [Timeout(10000)]
    public void SolveWithEGraph_CobraBackend_AppliesPreparedUnionsAndSolves()
    {
        string script = @"
<Options>
  EGraphBackend: Cobra
  Target: x
  MaxIterations: 8
</Options>
(A * B) * x + (A * C) * x = y
";

        var wrapper = new ProblemScriptEGraphWrapper();
        string result = wrapper.SolveWithEGraph(script);

        Assert.IsFalse(result.StartsWith("Error:", StringComparison.Ordinal), result);
        Assert.IsTrue(result.Contains("x", StringComparison.Ordinal), result);
    }

    [TestMethod]
    [Timeout(10000)]
    public void EGraph_UnionBatch_PreservesMetadataAndShape()
    {
        var graph = new EGraph();
        var matrixShape = new Shape(ImmutableArray.Create(2, 2));
        int matrix = graph.Add(new Symbol("M", matrixShape));
        int generic = graph.Add(new Symbol("N"));
        graph.GetClass(graph.Find(generic)).Metadata["tag"] = 7d;

        bool changed = graph.UnionBatch([new[] { generic, matrix }]);

        Assert.IsTrue(changed);
        int root = graph.Find(matrix);
        Assert.AreEqual(root, graph.Find(generic));
        Assert.AreEqual(7d, (double)graph.GetClass(root).Metadata["tag"]);
        Assert.AreEqual(matrixShape, graph.GetClass(root).Data as Shape);
    }

    [TestMethod]
    [Timeout(10000)]
    public void EGraph_UnionBatch_UsesRequestedAnchorOrder()
    {
        var graph = new EGraph();
        int anchor = graph.Add(new Add(new Symbol("x"), new Symbol("y")));
        int other1 = graph.Add(new Symbol("a"));
        int other2 = graph.Add(new Symbol("b"));
        graph.GetClass(graph.Find(anchor)).Metadata["anchor-tag"] = 11d;

        bool changed = graph.UnionBatch(
            [new[] { other1, anchor, other2 }],
            [anchor]);

        Assert.IsTrue(changed);
        int root = graph.Find(anchor);
        Assert.AreEqual(root, graph.Find(other1));
        Assert.AreEqual(root, graph.Find(other2));
        Assert.AreEqual(11d, (double)graph.GetClass(root).Metadata["anchor-tag"]);
    }

    [TestMethod]
    [Timeout(10000)]
    public void EGraph_UnionBatch_CanonicalPath_UsesRequestedAnchorOrder()
    {
        var graph = new EGraph();
        int anchor = graph.Add(new Add(new Symbol("x"), new Symbol("y")));
        int other1 = graph.Add(new Symbol("a"));
        int other2 = graph.Add(new Symbol("b"));
        anchor = graph.Find(anchor);
        other1 = graph.Find(other1);
        other2 = graph.Find(other2);
        graph.GetClass(anchor).Metadata["anchor-tag"] = 17d;

        bool changed = graph.UnionBatch(
            [new[] { other1, anchor, other2 }],
            [anchor],
            assumeCanonicalRoots: true);

        Assert.IsTrue(changed);
        int root = graph.Find(anchor);
        Assert.AreEqual(root, graph.Find(other1));
        Assert.AreEqual(root, graph.Find(other2));
        Assert.AreEqual(17d, (double)graph.GetClass(root).Metadata["anchor-tag"]);
    }

    private static string ToMatchKey(Match match)
    {
        string bindings = string.Join(
            ",",
            match.Bindings
                .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
                .Select(static kvp => $"{kvp.Key}:{kvp.Value}"));

        return $"{match.RootClassId}|{match.Rule.Pattern.ToDisplayString()}|{bindings}";
    }
}
