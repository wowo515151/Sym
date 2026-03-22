// Copyright Warren Harding 2026
using System.Collections.Generic;
using Sym.Core.EGraph;
using SymCobra.Regions;
using SymCobra.Runtime;

namespace SymCobra.Core;

public static class CobraUnionEngine
{
    public static bool ApplyUnions(CobraGraphState graphState, EGraph legacyGraph, IReadOnlyList<CobraPreparedUnion> preparedUnions)
    {
        // Ensure both graphs have all classes present in either one
        int maxClassCount = System.Math.Max(graphState.ClassCount, legacyGraph.ClassCount);
        graphState.EnsureClassCount(maxClassCount);
        while (legacyGraph.ClassCount < maxClassCount)
        {
            legacyGraph.AddClass();
        }

        var snapshot = CobraPlannerSnapshot.Create(graphState);
        var batchPlan = CobraUnionPreparationPlanner.BuildBatchPlan(graphState, snapshot, preparedUnions);
        if (batchPlan.Groups.Count == 0)
        {
            return false;
        }

        // GPU optimization: handle disjoint-set merges on the GPU
        var flattenedLeft = new List<int>();
        var flattenedRight = new List<int>();
        foreach (var group in batchPlan.Groups)
        {
            foreach (int member in group.MemberIds)
            {
                if (member != group.AnchorId)
                {
                    flattenedLeft.Add(group.AnchorId);
                    flattenedRight.Add(member);
                }
            }
        }

        var updatedClassIds = new List<int>();
        var updatedParentIds = new List<int>();
        var metricClassIds = new List<int>();
        var metricNodeCounts = new List<int>();
        var metricGenerations = new List<int>();

        if (flattenedLeft.Count > 0)
        {
            if (TryWarmGpuParentsFromCobra(graphState) &&
                CobraCudaNative.TryUnionBatchGpuCached(flattenedLeft.ToArray(), flattenedRight.ToArray(), out var gpuChanged))
            {
                // The GPU has successfully executed the union batch and cached the parent array updates. 
            }

            for (int i = 0; i < flattenedLeft.Count; i++)
            {
                graphState.Union(flattenedLeft[i], flattenedRight[i]);
            }

            foreach (var group in batchPlan.Groups)
            {
                int root = graphState.Find(group.AnchorId);
                foreach (int member in group.MemberIds)
                {
                    updatedClassIds.Add(member);
                    updatedParentIds.Add(root);
                }
                metricClassIds.Add(root);
                var rootClass = graphState.GetClass(root);
                metricNodeCounts.Add(rootClass.NodeIds.Count);
                metricGenerations.Add(rootClass.Generation);
            }
        }

        if (updatedClassIds.Count > 0)
        {
            CobraCudaNative.TryApplyParentUpdates(
                updatedClassIds.ToArray(),
                updatedParentIds.ToArray());
            CobraCudaNative.TryApplyClassMetricUpdates(
                metricClassIds.ToArray(),
                metricNodeCounts.ToArray(),
                metricGenerations.ToArray());
        }

        graphState.SyncLegacyGraphFromCobra(legacyGraph);

        return flattenedLeft.Count > 0;
    }

    internal static bool TryWarmGpuParentsFromCobra(CobraGraphState graphState)
    {
        return CobraCudaNative.TryWarmParents(graphState.GetParentSnapshot());
    }
}
