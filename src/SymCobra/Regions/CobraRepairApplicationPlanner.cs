using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using SymCobra.Core;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraRepairApplicationPlanner
{
    public static CobraRepairApplicationPlan Build(EGraph graph, CobraRepairCandidatePlan repairPlan)
    {
        return Build(graph, CobraPlannerSnapshot.Create(graph), repairPlan);
    }

    public static CobraRepairApplicationPlan Build(EGraph graph, CobraPlannerSnapshot? plannerSnapshot, CobraRepairCandidatePlan repairPlan)
    {
        if (repairPlan.Candidates.Count == 0)
        {
            return new CobraRepairApplicationPlan([], [], [], CobraRepairApplicationSource.CpuHeuristic);
        }

        return BuildCore(
            repairPlan,
            plannerSnapshot?.NodeCounts,
            plannerSnapshot?.Generations,
            candidate => CanonicalizeCandidate(graph, candidate),
            classId => graph.Find(classId));
    }

    public static CobraRepairApplicationPlan Build(CobraGraphState graphState, CobraRepairCandidatePlan repairPlan)
    {
        return Build(graphState, CobraPlannerSnapshot.Create(graphState), repairPlan);
    }

    public static CobraRepairApplicationPlan Build(CobraGraphState graphState, CobraPlannerSnapshot? plannerSnapshot, CobraRepairCandidatePlan repairPlan)
    {
        if (repairPlan.Candidates.Count == 0)
        {
            return new CobraRepairApplicationPlan([], [], [], CobraRepairApplicationSource.CpuHeuristic);
        }

        return BuildCore(
            repairPlan,
            plannerSnapshot?.NodeCounts,
            plannerSnapshot?.Generations,
            candidate => CanonicalizeCandidate(graphState, candidate),
            classId => graphState.Find(classId));
    }

    private static CobraRepairApplicationPlan BuildCore(
        CobraRepairCandidatePlan repairPlan,
        int[]? allNodeCounts,
        int[]? allGenerations,
        Func<EGraphRepairCandidate, ENode> canonicalize,
        Func<int, int> canonicalizeClassId)
    {
        if (allNodeCounts == null || allGenerations == null)
        {
            throw new InvalidOperationException("Planner snapshot metrics are required for COBRA repair application planning.");
        }
        var projectedCandidates = new ProjectedCandidate[repairPlan.Candidates.Count];
        var canonicalChildIds = new List<int>();
        var headHashes = new int[repairPlan.Candidates.Count];
        var childStarts = new int[repairPlan.Candidates.Count];
        var childCounts = new int[repairPlan.Candidates.Count];

        for (int i = 0; i < repairPlan.Candidates.Count; i++)
        {
            var candidate = repairPlan.Candidates[i];
            var canonicalNode = canonicalize(candidate);
            childStarts[i] = canonicalChildIds.Count;
            childCounts[i] = canonicalNode.Children.Count;
            foreach (int childId in canonicalNode.Children)
            {
                canonicalChildIds.Add(childId);
            }

            headHashes[i] = System.StringComparer.Ordinal.GetHashCode(canonicalNode.Head);
            projectedCandidates[i] = new ProjectedCandidate(candidate, i, canonicalNode);
        }

        int[] targetHashes;
        bool usedCudaHashes = CobraCudaNative.TryHashRepairTargets(
            headHashes,
            childStarts,
            childCounts,
            canonicalChildIds.ToArray(),
            out targetHashes);
        if (!usedCudaHashes)
        {
            targetHashes = new int[projectedCandidates.Length];
            for (int i = 0; i < projectedCandidates.Length; i++)
            {
                targetHashes[i] = projectedCandidates[i].CanonicalNode.GetHashCode();
            }
        }

        var groups = BuildGroups(projectedCandidates, targetHashes);

        if (groups.Length == 0)
        {
            return new CobraRepairApplicationPlan(repairPlan.Candidates, [], [], CobraRepairApplicationSource.CpuHeuristic);
        }

        var anchorIds = new int[groups.Length];
        var memberCounts = new int[groups.Length];
        var generations = new int[groups.Length];
        var nodeCounts = new int[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            anchorIds[i] = canonicalizeClassId(groups[i].AnchorClassId);
            memberCounts[i] = groups[i].Candidates.Count;

            int maxGeneration = int.MinValue;
            int totalNodeCount = 0;
            foreach (var candidate in groups[i].Candidates)
            {
                int canonicalClassId = canonicalizeClassId(candidate.ClassId);
                int generation = allGenerations[canonicalClassId];
                if (generation > maxGeneration)
                {
                    maxGeneration = generation;
                }

                totalNodeCount += allNodeCounts[canonicalClassId];
            }

            generations[i] = maxGeneration;
            nodeCounts[i] = totalNodeCount;
        }

        CobraRepairApplicationGroup[] orderedGroups;
        CobraRepairApplicationSource source;
        if (CobraCudaNative.TryScoreUnionGroups(anchorIds, memberCounts, generations, nodeCounts, out var scores))
        {
            orderedGroups = groups
                .Zip(scores, static (group, score) => new { group, score })
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.group.AnchorClassId)
                .Select(item => item.group)
                .ToArray();
            source = CobraRepairApplicationSource.Cuda;
        }
        else
        {
            orderedGroups = groups
                .OrderByDescending(group => group.Candidates.Count)
                .ThenByDescending(group => allGenerations[canonicalizeClassId(group.AnchorClassId)])
                .ThenBy(group => group.AnchorClassId)
                .ToArray();
            source = usedCudaHashes || repairPlan.Source == CobraRepairCandidateSource.Cuda
                ? CobraRepairApplicationSource.Cuda
                : CobraRepairApplicationSource.CpuHeuristic;
        }

        var orderedCandidates = orderedGroups
            .SelectMany(group => group.Candidates)
            .ToArray();
        var groupedCandidates = orderedGroups
            .Select(static group => group.Candidates)
            .ToArray();

        return new CobraRepairApplicationPlan(orderedCandidates, orderedGroups, groupedCandidates, source);
    }

    private static CobraRepairApplicationGroup[] BuildGroups(ProjectedCandidate[] projectedCandidates, int[] targetHashes)
    {
        var buckets = new Dictionary<int, List<ProjectedCandidate>>();
        for (int i = 0; i < projectedCandidates.Length; i++)
        {
            int targetHash = targetHashes[i];
            if (!buckets.TryGetValue(targetHash, out var bucket))
            {
                bucket = new List<ProjectedCandidate>();
                buckets[targetHash] = bucket;
            }

            bucket.Add(projectedCandidates[i]);
        }

        var groups = new List<CobraRepairApplicationGroup>();
        foreach (var bucket in buckets.Values)
        {
            var bucketGroups = new Dictionary<ENode, List<ProjectedCandidate>>();
            foreach (var projected in bucket)
            {
                if (!bucketGroups.TryGetValue(projected.CanonicalNode, out var groupedCandidates))
                {
                    groupedCandidates = new List<ProjectedCandidate>();
                    bucketGroups[projected.CanonicalNode] = groupedCandidates;
                }

                groupedCandidates.Add(projected);
            }

            foreach (var bucketGroup in bucketGroups.Values)
            {
                bucketGroup.Sort(static (left, right) => left.Index.CompareTo(right.Index));
                var orderedCandidates = new EGraphRepairCandidate[bucketGroup.Count];
                int anchorClassId = int.MaxValue;
                for (int i = 0; i < bucketGroup.Count; i++)
                {
                    orderedCandidates[i] = bucketGroup[i].Candidate;
                    if (bucketGroup[i].Candidate.ClassId < anchorClassId)
                    {
                        anchorClassId = bucketGroup[i].Candidate.ClassId;
                    }
                }

                groups.Add(new CobraRepairApplicationGroup(
                    anchorClassId,
                    bucketGroup[0].CanonicalNode,
                    orderedCandidates));
            }
        }

        return groups.ToArray();
    }

    private static ENode CanonicalizeCandidate(EGraph graph, EGraphRepairCandidate candidate)
    {
        var canonicalChildren = ImmutableList.CreateBuilder<int>();
        foreach (int childId in candidate.Node.Children)
        {
            canonicalChildren.Add(graph.Find(childId));
        }

        return new ENode(candidate.Node.Head, canonicalChildren.ToImmutable());
    }

    private static ENode CanonicalizeCandidate(CobraGraphState graphState, EGraphRepairCandidate candidate)
    {
        var canonicalChildren = ImmutableList.CreateBuilder<int>();
        foreach (int childId in candidate.Node.Children)
        {
            canonicalChildren.Add(graphState.Find(childId));
        }

        return new ENode(candidate.Node.Head, canonicalChildren.ToImmutable());
    }

    private readonly record struct ProjectedCandidate(EGraphRepairCandidate Candidate, int Index, ENode CanonicalNode);
}
