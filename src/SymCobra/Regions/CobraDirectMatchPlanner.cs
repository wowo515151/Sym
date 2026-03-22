// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraDirectMatchPlanner
{
    public static CobraDirectMatchPlan Build(EGraph graph, IReadOnlyList<int> classIds, IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass)
    {
        return Build(graph, CobraPlannerSnapshot.Create(graph), classIds, rulesByClass, new Dictionary<Rule, CobraRulePatternMetadata>());
    }

    public static CobraDirectMatchPlan Build(EGraph graph, CobraPlannerSnapshot snapshot, IReadOnlyList<int> classIds, IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass)
    {
        return Build(graph, snapshot, classIds, rulesByClass, new Dictionary<Rule, CobraRulePatternMetadata>());
    }

    public static CobraDirectMatchPlan Slice(CobraDirectMatchPlan plan, IReadOnlyList<int> classIds)
    {
        if (classIds.Count == 0 || plan.PairsByClass.Count == 0)
        {
            return new CobraDirectMatchPlan(
                new ReadOnlyDictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>(new Dictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>()),
                plan.Source,
                plan.ExhaustivelyHandledRulesByClass is null
                    ? null
                    : new ReadOnlyDictionary<int, IReadOnlySet<Rule>>(new Dictionary<int, IReadOnlySet<Rule>>()));
        }

        var slicedPairs = new Dictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>(classIds.Count);
        foreach (int classId in classIds)
        {
            if (plan.PairsByClass.TryGetValue(classId, out var pairsByRule))
            {
                slicedPairs[classId] = pairsByRule;
            }
        }

        IReadOnlyDictionary<int, IReadOnlySet<Rule>>? slicedHandledRules = null;
        if (plan.ExhaustivelyHandledRulesByClass is not null)
        {
            var handledRules = new Dictionary<int, IReadOnlySet<Rule>>(classIds.Count);
            foreach (int classId in classIds)
            {
                if (plan.ExhaustivelyHandledRulesByClass.TryGetValue(classId, out var rules))
                {
                    handledRules[classId] = rules;
                }
            }

            slicedHandledRules = new ReadOnlyDictionary<int, IReadOnlySet<Rule>>(handledRules);
        }

        return new CobraDirectMatchPlan(
            new ReadOnlyDictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>(slicedPairs),
            plan.Source,
            slicedHandledRules);
    }

    internal static CobraDirectMatchPlan Build(
        EGraph graph,
        CobraPlannerSnapshot snapshot,
        IReadOnlyList<int> classIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IDictionary<Rule, CobraRulePatternMetadata> ruleMetadataByRule)
    {
        if (classIds.Count == 0 || rulesByClass.Count == 0)
        {
            return new CobraDirectMatchPlan(
                new ReadOnlyDictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>(new Dictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>()),
                CobraDirectMatchSource.CpuHeuristic);
        }

        var allNodeCounts = snapshot.NodeCounts;
        var allGenerations = snapshot.Generations;
        var classConstraintMasks = snapshot.ClassConstraintMasks;
        var classHeadBucketMasks = snapshot.ClassHeadBucketMasks;
        var classExactHeadMasks = snapshot.ClassExactHeadMasks;
        var classChildEqualityMasks = snapshot.ClassChildEqualityMasks;
        var classChildAtomBucketMasks = snapshot.ClassChildAtomBucketMasks;
        var classChildConstraintMasks = snapshot.ClassChildConstraintMasks;
        var classChildReferenceBloomMasks = snapshot.ClassChildReferenceBloomMasks;

        var scoredClasses = new List<(int ClassId, int Score, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>> Rules)>();
        var exhaustivelyHandledRulesByClass = new Dictionary<int, HashSet<Rule>>();
        bool usedCuda = false;

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
        var batchDirectRules = new List<Rule>();

        int currentOutputOffset = 0;
        foreach (int classId in classIds)
        {
            if (!rulesByClass.TryGetValue(classId, out var classRules)) continue;
            var directRules = classRules.Where(CanUseCudaStructuralDirectMatch).ToList();
            if (directRules.Count == 0) continue;

            var eClass = graph.GetClass(classId);
            var nodes = eClass.Nodes.ToList();
            if (nodes.Count == 0) continue;
            RecordExhaustivelyHandledRules(
                exhaustivelyHandledRulesByClass,
                classId,
                directRules.Where(CanUseExhaustiveStructuralDirectMatch));
            batchClassIds.Add(classId);
            batchNodeOffsets.Add(batchNodeHeadCodes.Count);
            batchRuleOffsets.Add(batchRuleHeadCodes.Count);
            batchOutputOffsets.Add(currentOutputOffset);
            batchNodeCounts.Add(nodes.Count);
            batchRuleCounts.Add(directRules.Count);

            foreach (var node in nodes)
            {
                batchNodeHeadCodes.Add(CobraNodeMatchEncoding.EncodeHeadCode(node.Head));
                batchNodeArities.Add(node.Children.Count);
                batchNodeChildStarts.Add(batchNodeChildIds.Count);
                foreach (int childId in node.Children) batchNodeChildIds.Add(graph.Find(childId));
                batchNodes.Add(node);
            }

            foreach (var rule in directRules)
            {
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

                for (int i = 0; i < argInfos.Count; i++)
                {
                    var info = argInfos[i];
                    batchRuleArgGroupIds.Add(info.GroupId);
                    batchRuleArgConstraintMasks.Add(info.ConstraintMask);
                    batchRuleArgKinds.Add(info.Kind);
                    batchRuleArgHeadBuckets.Add(info.HeadBucket);
                    batchRuleArgExactHeadMasks.Add(info.ExactHeadMask);
                    batchRuleArgNestedRepeatMasks.Add(info.NestedRepeatMask);

                    for (int j = 0; j < 4; j++)
                    {
                        batchRuleArgNestedAtomBucketMasks.Add(i < nestedAtomMasks.Length / 4 ? nestedAtomMasks[(i * 4) + j] : 0);
                        batchRuleArgNestedConstraintMasks.Add(i < nestedConstraintMasks.Length / 4 ? nestedConstraintMasks[(i * 4) + j] : 0);
                        batchRuleArgNestedTopLevelReferenceMasks.Add(i < nestedTopLevelReferenceMasks.Length / 4 ? nestedTopLevelReferenceMasks[(i * 4) + j] : -1);
                    }
                }
                batchDirectRules.Add(rule);
            }

            currentOutputOffset += nodes.Count * directRules.Count;
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

                var pairsByRuleIndex = new Dictionary<int, List<CobraDirectMatchPair>>();
                var currentClassRules = new List<Rule>();
                for (int r = 0; r < nRules; r++) currentClassRules.Add(batchDirectRules[ruleBase + r]);

                for (int ruleIdxInClass = 0; ruleIdxInClass < nRules; ruleIdxInClass++)
                {
                    var list = new List<CobraDirectMatchPair>();
                    var rule = currentClassRules[ruleIdxInClass];
                    for (int nodeIdxInClass = 0; nodeIdxInClass < nNodes; nodeIdxInClass++)
                    {
                        if (batchScores[outOffset + (nodeIdxInClass * nRules) + ruleIdxInClass] > 0)
                        {
                            list.Add(new CobraDirectMatchPair(classId, rule, batchNodes[nodeBase + nodeIdxInClass]));
                        }
                    }
                    list = CompleteExactPairsForSafeStructuralRule(
                        graph,
                        classId,
                        rule,
                        list,
                        batchNodes.GetRange(nodeBase, nNodes));
                    pairsByRuleIndex[ruleIdxInClass] = list;
                }

                ProcessClassMatches(classId, currentClassRules, pairsByRuleIndex, scoredClasses, CobraDirectMatchSource.Cuda, ruleMetadataByRule, allNodeCounts, allGenerations);
            }
        }
        else
        {
            // Fallback
            foreach (int classId in classIds)
            {
                if (!rulesByClass.TryGetValue(classId, out var classRules)) continue;
                var directRules = classRules.Where(CanUseCudaStructuralDirectMatch).ToList();
                if (directRules.Count == 0) continue;
                var eClass = graph.GetClass(classId);
                var nodes = eClass.Nodes.ToList();
                if (nodes.Count == 0) continue;

                var pairsByRuleIndex = new Dictionary<int, List<CobraDirectMatchPair>>();
                for (int ruleIndex = 0; ruleIndex < directRules.Count; ruleIndex++)
                {
                    var list = new List<CobraDirectMatchPair>();
                    var metadata = CobraRulePatternMetadataCache.GetOrAdd(ruleMetadataByRule, directRules[ruleIndex]);

                    foreach (var node in nodes)
                    {
                        bool isCompatible = metadata.IsWildcard || (CobraNodeMatchEncoding.EncodeHeadCode(node.Head) == metadata.HeadCode && node.Children.Count == metadata.Arity);
                        if (isCompatible)
                        {
                            list.Add(new CobraDirectMatchPair(classId, directRules[ruleIndex], node));
                        }
                    }
                    list = CompleteExactPairsForSafeStructuralRule(
                        graph,
                        classId,
                        directRules[ruleIndex],
                        list,
                        nodes);
                    pairsByRuleIndex[ruleIndex] = list;
                }

                ProcessClassMatches(classId, directRules, pairsByRuleIndex, scoredClasses, CobraDirectMatchSource.CpuHeuristic, ruleMetadataByRule, allNodeCounts, allGenerations);
            }
        }

        AppendCandidateScreenedDirectMatches(
            graph,
            snapshot,
            classIds,
            rulesByClass,
            ruleMetadataByRule,
            scoredClasses,
            allNodeCounts,
            allGenerations,
            exhaustivelyHandledRulesByClass,
            ref usedCuda);

        int[] directClassScores = ScoreDirectClasses(scoredClasses, allNodeCounts, allGenerations);
        var byClass = scoredClasses
            .Zip(directClassScores, (item, score) => new { item.ClassId, item.Rules, Score = score })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ClassId)
            .ToDictionary(x => x.ClassId, x => x.Rules);
        var handledRules = exhaustivelyHandledRulesByClass.ToDictionary(
            static entry => entry.Key,
            static entry => (IReadOnlySet<Rule>)entry.Value);

        return new CobraDirectMatchPlan(
            new ReadOnlyDictionary<int, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>>(byClass),
            usedCuda ? CobraDirectMatchSource.Cuda : CobraDirectMatchSource.CpuHeuristic,
            new ReadOnlyDictionary<int, IReadOnlySet<Rule>>(handledRules));
    }

    private static void ProcessClassMatches(
        int classId,
        IReadOnlyList<Rule> directRules,
        Dictionary<int, List<CobraDirectMatchPair>> pairsByRuleIndex,
        List<(int ClassId, int Score, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>> Rules)> scoredClasses,
        CobraDirectMatchSource source,
        IDictionary<Rule, CobraRulePatternMetadata> ruleMetadataByRule,
        int[] allNodeCounts,
        int[] allGenerations)
    {
        var scoredRules = new List<(Rule Rule, int Score, IReadOnlyList<CobraDirectMatchPair> Pairs)>();
        var pairCounts = new List<int>();
        var arities = new List<int>();
        var nestedFlags = new List<int>();
        int classGeneration = allGenerations[classId];
        int classNodeCount = allNodeCounts[classId];
        int classBasePairScore = (classGeneration * 16) + (classNodeCount * 4);

        for (int ruleIndex = 0; ruleIndex < directRules.Count; ruleIndex++)
        {
            var pairs = pairsByRuleIndex[ruleIndex];
            int pairScore = 0;
            if (pairs.Count > 0)
            {
                pairScore = ScoreDirectPairs(pairs, allNodeCounts, allGenerations);
                pairs = pairs
                    .OrderByDescending(p => classBasePairScore + p.Node.Children.Count)
                    .ThenBy(p => p.Node.Head, StringComparer.Ordinal)
                    .ThenBy(p => p.Node.Children.Count)
                    .ToList();
            }

            if (pairs.Count > 0 || source == CobraDirectMatchSource.Cuda)
            {
                pairCounts.Add(pairs.Count);
                arities.Add(CobraRulePatternMetadataCache.GetOrAdd(ruleMetadataByRule, directRules[ruleIndex]).Arity);
                nestedFlags.Add(HasNestedOperationArgument(directRules[ruleIndex]) ? 1 : 0);
                scoredRules.Add((directRules[ruleIndex], pairScore, pairs));
            }
        }

        if (scoredRules.Count > 0)
        {
            var directRuleScores = ScoreDirectRules(pairCounts.ToArray(), arities.ToArray(), nestedFlags.ToArray());
            var orderedRules = scoredRules
                .Zip(directRuleScores, (entry, ruleScore) => new { entry.Rule, entry.Pairs, Score = entry.Score + ruleScore })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Pairs.Count)
                .ThenBy(x => x.Rule.Pattern.ToDisplayString(), StringComparer.Ordinal)
                .ToDictionary(x => x.Rule, x => (IReadOnlyList<CobraDirectMatchPair>)x.Pairs);
            int totalClassScore = scoredRules.Sum(sr => sr.Score);
            int existingIndex = scoredClasses.FindIndex(entry => entry.ClassId == classId);
            if (existingIndex >= 0)
            {
                var existing = scoredClasses[existingIndex];
                var mergedRules = existing.Rules.ToDictionary(entry => entry.Key, entry => entry.Value);
                foreach (var entry in orderedRules)
                {
                    mergedRules[entry.Key] = entry.Value;
                }

                scoredClasses[existingIndex] = (
                    classId,
                    existing.Score + totalClassScore,
                    new ReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>(mergedRules));
            }
            else
            {
                scoredClasses.Add((classId, totalClassScore, new ReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>>(orderedRules)));
            }
        }
    }

    private static void AppendCandidateScreenedDirectMatches(
        EGraph graph,
        CobraPlannerSnapshot snapshot,
        IReadOnlyList<int> classIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>> rulesByClass,
        IDictionary<Rule, CobraRulePatternMetadata> ruleMetadataByRule,
        List<(int ClassId, int Score, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>> Rules)> scoredClasses,
        int[] allNodeCounts,
        int[] allGenerations,
        Dictionary<int, HashSet<Rule>> exhaustivelyHandledRulesByClass,
        ref bool usedCuda)
    {
        var candidateRulesByClass = new Dictionary<int, IReadOnlyList<Rule>>();
        var candidateClassIds = new List<int>();
        foreach (int classId in classIds)
        {
            if (!rulesByClass.TryGetValue(classId, out var classRules))
            {
                continue;
            }

            var nestedRules = classRules.Where(CanUseCandidateScreenedDirectMatch).ToList();
            if (nestedRules.Count == 0)
            {
                continue;
            }

            candidateRulesByClass[classId] = nestedRules;
            candidateClassIds.Add(classId);
        }

        if (candidateClassIds.Count == 0)
        {
            return;
        }

        candidateRulesByClass = CobraNodeMatchCandidatePlanner.FilterRulesByPossibleCandidates(snapshot, candidateClassIds, candidateRulesByClass, ruleMetadataByRule)
            .ToDictionary(static entry => entry.Key, static entry => entry.Value);
        candidateClassIds = candidateClassIds.Where(candidateRulesByClass.ContainsKey).ToList();
        if (candidateClassIds.Count == 0)
        {
            return;
        }

        var candidatePlan = CobraNodeMatchCandidatePlanner.Build(graph, snapshot, candidateClassIds, candidateRulesByClass, ruleMetadataByRule);
        if (candidatePlan.Source == CobraNodeMatchCandidateSource.Cuda)
        {
            usedCuda = true;
        }

        CobraDirectMatchSource source = candidatePlan.Source == CobraNodeMatchCandidateSource.Cuda
            ? CobraDirectMatchSource.Cuda
            : CobraDirectMatchSource.CpuHeuristic;

        foreach (int classId in candidateClassIds)
        {
            var nestedRules = candidateRulesByClass[classId];
            RecordExhaustivelyHandledRules(
                exhaustivelyHandledRulesByClass,
                classId,
                nestedRules.Where(static rule => rule.Pattern is Wild or Atom));
            candidatePlan.EligibleNodesByClass.TryGetValue(classId, out var ruleNodeMap);
            var classNodes = graph.GetClass(classId).Nodes.ToList();
            var pairsByRuleIndex = new Dictionary<int, List<CobraDirectMatchPair>>();
            for (int ruleIndex = 0; ruleIndex < nestedRules.Count; ruleIndex++)
            {
                var rule = nestedRules[ruleIndex];
                var metadata = CobraRulePatternMetadataCache.GetOrAdd(ruleMetadataByRule, rule);
                List<CobraDirectMatchPair> pairs;
                if (!metadata.IsOneLevelDirectPattern)
                {
                    pairs = GetTopLevelCompatibleNodes(classNodes, rule)
                        .Select(node => new CobraDirectMatchPair(classId, rule, node))
                        .ToList();
                }
                else if (ruleNodeMap != null && ruleNodeMap.TryGetValue(rule, out var eligibleNodes))
                {
                    if (rule.Pattern is Wild or Atom)
                    {
                        pairs = eligibleNodes.Take(1).Select(node => new CobraDirectMatchPair(classId, rule, node)).ToList();
                    }
                    else
                    {
                        pairs = eligibleNodes.Select(node => new CobraDirectMatchPair(classId, rule, node)).ToList();
                    }
                }
                else
                {
                    pairs = [];
                }

                pairsByRuleIndex[ruleIndex] = pairs;
            }

            ProcessClassMatches(classId, nestedRules, pairsByRuleIndex, scoredClasses, source, ruleMetadataByRule, allNodeCounts, allGenerations);
        }
    }

    private static IEnumerable<ENode> GetTopLevelCompatibleNodes(IReadOnlyList<ENode> nodes, Rule rule)
    {
        if (rule.Pattern is Wild)
        {
            return nodes.Take(1);
        }

        string head = ENode.GetHead(rule.Pattern);
        int arity = GetRuleArity(rule);
        return nodes.Where(node => node.Head == head && node.Children.Count == arity);
    }

    private static int ScoreDirectPairs(IReadOnlyList<CobraDirectMatchPair> pairs, int[] allNodeCounts, int[] allGenerations)
    {
        if (pairs.Count == 0) return 0;
        var classIds = pairs.Select(pair => pair.ClassId).ToArray();
        var nodeArities = pairs.Select(pair => pair.Node.Children.Count).ToArray();
        var nestedFlags = pairs.Select(pair => HasNestedOperationArgument(pair.Rule) ? 1 : 0).ToArray();

        if (CobraCudaNative.TryScoreDirectPairsV2ById(classIds, nodeArities, nestedFlags, allNodeCounts, allGenerations, out var scores))
            return scores.Sum();

        int total = 0;
        for (int i = 0; i < pairs.Count; i++)
        {
            total += ScoreDirectPair(pairs[i], allNodeCounts, allGenerations);
        }

        return total;
    }

    private static int[] ScoreDirectClasses(
        IReadOnlyList<(int ClassId, int Score, IReadOnlyDictionary<Rule, IReadOnlyList<CobraDirectMatchPair>> Rules)> classes,
        int[] allNodeCounts,
        int[] allGenerations)
    {
        if (classes.Count == 0) return [];
        var pairCounts = classes.Select(item => item.Rules.Sum(ruleEntry => ruleEntry.Value.Count)).ToArray();
        var nestedPairCounts = classes.Select(item => item.Rules.Sum(ruleEntry => ruleEntry.Value.Count(pair => HasNestedOperationArgument(pair.Rule)))).ToArray();
        var classIds = classes.Select(item => item.ClassId).ToArray();
        
        if (CobraCudaNative.TryScoreDirectClassesV2ById(
            classIds, pairCounts, nestedPairCounts,
            allNodeCounts,
            allGenerations,
            out var scores))
            return scores;

        return classes.Select(item => item.Score).ToArray();
    }

    private static int ScoreDirectPair(CobraDirectMatchPair pair, int[] allNodeCounts, int[] allGenerations)
    {
        return (allGenerations[pair.ClassId] * 16) + (allNodeCounts[pair.ClassId] * 4) + pair.Node.Children.Count;
    }

    private static int[] ScoreDirectRules(int[] pairCounts, int[] arities, int[] nestedFlags)
    {
        if (pairCounts.Length == 0) return [];
        if (CobraCudaNative.TryScoreDirectRules(pairCounts, arities, nestedFlags, out var scores)) return scores;
        return pairCounts.Select((count, index) => (count * 32) + (arities[index] * 12) + (nestedFlags[index] * 24)).ToArray();
    }

    private static bool CanUseDirectGpuMatch(Rule rule) => rule.Pattern is Wild or Atom || CobraNodeMatchEncoding.IsBoundedDirectPattern(rule.Pattern, CobraNodeMatchEncoding.MaxDirectNestedOperationDepth);
    private static bool CanUseCudaStructuralDirectMatch(Rule rule) => CobraNodeMatchEncoding.IsBoundedDirectPattern(rule.Pattern, CobraNodeMatchEncoding.MaxCudaStructuralDirectNestedOperationDepth);
    private static bool CanUseCandidateScreenedDirectMatch(Rule rule) => CanUseDirectGpuMatch(rule) && !CanUseCudaStructuralDirectMatch(rule);
    private static bool CanUseExhaustiveStructuralDirectMatch(Rule rule)
    {
        if (IsPureEqualitySwapRule(rule))
        {
            return true;
        }

        if (rule.Pattern is not Equality equality)
        {
            return false;
        }

        bool leftIsOperation = equality.LeftOperand is Operation;
        bool rightIsOperation = equality.RightOperand is Operation;
        bool isIsolationEquality =
            leftIsOperation != rightIsOperation &&
            rule.Replacement is Equality;
        bool isStructuralEquality =
            leftIsOperation &&
            rightIsOperation &&
            IsSafeStructuralEqualityReplacement(rule.Replacement);

        return rule.Condition is null &&
               rule.AssumptionCondition is null &&
               rule.Transform is null &&
               (isIsolationEquality || isStructuralEquality) &&
               IsSafeGpuOwnedEqualityOperand(equality.LeftOperand) &&
               IsSafeGpuOwnedEqualityOperand(equality.RightOperand);
    }

    private static bool IsSafeGpuOwnedEqualityOperand(IExpression operand)
    {
        return operand switch
        {
            Wild => true,
            Atom => true,
            Operation op => CobraNodeMatchEncoding.IsOneLevelDirectChildPattern(op),
            _ => false
        };
    }

    private static bool IsSafeStructuralEqualityReplacement(IExpression replacement)
    {
        if (replacement is Equality)
        {
            return true;
        }

        return replacement is Vector vector &&
               vector.Arguments.All(static arg => arg is Equality);
    }

    private static bool IsPureEqualitySwapRule(Rule rule)
    {
        return rule.Condition is null &&
               rule.AssumptionCondition is null &&
               rule.Transform is null &&
               rule.Pattern is Equality
               {
                   LeftOperand: Wild left,
                   RightOperand: Wild right
               } &&
               rule.Replacement is Equality
               {
                   LeftOperand: Wild replacementLeft,
                   RightOperand: Wild replacementRight
               } &&
               string.Equals(left.Name, replacementRight.Name, StringComparison.Ordinal) &&
               string.Equals(right.Name, replacementLeft.Name, StringComparison.Ordinal) &&
               left.Constraint == replacementRight.Constraint &&
               right.Constraint == replacementLeft.Constraint;
    }

    private static List<CobraDirectMatchPair> CompleteExactPairsForSafeStructuralRule(
        EGraph graph,
        int classId,
        Rule rule,
        List<CobraDirectMatchPair> candidatePairs,
        IReadOnlyList<ENode> nodes)
    {
        if (!CanUseExhaustiveStructuralDirectMatch(rule))
        {
            return candidatePairs;
        }

        var completedPairs = candidatePairs.Count == 0
            ? new List<CobraDirectMatchPair>()
            : new List<CobraDirectMatchPair>(candidatePairs);
        var seenNodeKeys = new HashSet<string>(
            completedPairs.Select(static pair => GetNodeKey(pair.Node)),
            StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (!seenNodeKeys.Add(GetNodeKey(node)))
            {
                continue;
            }

            var pair = new CobraDirectMatchPair(classId, rule, node);
            if (CobraDirectMatchMaterializer.CanBuild(graph, pair))
            {
                completedPairs.Add(pair);
            }
        }

        return completedPairs;
    }

    private static string GetNodeKey(ENode node)
    {
        return $"{node.Head}|{string.Join(",", node.Children)}";
    }

    private static bool HasNestedOperationArgument(Rule rule) => rule.Pattern is Operation op && op.Arguments.Any(arg => arg is Operation);
    private static IReadOnlyList<CobraNodeMatchEncoding.FlatArgumentInfo> BuildFlatArgumentInfos(IExpression pattern) => CobraNodeMatchEncoding.BuildFlatArgumentInfos(pattern);
    private static int GetRuleHeadCode(Rule rule) => rule.Pattern is Wild ? 0 : CobraNodeMatchEncoding.EncodeHeadCode(ENode.GetHead(rule.Pattern));
    private static int GetRuleArity(Rule rule) => rule.Pattern is Operation op ? op.Arguments.Count : 0;

    private static void RecordExhaustivelyHandledRules(
        Dictionary<int, HashSet<Rule>> handledRulesByClass,
        int classId,
        IEnumerable<Rule> rules)
    {
        if (!handledRulesByClass.TryGetValue(classId, out var handledRules))
        {
            handledRules = new HashSet<Rule>();
            handledRulesByClass[classId] = handledRules;
        }

        foreach (var rule in rules)
        {
            handledRules.Add(rule);
        }
    }
}
