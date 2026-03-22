// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Linq;
using Sym.Core.EGraph;
using SymCobra.Core;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraUnionPreparationPlanner
{
    public static CobraUnionBatchPlan BuildBatchPlan(EGraph graph, IReadOnlyList<CobraPreparedUnion> preparedUnions)
    {
        return BuildBatchPlan(graph, CobraPlannerSnapshot.Create(graph), preparedUnions);
    }

    public static CobraUnionBatchPlan BuildBatchPlan(CobraGraphState graphState, IReadOnlyList<CobraPreparedUnion> preparedUnions)
    {
        return BuildBatchPlan(graphState, CobraPlannerSnapshot.Create(graphState), preparedUnions);
    }

    public static CobraUnionBatchPlan BuildBatchPlan(EGraph graph, CobraPlannerSnapshot snapshot, IReadOnlyList<CobraPreparedUnion> preparedUnions)
    {
        var batchPlan = BuildBatchPlan(preparedUnions, graph.ClassCount - 1);
        if (batchPlan.Groups.Count == 0)
        {
            return batchPlan;
        }

        var nodeCounts = snapshot.NodeCounts;
        var generations = snapshot.Generations;
        int[] allMembers = CollectDistinctGroupMembers(batchPlan.Groups);

        if (CobraCudaNative.TryScoreUnionMembers(allMembers, nodeCounts, generations, out var scores))
        {
            var scoreMap = new Dictionary<int, int>(allMembers.Length);
            for (int i = 0; i < allMembers.Length; i++)
            {
                scoreMap[allMembers[i]] = scores[i];
            }

            var groups = new CobraUnionBatchGroup[batchPlan.Groups.Count];
            for (int i = 0; i < batchPlan.Groups.Count; i++)
            {
                groups[i] = OrderGroupMembers(batchPlan.Groups[i], scoreMap);
            }

            var anchorIds = groups.Select(group => group.AnchorId).ToArray();
            var memberCounts = groups.Select(group => group.MemberIds.Count).ToArray();
            var groupGenerations = new int[groups.Length];
            var groupNodeCounts = new int[groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                int anchorId = groups[i].AnchorId;
                groupGenerations[i] = generations[anchorId];
                groupNodeCounts[i] = nodeCounts[anchorId];
            }

            if (CobraCudaNative.TryScoreUnionGroups(anchorIds, memberCounts, groupGenerations, groupNodeCounts, out var groupScores))
            {
                groups = groups
                    .Zip(groupScores, static (group, score) => new { group, score })
                    .OrderByDescending(item => item.score)
                    .ThenBy(item => item.group.AnchorId)
                    .Select(item => item.group)
                    .ToArray();
            }
            else
            {
                groups = groups
                    .OrderByDescending(group => generations[group.AnchorId])
                    .ThenByDescending(group => nodeCounts[group.AnchorId])
                    .ThenBy(group => group.AnchorId)
                    .ToArray();
            }

            return new CobraUnionBatchPlan(groups, CobraUnionPreparationSource.Cuda);
        }

        return batchPlan;
    }

    public static CobraUnionBatchPlan BuildBatchPlan(CobraGraphState graphState, CobraPlannerSnapshot snapshot, IReadOnlyList<CobraPreparedUnion> preparedUnions)
    {
        var batchPlan = BuildBatchPlan(preparedUnions, graphState.ClassCount - 1);
        if (batchPlan.Groups.Count == 0)
        {
            return batchPlan;
        }

        var nodeCounts = snapshot.NodeCounts;
        var generations = snapshot.Generations;
        int[] allMembers = CollectDistinctGroupMembers(batchPlan.Groups);

        if (CobraCudaNative.TryScoreUnionMembers(allMembers, nodeCounts, generations, out var scores))
        {
            var scoreMap = new Dictionary<int, int>(allMembers.Length);
            for (int i = 0; i < allMembers.Length; i++)
            {
                scoreMap[allMembers[i]] = scores[i];
            }

            var groups = new CobraUnionBatchGroup[batchPlan.Groups.Count];
            for (int i = 0; i < batchPlan.Groups.Count; i++)
            {
                groups[i] = OrderGroupMembers(batchPlan.Groups[i], scoreMap);
            }

            var anchorIds = groups.Select(group => group.AnchorId).ToArray();
            var memberCounts = groups.Select(group => group.MemberIds.Count).ToArray();
            var groupGenerations = new int[groups.Length];
            var groupNodeCounts = new int[groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                int anchorId = groups[i].AnchorId;
                groupGenerations[i] = generations[anchorId];
                groupNodeCounts[i] = nodeCounts[anchorId];
            }

            if (CobraCudaNative.TryScoreUnionGroups(anchorIds, memberCounts, groupGenerations, groupNodeCounts, out var groupScores))
            {
                groups = groups
                    .Zip(groupScores, static (group, score) => new { group, score })
                    .OrderByDescending(item => item.score)
                    .ThenBy(item => item.group.AnchorId)
                    .Select(item => item.group)
                    .ToArray();
            }
            else
            {
                groups = groups
                    .OrderByDescending(group => generations[group.AnchorId])
                    .ThenByDescending(group => nodeCounts[group.AnchorId])
                    .ThenBy(group => group.AnchorId)
                    .ToArray();
            }

            return new CobraUnionBatchPlan(groups, CobraUnionPreparationSource.Cuda);
        }

        return batchPlan;
    }

    public static CobraUnionBatchPlan BuildBatchPlan(IReadOnlyList<CobraPreparedUnion> preparedUnions, int maxClassId)
    {
        if (preparedUnions.Count == 0)
        {
            return new CobraUnionBatchPlan([], CobraUnionPreparationSource.CpuHeuristic);
        }

        var leftIds = preparedUnions.Select(p => p.LeftId).ToArray();
        var rightIds = preparedUnions.Select(p => p.RightId).ToArray();

        if (CobraCudaNative.TryGroupUnionsV2(leftIds, rightIds, maxClassId, out var groupKeys) ||
            CobraCudaNative.TryGroupUnions(leftIds, rightIds, out groupKeys))
        {
            var membersByGroup = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < preparedUnions.Count; i++)
            {
                int key = groupKeys[i];
                if (!membersByGroup.TryGetValue(key, out var members))
                {
                    members = new HashSet<int>();
                    membersByGroup[key] = members;
                }

                members.Add(preparedUnions[i].LeftId);
                members.Add(preparedUnions[i].RightId);
            }

            var groups = membersByGroup
                .Select(entry =>
                {
                    int[] members = entry.Value.ToArray();
                    System.Array.Sort(members);
                    return new CobraUnionBatchGroup(entry.Key, members);
                })
                .OrderBy(group => group.AnchorId)
                .ToArray();

            return new CobraUnionBatchPlan(groups, CobraUnionPreparationSource.Cuda);
        }

        var fallbackMembersByGroup = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < preparedUnions.Count; i++)
        {
            int key = preparedUnions[i].LeftId;
            if (!fallbackMembersByGroup.TryGetValue(key, out var members))
            {
                members = new HashSet<int>();
                fallbackMembersByGroup[key] = members;
            }

            members.Add(preparedUnions[i].LeftId);
            members.Add(preparedUnions[i].RightId);
        }

        var fallbackGroups = fallbackMembersByGroup
            .Select(entry =>
            {
                int[] members = entry.Value.ToArray();
                System.Array.Sort(members);
                return new CobraUnionBatchGroup(entry.Key, members);
            })
            .OrderBy(group => group.AnchorId)
            .ToArray();

        return new CobraUnionBatchPlan(fallbackGroups, CobraUnionPreparationSource.CpuHeuristic);
    }

    public static CobraUnionPreparationResult Prepare(EGraph graph, IEnumerable<(int LeftId, int RightId)> pairs)
    {
        var pairList = pairs.ToList();
        if (pairList.Count == 0)
        {
            return new CobraUnionPreparationResult([], CobraUnionPreparationSource.CpuHeuristic);
        }

        var leftIds = pairList.Select(p => p.LeftId).ToArray();
        var rightIds = pairList.Select(p => p.RightId).ToArray();
        var parents = graph.GetParentSnapshot();

        if (CobraCudaNative.TryResolveUnionRoots(parents, leftIds, rightIds, out var resolvedLeft, out var resolvedRight, out var pairKeys))
        {
            return BuildResolvedCudaResult(graph, resolvedLeft, resolvedRight, pairKeys);
        }

        return Prepare(pairList);
    }

    public static CobraUnionPreparationResult Prepare(CobraGraphState graphState, IEnumerable<(int LeftId, int RightId)> pairs)
    {
        return Prepare(graphState, pairs, preferCachedParentSnapshot: false);
    }

    public static CobraUnionPreparationResult Prepare(
        CobraGraphState graphState,
        IEnumerable<(int LeftId, int RightId)> pairs,
        bool preferCachedParentSnapshot)
    {
        var pairList = pairs.ToList();
        if (pairList.Count == 0)
        {
            return new CobraUnionPreparationResult([], CobraUnionPreparationSource.CpuHeuristic);
        }

        var leftIds = pairList.Select(p => p.LeftId).ToArray();
        var rightIds = pairList.Select(p => p.RightId).ToArray();

        if (preferCachedParentSnapshot &&
            CobraCudaNative.TryResolveUnionRootsFromCache(leftIds, rightIds, out var cachedLeft, out var cachedRight, out var cachedKeys))
        {
            return BuildResolvedCudaResult(graphState, cachedLeft, cachedRight, cachedKeys);
        }

        var parents = graphState.GetParentSnapshot();

        if (CobraCudaNative.TryResolveUnionRoots(parents, leftIds, rightIds, out var resolvedLeft, out var resolvedRight, out var pairKeys))
        {
            return BuildResolvedCudaResult(graphState, resolvedLeft, resolvedRight, pairKeys);
        }

        return Prepare(pairList);
    }

    private static CobraUnionPreparationResult BuildResolvedCudaResult(
        EGraph graph,
        int[] resolvedLeft,
        int[] resolvedRight,
        ulong[] pairKeys)
    {
        var deduped = DedupeResolvedPairs(resolvedLeft, resolvedRight, pairKeys);
        deduped = OrderPreparedUnions(graph, deduped);
        return new CobraUnionPreparationResult(deduped, CobraUnionPreparationSource.Cuda);
    }

    private static CobraUnionPreparationResult BuildResolvedCudaResult(
        CobraGraphState graphState,
        int[] resolvedLeft,
        int[] resolvedRight,
        ulong[] pairKeys)
    {
        var deduped = DedupeResolvedPairs(resolvedLeft, resolvedRight, pairKeys);
        deduped = OrderPreparedUnions(graphState, deduped);
        return new CobraUnionPreparationResult(deduped, CobraUnionPreparationSource.Cuda);
    }

    private static List<CobraPreparedUnion> DedupeResolvedPairs(
        int[] resolvedLeft,
        int[] resolvedRight,
        ulong[] pairKeys)
    {
        var deduped = new List<CobraPreparedUnion>();
        var seen = new HashSet<ulong>();
        for (int i = 0; i < pairKeys.Length; i++)
        {
            if (resolvedLeft[i] == resolvedRight[i])
            {
                continue;
            }

            if (seen.Add(pairKeys[i]))
            {
                deduped.Add(new CobraPreparedUnion(resolvedLeft[i], resolvedRight[i], pairKeys[i]));
            }
        }

        return deduped;
    }

    private static List<CobraPreparedUnion> OrderPreparedUnions(EGraph graph, List<CobraPreparedUnion> prepared)
    {
        if (prepared.Count <= 1)
        {
            return prepared;
        }

        int[] uniqueMembers = CollectDistinctUnionMembers(prepared);
        CreateClassMetrics(graph, out var nodeCounts, out var generations);

        var leftIds = prepared.Select(union => union.LeftId).ToArray();
        var rightIds = prepared.Select(union => union.RightId).ToArray();

        if (CobraCudaNative.TryScorePreparedUnionsByClassId(leftIds, rightIds, nodeCounts, generations, out var pairScores))
        {
            return prepared
                .Zip(pairScores, static (union, score) => new { union, score })
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.union.LeftId)
                .ThenBy(item => item.union.RightId)
                .Select(item => item.union)
                .ToList();
        }

        if (CobraCudaNative.TryScoreUnionMembers(uniqueMembers, nodeCounts, generations, out var scores))
        {
            var scoreMap = new Dictionary<int, int>(uniqueMembers.Length);
            for (int i = 0; i < uniqueMembers.Length; i++)
            {
                scoreMap[uniqueMembers[i]] = scores[i];
            }

            return prepared
                .OrderByDescending(union => scoreMap[union.LeftId] + scoreMap[union.RightId])
                .ThenBy(union => union.LeftId)
                .ThenBy(union => union.RightId)
                .ToList();
        }

        return prepared
            .OrderByDescending(union => generations[union.LeftId] + generations[union.RightId])
            .ThenBy(union => union.LeftId)
            .ThenBy(union => union.RightId)
            .ToList();
    }

    private static List<CobraPreparedUnion> OrderPreparedUnions(CobraGraphState graphState, List<CobraPreparedUnion> prepared)
    {
        if (prepared.Count <= 1)
        {
            return prepared;
        }

        int[] uniqueMembers = CollectDistinctUnionMembers(prepared);
        CreateClassMetrics(graphState, out var nodeCounts, out var generations);

        var leftIds = prepared.Select(union => union.LeftId).ToArray();
        var rightIds = prepared.Select(union => union.RightId).ToArray();

        if (CobraCudaNative.TryScorePreparedUnionsByClassId(leftIds, rightIds, nodeCounts, generations, out var pairScores))
        {
            return prepared
                .Zip(pairScores, static (union, score) => new { union, score })
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.union.LeftId)
                .ThenBy(item => item.union.RightId)
                .Select(item => item.union)
                .ToList();
        }

        if (CobraCudaNative.TryScoreUnionMembers(uniqueMembers, nodeCounts, generations, out var scores))
        {
            var scoreMap = new Dictionary<int, int>(uniqueMembers.Length);
            for (int i = 0; i < uniqueMembers.Length; i++)
            {
                scoreMap[uniqueMembers[i]] = scores[i];
            }

            return prepared
                .OrderByDescending(union => scoreMap[union.LeftId] + scoreMap[union.RightId])
                .ThenBy(union => union.LeftId)
                .ThenBy(union => union.RightId)
                .ToList();
        }

        return prepared
            .OrderByDescending(union => generations[union.LeftId] + generations[union.RightId])
            .ThenBy(union => union.LeftId)
            .ThenBy(union => union.RightId)
            .ToList();
    }

    public static CobraUnionPreparationResult Prepare(IEnumerable<(int LeftId, int RightId)> pairs)
    {
        var pairList = pairs.ToList();
        if (pairList.Count == 0)
        {
            return new CobraUnionPreparationResult([], CobraUnionPreparationSource.CpuHeuristic);
        }

        var leftIds = pairList.Select(p => p.LeftId).ToArray();
        var rightIds = pairList.Select(p => p.RightId).ToArray();

        if (CobraCudaNative.TryPrepareUnions(leftIds, rightIds, out var normalizedLeft, out var normalizedRight, out var pairKeys))
        {
            var deduped = new List<CobraPreparedUnion>();
            var seen = new HashSet<ulong>();
            for (int i = 0; i < pairKeys.Length; i++)
            {
                if (normalizedLeft[i] == normalizedRight[i])
                {
                    continue;
                }

                if (seen.Add(pairKeys[i]))
                {
                    deduped.Add(new CobraPreparedUnion(normalizedLeft[i], normalizedRight[i], pairKeys[i]));
                }
            }

            return new CobraUnionPreparationResult(deduped, CobraUnionPreparationSource.Cuda);
        }

        var fallback = pairList
            .Select(pair =>
            {
                int left = pair.LeftId < pair.RightId ? pair.LeftId : pair.RightId;
                int right = pair.LeftId < pair.RightId ? pair.RightId : pair.LeftId;
                ulong key = ((ulong)(uint)left << 32) | (uint)right;
                return new CobraPreparedUnion(left, right, key);
            })
            .Where(p => p.LeftId != p.RightId)
            .DistinctBy(p => p.PairKey)
            .ToList();

        return new CobraUnionPreparationResult(fallback, CobraUnionPreparationSource.CpuHeuristic);
    }

    private static void CreateClassMetrics(EGraph graph, out int[] nodeCounts, out int[] generations)
    {
        nodeCounts = new int[graph.ClassCount];
        generations = new int[graph.ClassCount];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            var eClass = graph.GetClass(classId);
            nodeCounts[classId] = eClass.Nodes.Count;
            generations[classId] = eClass.Generation;
        }
    }

    private static void CreateClassMetrics(CobraGraphState graphState, out int[] nodeCounts, out int[] generations)
    {
        nodeCounts = new int[graphState.ClassCount];
        generations = new int[graphState.ClassCount];
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            var cobraClass = graphState.GetClass(classId);
            nodeCounts[classId] = cobraClass.NodeIds.Count;
            generations[classId] = cobraClass.Generation;
        }
    }

    private static int[] CollectDistinctGroupMembers(IReadOnlyList<CobraUnionBatchGroup> groups)
    {
        var members = new HashSet<int>();
        for (int i = 0; i < groups.Count; i++)
        {
            foreach (int memberId in groups[i].MemberIds)
            {
                members.Add(memberId);
            }
        }

        return members.ToArray();
    }

    private static int[] CollectDistinctUnionMembers(IReadOnlyList<CobraPreparedUnion> preparedUnions)
    {
        var members = new HashSet<int>();
        for (int i = 0; i < preparedUnions.Count; i++)
        {
            members.Add(preparedUnions[i].LeftId);
            members.Add(preparedUnions[i].RightId);
        }

        return members.ToArray();
    }

    private static CobraUnionBatchGroup OrderGroupMembers(CobraUnionBatchGroup group, IReadOnlyDictionary<int, int> scoreMap)
    {
        int[] orderedMembers = group.MemberIds.ToArray();
        System.Array.Sort(orderedMembers, (left, right) =>
        {
            int scoreComparison = scoreMap[right].CompareTo(scoreMap[left]);
            return scoreComparison != 0 ? scoreComparison : left.CompareTo(right);
        });

        return new CobraUnionBatchGroup(orderedMembers[0], orderedMembers);
    }
}
