using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using SymCobra.Core;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraRepairCandidatePlanner
{
    public static CobraRepairCandidatePlan Build(EGraph graph)
    {
        return Build(graph, null);
    }

    public static CobraRepairCandidatePlan Build(EGraph graph, CobraPlannerSnapshot? plannerSnapshot)
    {
        var repairSnapshot = graph.GetRepairSnapshot();
        return Build(repairSnapshot, plannerSnapshot, classId => graph.Find(classId), CreateMetricsFactory(graph));
    }

    public static CobraRepairCandidatePlan Build(CobraGraphState graphState)
    {
        return Build(graphState, null);
    }

    public static CobraRepairCandidatePlan Build(CobraGraphState graphState, CobraPlannerSnapshot? plannerSnapshot)
    {
        var repairSnapshot = graphState.GetRepairSnapshot();
        return Build(repairSnapshot, plannerSnapshot, classId => graphState.Find(classId), CreateMetricsFactory(graphState));
    }

    private static CobraRepairCandidatePlan Build(
        EGraphRepairSnapshot repairSnapshot,
        CobraPlannerSnapshot? plannerSnapshot,
        Func<int, int> canonicalizeClassId,
        Func<(int[] NodeCounts, int[] Generations)> metricsFactory)
    {
        if (repairSnapshot.Candidates.Count == 0)
        {
            return new CobraRepairCandidatePlan([], CobraRepairCandidateSource.CpuHeuristic);
        }

        if (CobraCudaNative.TryMarkRepairCandidates(
            repairSnapshot.Parents,
            repairSnapshot.ChildStarts,
            repairSnapshot.ChildCounts,
            repairSnapshot.ChildIds,
            out var dirtyFlags))
        {
            var dirtyCandidateList = new List<EGraphRepairCandidate>(repairSnapshot.Candidates.Count);
            for (int i = 0; i < repairSnapshot.Candidates.Count && i < dirtyFlags.Length; i++)
            {
                if (dirtyFlags[i] != 0)
                {
                    dirtyCandidateList.Add(repairSnapshot.Candidates[i]);
                }
            }

            var dirtyCandidates = dirtyCandidateList.ToArray();

            if (dirtyCandidates.Length > 0)
            {
                int[] allNodeCounts;
                int[] allGenerations;
                if (plannerSnapshot is null)
                {
                    (allNodeCounts, allGenerations) = metricsFactory();
                }
                else
                {
                    allNodeCounts = plannerSnapshot.NodeCounts;
                    allGenerations = plannerSnapshot.Generations;
                    for (int i = 0; i < dirtyCandidates.Length; i++)
                    {
                        int classId = dirtyCandidates[i].ClassId;
                        if ((uint)classId >= (uint)allNodeCounts.Length || (uint)classId >= (uint)allGenerations.Length)
                        {
                            (allNodeCounts, allGenerations) = metricsFactory();
                            break;
                        }
                    }
                }
                var classIds = new int[dirtyCandidates.Length];
                var childCounts = new int[dirtyCandidates.Length];
                var boundaryFlags = new int[dirtyCandidates.Length];
                var candidateNodeCounts = new int[dirtyCandidates.Length];
                var candidateGenerations = new int[dirtyCandidates.Length];
                for (int i = 0; i < dirtyCandidates.Length; i++)
                {
                    int classId = canonicalizeClassId(dirtyCandidates[i].ClassId);
                    classIds[i] = classId;
                    childCounts[i] = dirtyCandidates[i].Node.Children.Count;
                    boundaryFlags[i] = dirtyCandidates[i].Node.Head is "Transpose" or "Relu" ? 1 : 0;
                    candidateNodeCounts[i] = allNodeCounts[classId];
                    candidateGenerations[i] = allGenerations[classId];
                }

                if (CobraCudaNative.TryScoreRepairCandidatesV2ById(classIds, childCounts, allNodeCounts, allGenerations, boundaryFlags, out var scores) ||
                    CobraCudaNative.TryScoreRepairCandidatesV2(
                        classIds,
                        childCounts,
                        candidateGenerations,
                        candidateNodeCounts,
                        boundaryFlags,
                        out scores))
                {
                    dirtyCandidates = dirtyCandidates
                        .Zip(scores, static (candidate, score) => new { candidate, score })
                        .OrderByDescending(item => item.score)
                        .ThenBy(item => item.candidate.ClassId)
                        .Select(item => item.candidate)
                        .ToArray();
                }
                else if (CobraCudaNative.TryScoreRepairCandidates(
                    classIds,
                    childCounts,
                    candidateGenerations,
                    candidateNodeCounts,
                    out var fallbackScores))
                {
                    dirtyCandidates = dirtyCandidates
                        .Zip(fallbackScores, static (candidate, score) => new { candidate, score })
                        .OrderByDescending(item => item.score)
                        .ThenBy(item => item.candidate.ClassId)
                        .Select(item => item.candidate)
                        .ToArray();
                }
                else
                {
                    dirtyCandidates = dirtyCandidates
                        .OrderByDescending(candidate => allGenerations[candidate.ClassId])
                        .ThenByDescending(candidate => allNodeCounts[candidate.ClassId])
                        .ThenBy(candidate => candidate.ClassId)
                        .ToArray();
                }
            }

            return new CobraRepairCandidatePlan(dirtyCandidates, CobraRepairCandidateSource.Cuda);
        }

        var fallback = repairSnapshot.Candidates
            .Where(candidate => candidate.Node.Children.Any(childId => canonicalizeClassId(childId) != childId))
            .ToArray();
        return new CobraRepairCandidatePlan(fallback, CobraRepairCandidateSource.CpuHeuristic);
    }

    private static Func<(int[] NodeCounts, int[] Generations)> CreateMetricsFactory(EGraph graph)
    {
        return () =>
        {
            int[] allNodeCounts = new int[graph.ClassCount];
            int[] allGenerations = new int[graph.ClassCount];
            for (int classId = 0; classId < graph.ClassCount; classId++)
            {
                var eClass = graph.GetClass(classId);
                allNodeCounts[classId] = eClass.Nodes.Count;
                allGenerations[classId] = eClass.Generation;
            }

            return (allNodeCounts, allGenerations);
        };
    }

    private static Func<(int[] NodeCounts, int[] Generations)> CreateMetricsFactory(CobraGraphState graphState)
    {
        return () =>
        {
            int[] allNodeCounts = new int[graphState.ClassCount];
            int[] allGenerations = new int[graphState.ClassCount];
            for (int classId = 0; classId < graphState.ClassCount; classId++)
            {
                var cobraClass = graphState.GetClass(classId);
                allNodeCounts[classId] = cobraClass.NodeIds.Count;
                allGenerations[classId] = cobraClass.Generation;
            }

            return (allNodeCounts, allGenerations);
        };
    }
}
