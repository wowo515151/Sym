// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraRuleCompatibilityPlanner
{
    private const int OtherBit = 1 << 30;

    public static CobraRuleCompatibilityPlan Build(EGraph graph, IReadOnlyList<int> classIds, IReadOnlyList<Rule> rules)
    {
        if (classIds.Count == 0 || rules.Count == 0)
        {
            return new CobraRuleCompatibilityPlan(
                new ReadOnlyDictionary<int, IReadOnlyList<Rule>>(new Dictionary<int, IReadOnlyList<Rule>>()),
            CobraRuleCompatibilitySource.CpuHeuristic);
        }

        var classMasks = classIds.Select(id => BuildClassMask(graph.GetClass(id))).ToArray();
        var classHeadArityKeys = classIds.Select(id => BuildClassHeadArityKeys(graph.GetClass(id))).ToArray();
        var ruleMasks = rules.Select(BuildRuleMask).ToArray();
        var ruleHeads = rules.Select(GetRuleHead).ToArray();
        var ruleArities = rules.Select(GetRuleArity).ToArray();
        var wildcardFlags = rules.Select(rule => rule.Pattern is Wild ? 1 : 0).ToArray();

        if (CobraCudaNative.TryScoreRuleCompatibility(classMasks, ruleMasks, out var scores))
        {
            return BuildPlan(
                classIds,
                rules,
                classHeadArityKeys,
                ruleHeads,
                ruleArities,
                wildcardFlags,
                scores,
                CobraRuleCompatibilitySource.Cuda);
        }

        var cpuScores = new int[classIds.Count * rules.Count];
        for (int classIndex = 0; classIndex < classIds.Count; classIndex++)
        {
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                cpuScores[(classIndex * rules.Count) + ruleIndex] =
                    IsCompatible(classMasks[classIndex], ruleMasks[ruleIndex]) &&
                    HasCompatibleTopLevelHeadAndArity(classHeadArityKeys[classIndex], ruleHeads[ruleIndex], ruleArities[ruleIndex], wildcardFlags[ruleIndex])
                        ? 1
                        : 0;
            }
        }

        return BuildPlan(
            classIds,
            rules,
            classHeadArityKeys,
            ruleHeads,
            ruleArities,
            wildcardFlags,
            cpuScores,
            CobraRuleCompatibilitySource.CpuHeuristic);
    }

    public static CobraRuleCompatibilityPlan Slice(CobraRuleCompatibilityPlan plan, IReadOnlyList<int> classIds)
    {
        if (classIds.Count == 0 || plan.RulesByClassId.Count == 0)
        {
            return new CobraRuleCompatibilityPlan(
                new ReadOnlyDictionary<int, IReadOnlyList<Rule>>(new Dictionary<int, IReadOnlyList<Rule>>()),
                plan.Source);
        }

        var slicedMap = new Dictionary<int, IReadOnlyList<Rule>>(classIds.Count);
        foreach (int classId in classIds)
        {
            if (plan.RulesByClassId.TryGetValue(classId, out var rules))
            {
                slicedMap[classId] = rules;
            }
        }

        return new CobraRuleCompatibilityPlan(
            new ReadOnlyDictionary<int, IReadOnlyList<Rule>>(slicedMap),
            plan.Source);
    }

    private static CobraRuleCompatibilityPlan BuildPlan(
        IReadOnlyList<int> classIds,
        IReadOnlyList<Rule> rules,
        IReadOnlyList<HashSet<string>> classHeadArityKeys,
        IReadOnlyList<string?> ruleHeads,
        IReadOnlyList<int> ruleArities,
        IReadOnlyList<int> wildcardFlags,
        int[] scores,
        CobraRuleCompatibilitySource source)
    {
        var compatibilityCounts = new int[rules.Count];
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            int count = 0;
            for (int classIndex = 0; classIndex < classIds.Count; classIndex++)
            {
                if (scores[(classIndex * rules.Count) + ruleIndex] > 0 &&
                    HasCompatibleTopLevelHeadAndArity(classHeadArityKeys[classIndex], ruleHeads[ruleIndex], ruleArities[ruleIndex], wildcardFlags[ruleIndex]))
                {
                    count++;
                }
            }

            compatibilityCounts[ruleIndex] = count;
        }

        int[] ruleScores = ScoreRuleOrder(compatibilityCounts, ruleArities.ToArray(), wildcardFlags.ToArray());

        var map = new Dictionary<int, IReadOnlyList<Rule>>(classIds.Count);
        for (int classIndex = 0; classIndex < classIds.Count; classIndex++)
        {
            var compatibleRules = new List<(Rule Rule, int Count, int Score)>();
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
            {
                if (scores[(classIndex * rules.Count) + ruleIndex] > 0 &&
                    HasCompatibleTopLevelHeadAndArity(classHeadArityKeys[classIndex], ruleHeads[ruleIndex], ruleArities[ruleIndex], wildcardFlags[ruleIndex]))
                {
                    compatibleRules.Add((rules[ruleIndex], compatibilityCounts[ruleIndex], ruleScores[ruleIndex]));
                }
            }

            map[classIds[classIndex]] = compatibleRules
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Count)
                .ThenBy(item => item.Rule.Pattern.ToDisplayString(), StringComparer.Ordinal)
                .Select(item => item.Rule)
                .ToArray();
        }

        return new CobraRuleCompatibilityPlan(
            new ReadOnlyDictionary<int, IReadOnlyList<Rule>>(map),
            source);
    }

    private static bool IsCompatible(int classMask, int ruleMask)
    {
        return ruleMask == 0 || (classMask & ruleMask) != 0;
    }

    private static bool HasCompatibleTopLevelHeadAndArity(
        IReadOnlySet<string> classHeadArityKeys,
        string? ruleHead,
        int ruleArity,
        int wildcardFlag)
    {
        return wildcardFlag == 1 || (ruleHead is not null && classHeadArityKeys.Contains(CreateHeadArityKey(ruleHead, ruleArity)));
    }

    private static int[] ScoreRuleOrder(int[] compatibilityCounts, int[] arities, int[] wildcardFlags)
    {
        if (CobraCudaNative.TryScoreRuleOrder(compatibilityCounts, arities, wildcardFlags, out var scores))
        {
            return scores;
        }

        return compatibilityCounts
            .Zip(arities, static (count, arity) => new { count, arity })
            .Zip(wildcardFlags, static (left, wildcard) => (left.count == 0 ? 0 : 1000 / left.count) + (left.arity * 16) - (wildcard * 64))
            .ToArray();
    }

    private static int BuildClassMask(EClass eClass)
    {
        int mask = 0;
        foreach (var node in eClass.Nodes)
        {
            mask |= EncodeHeadMask(node.Head);
        }

        return mask;
    }

    private static HashSet<string> BuildClassHeadArityKeys(EClass eClass)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in eClass.Nodes)
        {
            keys.Add(CreateHeadArityKey(node.Head, node.Children.Count));
        }

        return keys;
    }

    private static int BuildRuleMask(Rule rule)
    {
        if (rule.Pattern is Wild)
        {
            return 0;
        }

        return EncodeHeadMask(ENode.GetHead(rule.Pattern));
    }

    private static int GetRuleArity(Rule rule)
    {
        return rule.Pattern switch
        {
            Operation op => op.Arguments.Count,
            _ => 0
        };
    }

    private static string? GetRuleHead(Rule rule)
    {
        return rule.Pattern is Wild ? null : ENode.GetHead(rule.Pattern);
    }

    private static string CreateHeadArityKey(string head, int arity)
    {
        return $"{head}|{arity}";
    }

    private static int EncodeHeadMask(string head)
    {
        return head switch
        {
            "Add" or "TensorAdd" => 1 << 0,
            "Mul" or "TensorMul" => 1 << 1,
            "MatMul" or "FusedMatMulAdd" or "FusedMatMulAddRelu" => 1 << 2,
            "Transpose" => 1 << 3,
            "Relu" => 1 << 4,
            "Equality" => 1 << 5,
            "Vector" => 1 << 6,
            var value when value.StartsWith("Sym:", StringComparison.Ordinal) => 1 << 7,
            var value when value.StartsWith("Num:", StringComparison.Ordinal) => 1 << 8,
            _ => OtherBit
        };
    }
}
