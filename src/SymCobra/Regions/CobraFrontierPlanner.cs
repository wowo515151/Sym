// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraFrontierPlanner
{
    public static CobraFrontierPlan Build(EGraph graph, CobraRegionPlan? regionPlan = null)
    {
        return Build(graph, CobraPlannerSnapshot.Create(graph), regionPlan);
    }

    public static CobraFrontierPlan Build(EGraph graph, CobraPlannerSnapshot snapshot, CobraRegionPlan? regionPlan = null)
    {
        return Build(graph, snapshot.RootIds, snapshot.NodeCounts, snapshot.Generations, regionPlan);
    }

    public static CobraFrontierPlan Build(EGraph graph, IReadOnlyList<int> rootIds, int[] allNodeCounts, int[] allGenerations, CobraRegionPlan? regionPlan = null)
    {
        if (rootIds.Count == 0)
        {
            return new CobraFrontierPlan([], CobraFrontierPrioritySource.CpuHeuristic);
        }

        var hotFlags = new int[rootIds.Count];
        var boundaryFlags = new int[rootIds.Count];
        var residualFlags = new int[rootIds.Count];
        var suppressedFlags = new int[rootIds.Count];
        var hotRegionCounts = new int[rootIds.Count];
        var boundaryRegionCounts = new int[rootIds.Count];

        var hotCountMap = regionPlan?.Regions
            .SelectMany(region => region.MemberClassIds.Select(classId => (classId, isHot: regionPlan.HotClassIds.Contains(classId))))
            .Where(item => item.isHot)
            .GroupBy(item => item.classId)
            .ToDictionary(group => group.Key, group => group.Count()) ?? new Dictionary<int, int>();
        var boundaryCountMap = regionPlan?.Regions
            .SelectMany(region => region.BoundaryClassIds)
            .GroupBy(classId => classId)
            .ToDictionary(group => group.Key, group => group.Count()) ?? new Dictionary<int, int>();

        for (int i = 0; i < rootIds.Count; i++)
        {
            int classId = rootIds[i];
            hotFlags[i] = regionPlan is not null && regionPlan.HotClassIds.Contains(classId) ? 1 : 0;
            boundaryFlags[i] = regionPlan is not null && regionPlan.BoundaryClassIds.Contains(classId) ? 1 : 0;
            residualFlags[i] = regionPlan is not null && regionPlan.ResidualClassIds.Contains(classId) ? 1 : 0;
            suppressedFlags[i] = regionPlan is not null && regionPlan.SuppressedClassIds.Contains(classId) ? 1 : 0;
            hotRegionCounts[i] = hotCountMap.GetValueOrDefault(classId);
            boundaryRegionCounts[i] = boundaryCountMap.GetValueOrDefault(classId);
        }

        if (CobraCudaNative.TryScoreFrontierV3ById(
                rootIds.ToArray(),
                allNodeCounts,
                allGenerations,
                hotFlags,
                boundaryFlags,
                residualFlags,
                suppressedFlags,
                hotRegionCounts,
                boundaryRegionCounts,
                out var gpuScores) ||
            CobraCudaNative.TryScoreFrontierV3(
                rootIds.Select(id => allNodeCounts[id]).ToArray(),
                rootIds.Select(id => allGenerations[id]).ToArray(),
                hotFlags,
                boundaryFlags,
                residualFlags,
                suppressedFlags,
                hotRegionCounts,
                boundaryRegionCounts,
                out gpuScores) ||
            CobraCudaNative.TryScoreFrontierV2ById(
                rootIds.ToArray(),
                allNodeCounts,
                allGenerations,
                hotFlags,
                boundaryFlags,
                residualFlags,
                hotRegionCounts,
                boundaryRegionCounts,
                out gpuScores) ||
            CobraCudaNative.TryScoreFrontierV2(
                rootIds.Select(id => allNodeCounts[id]).ToArray(),
                rootIds.Select(id => allGenerations[id]).ToArray(),
                hotFlags,
                boundaryFlags,
                residualFlags,
                hotRegionCounts,
                boundaryRegionCounts,
                out gpuScores))
        {
            var ranked = rootIds
                .Select((classId, index) => (classId, score: gpuScores[index], index))
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.index)
                .ToList();

            return BuildQueuedPlan(ranked, CobraFrontierPrioritySource.Cuda, hotFlags, boundaryFlags, residualFlags, suppressedFlags);
        }

        var fallbackRanked = rootIds
            .Select((classId, index) => (
                classId,
                score:
                    (hotFlags[index] * 1600) +
                    (hotRegionCounts[index] * 300) +
                    (allGenerations[classId] * 8) +
                    (allNodeCounts[classId] * 2) -
                    (boundaryFlags[index] * 180) -
                    (boundaryRegionCounts[index] * 40) -
                    (residualFlags[index] * 140) -
                    (suppressedFlags[index] * 1200),
                index))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .ToList();

        return BuildQueuedPlan(fallbackRanked, CobraFrontierPrioritySource.CpuHeuristic, hotFlags, boundaryFlags, residualFlags, suppressedFlags);
    }

    private static CobraFrontierPlan BuildQueuedPlan(
        IReadOnlyList<(int classId, int score, int index)> rankedClasses,
        CobraFrontierPrioritySource prioritySource,
        IReadOnlyList<int> hotFlags,
        IReadOnlyList<int> boundaryFlags,
        IReadOnlyList<int> residualFlags,
        IReadOnlyList<int> suppressedFlags)
    {
        var interior = new List<int>(rankedClasses.Count);
        var boundary = new List<int>();
        var residual = new List<int>();
        var suppressed = new List<int>();
        var routingReasons = new Dictionary<int, string>(rankedClasses.Count);

        foreach (var item in rankedClasses)
        {
            int classId = item.classId;
            int index = item.index;
            if (suppressedFlags[index] != 0)
            {
                suppressed.Add(classId);
                routingReasons[classId] = "suppressed-conflict";
            }
            else if (boundaryFlags[index] != 0)
            {
                boundary.Add(classId);
                routingReasons[classId] = hotFlags[index] != 0 ? "boundary-hot" : "boundary-deferred";
            }
            else if (residualFlags[index] != 0)
            {
                residual.Add(classId);
                routingReasons[classId] = hotFlags[index] != 0 ? "residual-hot" : "residual-deferred";
            }
            else
            {
                interior.Add(classId);
                routingReasons[classId] = hotFlags[index] != 0 ? "interior-hot" : "interior-default";
            }
        }

        var ordered = new List<int>(rankedClasses.Count);
        ordered.AddRange(interior);
        ordered.AddRange(boundary);
        ordered.AddRange(residual);
        ordered.AddRange(suppressed);

        return new CobraFrontierPlan(
            ordered,
            prioritySource,
            interior,
            boundary,
            residual,
            suppressed,
            routingReasons);
    }
}
