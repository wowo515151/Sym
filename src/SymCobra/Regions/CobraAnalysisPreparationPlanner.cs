using System.Collections.Generic;
using System.Linq;
using Sym.Core;
using Sym.Core.EGraph;
using SymCobra.Core;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraAnalysisPreparationPlanner
{
    public static CobraAnalysisPreparationPlan Build(EGraph graph)
    {
        return Build(graph, CobraPlannerSnapshot.Create(graph), null);
    }

    public static CobraAnalysisPreparationPlan Build(EGraph graph, CobraRepairCandidatePlan? repairCandidatePlan)
    {
        return Build(graph, CobraPlannerSnapshot.Create(graph), repairCandidatePlan);
    }

    public static CobraAnalysisPreparationPlan Build(EGraph graph, CobraPlannerSnapshot snapshot, CobraRepairCandidatePlan? repairCandidatePlan)
    {
        return Build(
            snapshot.RootIds,
            snapshot.NodeCounts,
            snapshot.Generations,
            repairCandidatePlan,
            classId => graph.GetClass(classId).Data is Shape shape && shape.IsValid,
            classId => graph.GetClass(classId).Nodes.Count,
            classId => graph.GetClass(classId).Generation);
    }

    public static CobraAnalysisPreparationPlan Build(EGraph graph, int[] allNodeCounts, int[] allGenerations, CobraRepairCandidatePlan? repairCandidatePlan = null)
    {
        return Build(graph, graph.GetRootIds(), allNodeCounts, allGenerations, repairCandidatePlan);
    }

    public static CobraAnalysisPreparationPlan Build(EGraph graph, IReadOnlyList<int> rootIds, int[] allNodeCounts, int[] allGenerations, CobraRepairCandidatePlan? repairCandidatePlan = null)
    {
        return Build(
            rootIds,
            allNodeCounts,
            allGenerations,
            repairCandidatePlan,
            classId => graph.GetClass(classId).Data is Shape shape && shape.IsValid,
            classId => graph.GetClass(classId).Nodes.Count,
            classId => graph.GetClass(classId).Generation);
    }

    public static CobraAnalysisPreparationPlan Build(CobraGraphState graphState)
    {
        return Build(graphState, CobraPlannerSnapshot.Create(graphState), null);
    }

    public static CobraAnalysisPreparationPlan Build(CobraGraphState graphState, CobraRepairCandidatePlan? repairCandidatePlan)
    {
        return Build(graphState, CobraPlannerSnapshot.Create(graphState), repairCandidatePlan);
    }

    public static CobraAnalysisPreparationPlan Build(CobraGraphState graphState, CobraPlannerSnapshot snapshot, CobraRepairCandidatePlan? repairCandidatePlan)
    {
        return Build(
            snapshot.RootIds,
            snapshot.NodeCounts,
            snapshot.Generations,
            repairCandidatePlan,
            classId => graphState.GetClass(classId).Metadata.TryGetValue("shape", out var shapeObj) && shapeObj is Shape shape && shape.IsValid,
            classId => graphState.GetClass(classId).NodeIds.Count,
            classId => graphState.GetClass(classId).Generation);
    }

    public static CobraAnalysisPreparationPlan Build(CobraGraphState graphState, IReadOnlyList<int> rootIds, int[] allNodeCounts, int[] allGenerations, CobraRepairCandidatePlan? repairCandidatePlan = null)
    {
        return Build(
            rootIds,
            allNodeCounts,
            allGenerations,
            repairCandidatePlan,
            classId => graphState.GetClass(classId).Metadata.TryGetValue("shape", out var shapeObj) && shapeObj is Shape shape && shape.IsValid,
            classId => graphState.GetClass(classId).NodeIds.Count,
            classId => graphState.GetClass(classId).Generation);
    }

    private static CobraAnalysisPreparationPlan Build(
        IReadOnlyList<int> rootIds,
        int[] allNodeCounts,
        int[] allGenerations,
        CobraRepairCandidatePlan? repairCandidatePlan,
        Func<int, bool> hasValidShape,
        Func<int, int> fallbackNodeCount,
        Func<int, int> fallbackGeneration)
    {
        if (rootIds.Count == 0)
        {
            return new CobraAnalysisPreparationPlan([], CobraAnalysisPreparationSource.CpuHeuristic);
        }

        var repairCounts = BuildRepairCounts(repairCandidatePlan);

        var unresolvedFlags = new int[rootIds.Count];
        var repairPressure = new int[rootIds.Count];
        
        for (int i = 0; i < rootIds.Count; i++)
        {
            repairPressure[i] = repairCounts.GetValueOrDefault(rootIds[i]);
            unresolvedFlags[i] = hasValidShape(rootIds[i]) ? 0 : 1;
        }

        if (CobraCudaNative.TryScoreAnalysisWithRepairById(
                rootIds.ToArray(),
                allNodeCounts,
                allGenerations,
                unresolvedFlags,
                repairPressure,
                out var scores) ||
            CobraCudaNative.TryScoreAnalysisWithRepair(
                rootIds.Select(id => id < allNodeCounts.Length ? allNodeCounts[id] : fallbackNodeCount(id)).ToArray(),
                rootIds.Select(id => id < allGenerations.Length ? allGenerations[id] : fallbackGeneration(id)).ToArray(),
                unresolvedFlags,
                repairPressure,
                out scores))
        {
            return new CobraAnalysisPreparationPlan(
                rootIds.Zip(scores, static (id, score) => new { id, score })
                    .OrderByDescending(item => item.score)
                    .ThenBy(item => item.id)
                    .Select(item => item.id)
                    .ToArray(),
                CobraAnalysisPreparationSource.Cuda);
        }

        return new CobraAnalysisPreparationPlan(
            rootIds.OrderByDescending(id => (hasValidShape(id) ? 0 : 1) + (repairCounts.GetValueOrDefault(id) > 0 ? 1 : 0))
                .ThenByDescending(id => (id < allGenerations.Length ? allGenerations[id] : fallbackGeneration(id)) + (repairCounts.GetValueOrDefault(id) * 4))
                .ThenByDescending(id => (id < allNodeCounts.Length ? allNodeCounts[id] : fallbackNodeCount(id)) + repairCounts.GetValueOrDefault(id))
                .ThenBy(id => id)
                .ToArray(),
            CobraAnalysisPreparationSource.CpuHeuristic);
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
}
