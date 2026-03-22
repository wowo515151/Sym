using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using SymCobra.Core;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraRebuildPreparationPlanner
{
    public static CobraRebuildPreparationPlan Build(EGraph graph)
    {
        return Build(graph, CobraPlannerSnapshot.Create(graph), null);
    }

    public static CobraRebuildPreparationPlan Build(EGraph graph, CobraRepairCandidatePlan? repairCandidatePlan)
    {
        return Build(graph, CobraPlannerSnapshot.Create(graph), repairCandidatePlan);
    }

    public static CobraRebuildPreparationPlan Build(EGraph graph, CobraPlannerSnapshot snapshot, CobraRepairCandidatePlan? repairCandidatePlan)
    {
        return Build(graph, snapshot.RootIds, snapshot.NodeCounts, snapshot.Generations, repairCandidatePlan);
    }

    public static CobraRebuildPreparationPlan Build(CobraGraphState graphState)
    {
        return Build(graphState, CobraPlannerSnapshot.Create(graphState), null);
    }

    public static CobraRebuildPreparationPlan Build(CobraGraphState graphState, CobraRepairCandidatePlan? repairCandidatePlan)
    {
        return Build(graphState, CobraPlannerSnapshot.Create(graphState), repairCandidatePlan);
    }

    public static CobraRebuildPreparationPlan Build(CobraGraphState graphState, CobraPlannerSnapshot snapshot, CobraRepairCandidatePlan? repairCandidatePlan)
    {
        return Build(graphState, snapshot.RootIds, snapshot.NodeCounts, snapshot.Generations, repairCandidatePlan);
    }

    public static CobraRebuildPreparationPlan Build(EGraph graph, int[] allNodeCounts, int[] allGenerations, CobraRepairCandidatePlan? repairCandidatePlan = null)
    {
        return Build(graph, graph.GetRootIds(), allNodeCounts, allGenerations, repairCandidatePlan);
    }

    public static CobraRebuildPreparationPlan Build(EGraph graph, IReadOnlyList<int> rootIds, int[] allNodeCounts, int[] allGenerations, CobraRepairCandidatePlan? repairCandidatePlan = null)
    {
        return BuildCore(
            rootIds,
            allNodeCounts,
            allGenerations,
            repairCandidatePlan,
            id => graph.GetClass(id).Nodes.Count,
            id => graph.GetClass(id).Generation);
    }

    public static CobraRebuildPreparationPlan Build(CobraGraphState graphState, int[] allNodeCounts, int[] allGenerations, CobraRepairCandidatePlan? repairCandidatePlan = null)
    {
        return Build(graphState, CobraPlannerSnapshot.Create(graphState).RootIds, allNodeCounts, allGenerations, repairCandidatePlan);
    }

    public static CobraRebuildPreparationPlan Build(CobraGraphState graphState, IReadOnlyList<int> rootIds, int[] allNodeCounts, int[] allGenerations, CobraRepairCandidatePlan? repairCandidatePlan = null)
    {
        return BuildCore(
            rootIds,
            allNodeCounts,
            allGenerations,
            repairCandidatePlan,
            id => graphState.GetClass(id).NodeIds.Count,
            id => graphState.GetClass(id).Generation);
    }

    private static Dictionary<int, int> BuildRepairCounts(CobraRepairCandidatePlan? repairCandidatePlan)
    {
        var repairCounts = new Dictionary<int, int>();
        if (repairCandidatePlan == null || repairCandidatePlan.Candidates.Count == 0)
        {
            return repairCounts;
        }

        foreach (var candidate in repairCandidatePlan.Candidates)
        {
            repairCounts[candidate.ClassId] = repairCounts.TryGetValue(candidate.ClassId, out int count)
                ? count + 1
                : 1;
        }

        return repairCounts;
    }

    private static CobraRebuildPreparationPlan BuildCore(
        IReadOnlyList<int> rootIds,
        int[] allNodeCounts,
        int[] allGenerations,
        CobraRepairCandidatePlan? repairCandidatePlan,
        Func<int, int> getNodeCount,
        Func<int, int> getGeneration)
    {
        if (rootIds.Count == 0)
        {
            return new CobraRebuildPreparationPlan([], CobraRebuildPreparationSource.CpuHeuristic);
        }

        var repairCounts = BuildRepairCounts(repairCandidatePlan);
        var repairCountsArr = rootIds.Select(id => repairCounts.GetValueOrDefault(id)).ToArray();

        if (CobraCudaNative.TryScoreRebuildWithRepairById(
                rootIds.ToArray(),
                allNodeCounts,
                allGenerations,
                repairCountsArr,
                out var scores) ||
            CobraCudaNative.TryScoreRebuildWithRepair(
                rootIds.Select(id => id < allNodeCounts.Length ? allNodeCounts[id] : getNodeCount(id)).ToArray(),
                rootIds.Select(id => id < allGenerations.Length ? allGenerations[id] : getGeneration(id)).ToArray(),
                repairCountsArr,
                out scores))
        {
            return new CobraRebuildPreparationPlan(
                rootIds.Zip(scores, static (id, score) => new { id, score })
                    .OrderByDescending(item => item.score)
                    .ThenBy(item => item.id)
                    .Select(item => item.id)
                    .ToArray(),
                CobraRebuildPreparationSource.Cuda);
        }

        return new CobraRebuildPreparationPlan(
            rootIds.OrderByDescending(id => (id < allGenerations.Length ? allGenerations[id] : getGeneration(id)) + (repairCounts.GetValueOrDefault(id) * 8))
                .ThenByDescending(id => (id < allNodeCounts.Length ? allNodeCounts[id] : getNodeCount(id)) + repairCounts.GetValueOrDefault(id))
                .ThenBy(id => id)
                .ToArray(),
            CobraRebuildPreparationSource.CpuHeuristic);
    }
}
