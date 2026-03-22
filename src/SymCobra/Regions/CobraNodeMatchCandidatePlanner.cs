// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraNodeMatchCandidatePlanner
{
    public static CobraNodeMatchCandidatePlan Build(EGraph graph, IReadOnlyList<int> classIds, IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass)
    {
        return Build(graph, CobraPlannerSnapshot.Create(graph), classIds, rulesByClass, new Dictionary<Rule, CobraRulePatternMetadata>());
    }

    public static CobraNodeMatchCandidatePlan Build(EGraph graph, CobraPlannerSnapshot snapshot, IReadOnlyList<int> classIds, IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass)
    {
        return Build(graph, snapshot, classIds, rulesByClass, new Dictionary<Rule, CobraRulePatternMetadata>());
    }

    public static CobraNodeMatchCandidatePlan Slice(CobraNodeMatchCandidatePlan plan, IReadOnlyList<int> classIds)
    {
        if (classIds.Count == 0 || plan.EligibleNodesByClass.Count == 0)
        {
            return new CobraNodeMatchCandidatePlan(
                new ReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>(new Dictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>()),
                plan.Source);
        }

        var slicedMap = new Dictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>(classIds.Count);
        foreach (int classId in classIds)
        {
            if (plan.EligibleNodesByClass.TryGetValue(classId, out var eligibleRules))
            {
                slicedMap[classId] = eligibleRules;
            }
        }

        return new CobraNodeMatchCandidatePlan(
            new ReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>(slicedMap),
            plan.Source);
    }

    internal static CobraNodeMatchCandidatePlan Build(
        EGraph graph,
        CobraPlannerSnapshot snapshot,
        IReadOnlyList<int> classIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IDictionary<Rule, CobraRulePatternMetadata> ruleMetadataByRule)
    {
        if (classIds.Count == 0 || rulesByClass.Count == 0)
        {
            return new CobraNodeMatchCandidatePlan(
                new ReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>(new Dictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>()),
                CobraNodeMatchCandidateSource.CpuHeuristic);
        }

        var classConstraintMasks = snapshot.ClassConstraintMasks;
        var classHeadBucketMasks = snapshot.ClassHeadBucketMasks;
        var classExactHeadMasks = snapshot.ClassExactHeadMasks;
        var classChildEqualityMasks = snapshot.ClassChildEqualityMasks;
        var classChildAtomBucketMasks = snapshot.ClassChildAtomBucketMasks;
        var classChildConstraintMasks = snapshot.ClassChildConstraintMasks;
        var classChildReferenceBloomMasks = snapshot.ClassChildReferenceBloomMasks;

        var eligibleByClass = new Dictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>();
        bool usedCuda = false;
        var originalRulesByClass = new Dictionary<int, IReadOnlyList<Rule>>();
        var eligibleRuleIndicesByClass = new Dictionary<int, int[]>();
        var directArgInfosByRule = new Dictionary<Rule, IReadOnlyList<CobraNodeMatchEncoding.FlatArgumentInfo>>();

        var batchClassIds = new List<int>();
        var batchNodeOffsets = new List<int>();
        var batchRuleOffsets = new List<int>();
        var batchOutputOffsets = new List<int>();
        var batchNodeCounts = new List<int>();
        var batchRuleCounts = new List<int>();
        var batchNodeHeadCodes = new List<int>();
        var batchNodeArities = new List<int>();
        var batchNodeChildStarts = new List<int>();
        var batchNodeChildIds = new List<int>();
        var batchRuleHeadCodes = new List<int>();
        var batchRuleArities = new List<int>();
        var batchWildcardFlags = new List<int>();
        var batchDirectWildcardFlags = new List<int>();
        var batchRuleArgStarts = new List<int>();
        var batchRuleArgGroupIds = new List<int>();
        var batchRuleArgConstraintMasks = new List<int>();
        var batchRuleArgKinds = new List<int>();
        var batchRuleArgHeadBuckets = new List<int>();
        var batchRuleArgExactHeadMasks = new List<int>();
        var batchRuleArgNestedRepeatMasks = new List<int>();
        var batchRuleArgNestedAtomBucketMasks = new List<int>();
        var batchRuleArgNestedConstraintMasks = new List<int>();
        var batchRuleArgNestedTopLevelReferenceMasks = new List<int>();
        var batchNodes = new List<ENode>();
        var batchRules = new List<Rule>();

        int currentOutputOffset = 0;
        foreach (int classId in classIds)
        {
            if (!rulesByClass.TryGetValue(classId, out var rules) || rules.Count == 0) continue;
            var eClass = graph.GetClass(classId);
            var nodes = eClass.Nodes.ToList();
            if (nodes.Count == 0) continue;
            originalRulesByClass[classId] = rules;

            var eligibleRuleIndices = new List<int>();
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                var metadata = CobraRulePatternMetadataCache.GetOrAdd(ruleMetadataByRule, rules[ruleIndex]);
                if (CanRulePossiblyMatchClass(
                    classId,
                    metadata,
                    classHeadBucketMasks,
                    classChildEqualityMasks,
                    classChildAtomBucketMasks,
                    classChildConstraintMasks))
                {
                    eligibleRuleIndices.Add(ruleIndex);
                }
            }

            eligibleRuleIndicesByClass[classId] = eligibleRuleIndices.ToArray();
            if (eligibleRuleIndices.Count == 0)
            {
                continue;
            }

            batchClassIds.Add(classId);
            batchNodeOffsets.Add(batchNodeHeadCodes.Count);
            batchRuleOffsets.Add(batchRuleHeadCodes.Count);
            batchOutputOffsets.Add(currentOutputOffset);
            batchNodeCounts.Add(nodes.Count);
            batchRuleCounts.Add(eligibleRuleIndices.Count);

            foreach (var node in nodes)
            {
                batchNodeHeadCodes.Add(CobraNodeMatchEncoding.EncodeHeadCode(node.Head));
                batchNodeArities.Add(node.Children.Count);
                batchNodeChildStarts.Add(batchNodeChildIds.Count);
                foreach (int childId in node.Children) batchNodeChildIds.Add(graph.Find(childId));
                batchNodes.Add(node);
            }

            foreach (int ruleIndex in eligibleRuleIndices)
            {
                var rule = rules[ruleIndex];
                var metadata = CobraRulePatternMetadataCache.GetOrAdd(ruleMetadataByRule, rule);
                batchRuleHeadCodes.Add(metadata.HeadCode);
                batchRuleArities.Add(metadata.Arity);
                batchWildcardFlags.Add(metadata.WildcardFlag);
                batchDirectWildcardFlags.Add(metadata.DirectPatternFlag);
                batchRuleArgStarts.Add(batchRuleArgGroupIds.Count);
                var argInfos = metadata.FlatArgumentInfos;
                var nestedAtomMasks = metadata.NestedAtomMasks;
                var nestedConstraintMasks = metadata.NestedConstraintMasks;
                var nestedTopLevelReferenceMasks = metadata.NestedTopLevelReferenceMasks;
                foreach (var info in argInfos)
                {
                    batchRuleArgGroupIds.Add(info.GroupId);
                    batchRuleArgConstraintMasks.Add(info.ConstraintMask);
                    batchRuleArgKinds.Add(info.Kind);
                    batchRuleArgHeadBuckets.Add(info.HeadBucket);
                    batchRuleArgExactHeadMasks.Add(info.ExactHeadMask);
                    batchRuleArgNestedRepeatMasks.Add(info.NestedRepeatMask);
                    int argIndex = batchRuleArgKinds.Count - 1;
                    for (int i = 0; i < 4; i++)
                    {
                        batchRuleArgNestedAtomBucketMasks.Add(argIndex < nestedAtomMasks.Length / 4 ? nestedAtomMasks[(argIndex * 4) + i] : 0);
                        batchRuleArgNestedConstraintMasks.Add(argIndex < nestedConstraintMasks.Length / 4 ? nestedConstraintMasks[(argIndex * 4) + i] : 0);
                        batchRuleArgNestedTopLevelReferenceMasks.Add(argIndex < nestedTopLevelReferenceMasks.Length / 4 ? nestedTopLevelReferenceMasks[(argIndex * 4) + i] : -1);
                    }
                }
                batchRules.Add(rule);
            }

            currentOutputOffset += nodes.Count * eligibleRuleIndices.Count;
        }

        if (batchClassIds.Count > 0 &&
            CobraCudaNative.TryScoreNodeRuleCandidatesBatchV4(
                batchNodeOffsets.ToArray(),
                batchRuleOffsets.ToArray(),
                batchOutputOffsets.ToArray(),
                batchNodeCounts.ToArray(),
                batchRuleCounts.ToArray(),
                batchNodeHeadCodes.ToArray(),
                batchNodeArities.ToArray(),
                batchNodeChildStarts.ToArray(),
                batchNodeChildIds.ToArray(),
                classConstraintMasks,
                classHeadBucketMasks,
                classExactHeadMasks,
                classChildEqualityMasks,
                classChildAtomBucketMasks,
                classChildConstraintMasks,
                classChildReferenceBloomMasks,
                batchRuleHeadCodes.ToArray(),
                batchRuleArities.ToArray(),
                batchWildcardFlags.ToArray(),
                batchDirectWildcardFlags.ToArray(),
                batchRuleArgStarts.ToArray(),
                batchRuleArgGroupIds.ToArray(),
                batchRuleArgConstraintMasks.ToArray(),
                batchRuleArgKinds.ToArray(),
                batchRuleArgHeadBuckets.ToArray(),
                batchRuleArgExactHeadMasks.ToArray(),
                batchRuleArgNestedRepeatMasks.ToArray(),
                batchRuleArgNestedAtomBucketMasks.ToArray(),
                batchRuleArgNestedConstraintMasks.ToArray(),
                batchRuleArgNestedTopLevelReferenceMasks.ToArray(),
                currentOutputOffset,
                batchNodeHeadCodes.Count,
                batchRuleHeadCodes.Count,
                batchNodeChildIds.Count,
                batchRuleArgGroupIds.Count,
                batchClassIds.Count,
                graph.ClassCount,
                out var batchScores))
        {
            usedCuda = true;
            for (int i = 0; i < batchClassIds.Count; i++)
            {
                int classId = batchClassIds[i];
                int nNodes = batchNodeCounts[i];
                int nRules = batchRuleCounts[i];
                int outOffset = batchOutputOffsets[i];
                int nodeBase = batchNodeOffsets[i];
                int ruleBase = batchRuleOffsets[i];
                var originalRules = originalRulesByClass[classId];
                var eligibleRuleIndices = eligibleRuleIndicesByClass[classId];

                var allowedByRuleIndex = new Dictionary<int, HashSet<ENode>>();
                for (int ruleIndex = 0; ruleIndex < originalRules.Count; ruleIndex++)
                {
                    allowedByRuleIndex[ruleIndex] = [];
                }

                for (int eligibleRuleIndex = 0; eligibleRuleIndex < nRules; eligibleRuleIndex++)
                {
                    var allowed = new HashSet<ENode>();
                    for (int nodeIdxInClass = 0; nodeIdxInClass < nNodes; nodeIdxInClass++)
                    {
                        if (batchScores[outOffset + (nodeIdxInClass * nRules) + eligibleRuleIndex] > 0)
                        {
                            allowed.Add(batchNodes[nodeBase + nodeIdxInClass]);
                        }
                    }
                    allowedByRuleIndex[eligibleRuleIndices[eligibleRuleIndex]] = allowed;
                }

                eligibleByClass[classId] = BuildScoredRuleMap(allowedByRuleIndex, originalRules, CobraNodeMatchCandidateSource.Cuda, ruleMetadataByRule);
            }

            foreach (var (classId, rules) in originalRulesByClass)
            {
                if (eligibleByClass.ContainsKey(classId))
                {
                    continue;
                }

                var allowedByRuleIndex = new Dictionary<int, HashSet<ENode>>();
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    allowedByRuleIndex[ruleIndex] = [];
                }

                eligibleByClass[classId] = BuildScoredRuleMap(allowedByRuleIndex, rules, CobraNodeMatchCandidateSource.Cuda, ruleMetadataByRule);
            }
        }
        else
        {
            // Fallback
            foreach (int classId in classIds)
            {
                if (!rulesByClass.TryGetValue(classId, out var rules) || rules.Count == 0) continue;
                var eClass = graph.GetClass(classId);
                var nodes = eClass.Nodes.ToList();
                if (nodes.Count == 0) continue;
                var eligibleRuleIndices = eligibleRuleIndicesByClass.TryGetValue(classId, out var prefilteredRuleIndices)
                    ? prefilteredRuleIndices
                    : [];

                var allowedByRuleIndex = new Dictionary<int, HashSet<ENode>>();
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    allowedByRuleIndex[ruleIndex] = [];
                }

                foreach (int ruleIndex in eligibleRuleIndices)
                {
                    var allowed = allowedByRuleIndex[ruleIndex];
                    var metadata = CobraRulePatternMetadataCache.GetOrAdd(ruleMetadataByRule, rules[ruleIndex]);

                    foreach (var node in nodes)
                    {
                        if (metadata.IsWildcard || (CobraNodeMatchEncoding.EncodeHeadCode(node.Head) == metadata.HeadCode && node.Children.Count == metadata.Arity))
                        {
                            allowed.Add(node);
                        }
                    }
                }

                eligibleByClass[classId] = BuildScoredRuleMap(allowedByRuleIndex, rules, CobraNodeMatchCandidateSource.CpuHeuristic, ruleMetadataByRule);
            }
        }

        return new CobraNodeMatchCandidatePlan(
            new ReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>(eligibleByClass),
            usedCuda ? CobraNodeMatchCandidateSource.Cuda : CobraNodeMatchCandidateSource.CpuHeuristic);
    }

    private static IReadOnlyDictionary<Rule, HashSet<ENode>> BuildScoredRuleMap(
        Dictionary<int, HashSet<ENode>> allowedByRuleIndex,
        IReadOnlyList<Rule> rules,
        CobraNodeMatchCandidateSource source,
        IDictionary<Rule, CobraRulePatternMetadata> ruleMetadataByRule)
    {
        var allowedByRule = new Dictionary<Rule, HashSet<ENode>>();
        var scoredRules = new List<(Rule Rule, int Score)>();
        var allowedCounts = new List<int>();
        var arities = new List<int>();
        var directFlags = new List<int>();
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            var allowed = allowedByRuleIndex[ruleIndex];
            allowedByRule[rules[ruleIndex]] = allowed;
            if (allowed.Count > 0)
            {
                var metadata = CobraRulePatternMetadataCache.GetOrAdd(ruleMetadataByRule, rules[ruleIndex]);
                allowedCounts.Add(allowed.Count);
                arities.Add(metadata.Arity);
                directFlags.Add(metadata.DirectPatternFlag);
                scoredRules.Add((rules[ruleIndex], 0));
            }
        }

        int[] ruleScores = ScoreCandidateRules(allowedCounts.ToArray(), arities.ToArray(), directFlags.ToArray());
        for (int i = 0; i < scoredRules.Count; i++)
        {
            scoredRules[i] = (scoredRules[i].Rule, ruleScores[i]);
        }

        var scoreByRule = scoredRules.ToDictionary(item => item.Rule, item => item.Score);
        return rules
            .OrderByDescending(rule => scoreByRule.GetValueOrDefault(rule))
            .ThenBy(rule => rule.Pattern.ToDisplayString(), StringComparer.Ordinal)
            .ToDictionary(rule => rule, rule => allowedByRule[rule]);
    }

    internal static IReadOnlyDictionary<int, IReadOnlyList<Rule>> FilterRulesByPossibleCandidates(
        CobraPlannerSnapshot snapshot,
        IReadOnlyList<int> classIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IDictionary<Rule, CobraRulePatternMetadata>? ruleMetadataByRule = null)
    {
        if (classIds.Count == 0 || rulesByClass.Count == 0)
        {
            return new ReadOnlyDictionary<int, IReadOnlyList<Rule>>(new Dictionary<int, IReadOnlyList<Rule>>());
        }

        ruleMetadataByRule ??= new Dictionary<Rule, CobraRulePatternMetadata>();
        var classHeadBucketMasks = snapshot.ClassHeadBucketMasks;
        var classChildEqualityMasks = snapshot.ClassChildEqualityMasks;
        var classChildAtomBucketMasks = snapshot.ClassChildAtomBucketMasks;
        var classChildConstraintMasks = snapshot.ClassChildConstraintMasks;
        var filteredRulesByClass = new Dictionary<int, IReadOnlyList<Rule>>();

        foreach (int classId in classIds)
        {
            if (!rulesByClass.TryGetValue(classId, out var rules) || rules.Count == 0)
            {
                continue;
            }

            var filteredRules = new List<Rule>();
            foreach (var rule in rules)
            {
                var metadata = CobraRulePatternMetadataCache.GetOrAdd(ruleMetadataByRule, rule);
                if (CanRulePossiblyMatchClass(
                    classId,
                    metadata,
                    classHeadBucketMasks,
                    classChildEqualityMasks,
                    classChildAtomBucketMasks,
                    classChildConstraintMasks))
                {
                    filteredRules.Add(rule);
                }
            }

            if (filteredRules.Count > 0)
            {
                filteredRulesByClass[classId] = filteredRules;
            }
        }

        return new ReadOnlyDictionary<int, IReadOnlyList<Rule>>(filteredRulesByClass);
    }

    private static int[] ScoreCandidateRules(int[] allowedCounts, int[] arities, int[] directFlags)
    {
        if (allowedCounts.Length == 0) return [];
        if (CobraCudaNative.TryScoreCandidateRules(allowedCounts, arities, directFlags, out var scores)) return scores;
        return allowedCounts
            .Select((count, index) => (count == 0 ? 0 : 1000 / count) + (arities[index] * 12) + (directFlags[index] * 40))
            .ToArray();
    }

    private static bool CanRulePossiblyMatchClass(
        int classId,
        CobraRulePatternMetadata metadata,
        int[] classHeadBucketMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks)
    {
        if (metadata.IsWildcard || !metadata.IsOneLevelDirectPattern)
        {
            return true;
        }

        int requiredTopLevelBucketMask = 1 << metadata.HeadBucket;
        if ((classHeadBucketMasks[classId] & requiredTopLevelBucketMask) == 0)
        {
            return false;
        }

        if (metadata.Arity > 4)
        {
            return true;
        }

        var argInfos = metadata.FlatArgumentInfos;

        int requiredEqualityMask = 0;
        for (int argIndex = 0; argIndex < argInfos.Count; argIndex++)
        {
            var info = argInfos[argIndex];
            if (info.ConstraintMask != 0 &&
                (classChildConstraintMasks[(classId * 4) + argIndex] & info.ConstraintMask) == 0)
            {
                return false;
            }

            if (info.Kind == 1)
            {
                int requiredBucketMask = 1 << info.HeadBucket;
                if ((classChildAtomBucketMasks[(classId * 4) + argIndex] & requiredBucketMask) == 0)
                {
                    return false;
                }
            }

            if (info.GroupId < argIndex)
            {
                requiredEqualityMask |= CobraNodeMatchEncoding.EncodeChildEqualityPairMask(info.GroupId, argIndex);
            }
        }

        return requiredEqualityMask == 0 || (classChildEqualityMasks[classId] & requiredEqualityMask) == requiredEqualityMask;
    }
}
