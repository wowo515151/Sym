using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;

namespace SymCobra.Core;

public static class CobraGraphMatcher
{
    public static List<Match> FindMatches(
        CobraGraphState graphState,
        IEnumerable<Rule> rules,
        IReadOnlyList<int>? classIds,
        IReadOnlyDictionary<int, IReadOnlyList<Rule>>? rulesByClass,
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>? eligibleNodesByClass,
        MatchHistory? history = null,
        CancellationToken ct = default)
    {
        var matches = new List<Match>();
        var visitedRoots = new HashSet<int>();
        var targetClassIds = classIds ?? Enumerable.Range(0, graphState.ClassCount);
        IReadOnlyList<Rule>? fallbackRuleList = null;
        var eNodeCache = new Dictionary<int, ENode>();

        foreach (int rawRootId in targetClassIds)
        {
            ct.ThrowIfCancellationRequested();
            int rootId = graphState.Find(rawRootId);
            if (!visitedRoots.Add(rootId))
            {
                continue;
            }

            var eClass = graphState.GetClass(rootId);
            int currentGen = Math.Max(1, graphState.SyncState.Epoch);
            var classRuleList = rulesByClass != null && rulesByClass.TryGetValue(rootId, out var plannedRules)
                ? plannedRules
                : (fallbackRuleList ??= rules as IReadOnlyList<Rule> ?? rules.ToList());

            foreach (var rule in classRuleList)
            {
                ct.ThrowIfCancellationRequested();

                if (rule.Pattern is Wild wild)
                {
                    if (history != null && history.IsClassUpToDate(rule, rootId, currentGen))
                    {
                        continue;
                    }

                    if (CheckConstraint(graphState, eClass, wild.Constraint))
                    {
                        var bindings = ImmutableDictionary.CreateBuilder<string, int>();
                        bindings.Add(wild.Name, rootId);
                        matches.Add(new Match(rule, rootId, bindings.ToImmutable()));
                    }

                    history?.MarkClassChecked(rule, rootId, currentGen);
                    continue;
                }

                foreach (int nodeId in eClass.NodeIds)
                {
                    CobraNodeRecord node = graphState.GetNode(nodeId);
                    ENode eNode = GetOrCreateENode(nodeId, node, eNodeCache);
                    if (!IsEligibleNode(rootId, rule, eNode, eligibleNodesByClass))
                    {
                        continue;
                    }

                    if (history != null && history.IsNodeChecked(rule, eNode, currentGen))
                    {
                        continue;
                    }

                    var bindings = ImmutableDictionary.CreateBuilder<string, int>();
                    if (TryMatch(graphState, node, rule.Pattern, bindings, ct))
                    {
                        matches.Add(new Match(rule, rootId, bindings.ToImmutable()));
                    }

                    history?.MarkNodeChecked(rule, eNode, currentGen);
                }
            }
        }

        return matches.OrderBy(static m => m.Rule.Name ?? string.Empty).ThenBy(static m => m.RootClassId).ToList();
    }

    private static bool IsEligibleNode(
        int rootId,
        Rule rule,
        ENode eNode,
        IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>? eligibleNodesByClass)
    {
        if (eligibleNodesByClass == null ||
            !eligibleNodesByClass.TryGetValue(rootId, out var ruleNodeMap) ||
            !ruleNodeMap.TryGetValue(rule, out var eligibleNodes))
        {
            return true;
        }

        return eligibleNodes.Contains(eNode);
    }

    private static bool CheckConstraint(CobraGraphState graphState, CobraClassRecord eClass, WildConstraint constraint)
    {
        if (constraint == WildConstraint.None)
        {
            return true;
        }

        if (eClass.Metadata.TryGetValue("shape", out var metadataShape) &&
            metadataShape is Shape shape &&
            shape.IsValid)
        {
            switch (constraint)
            {
                case WildConstraint.Scalar when shape.IsScalar:
                case WildConstraint.Vector when shape.IsVector:
                case WildConstraint.Matrix when shape.IsMatrix:
                case WildConstraint.Tensor when shape.IsTensor || shape.IsMatrix || shape.IsVector:
                    return true;
            }
        }

        return eClass.NodeIds.Any(nodeId => SatisfiesConstraint(graphState.GetNode(nodeId), constraint));
    }

    private static bool SatisfiesConstraint(CobraNodeRecord node, WildConstraint constraint)
    {
        if (constraint == WildConstraint.None)
        {
            return true;
        }

        bool isNumber = node.Head.StartsWith("Num:", StringComparison.Ordinal) || (node.Literal?.StartsWith("Num:", StringComparison.Ordinal) ?? false);
        bool isMatrix = node.Head == "Matrix" || node.Head == "MatMul" || node.Head == "FusedMatMulAdd" || node.Head == "FusedMatMulAddRelu";
        bool isVector = node.Head == "Vector";
        bool isTensorOp = node.Head == "Conv2D" || node.Head == "FusedConv2DRelu" || node.Head == "TensorAdd" || node.Head == "TensorMul" || node.Head == "Transpose" || node.Head == "Relu";
        bool isTensor = isMatrix || isVector || isTensorOp;

        return constraint switch
        {
            WildConstraint.Scalar => !isTensor,
            WildConstraint.Constant => isNumber,
            WildConstraint.Vector => isVector,
            WildConstraint.Matrix => isMatrix,
            WildConstraint.Tensor => isTensor,
            _ => true
        };
    }

    private static bool TryMatch(
        CobraGraphState graphState,
        CobraNodeRecord node,
        IExpression pattern,
        ImmutableDictionary<string, int>.Builder bindings,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (pattern is Atom atom)
        {
            return node.Head == ENode.GetHead(atom);
        }

        if (pattern is not Operation op)
        {
            return false;
        }

        string opHead = ENode.GetHead(op);
        if (node.Head != opHead || node.CanonicalChildIds.Length != op.Arguments.Count)
        {
            return false;
        }

        var checkpoint = bindings.ToImmutable();
        for (int i = 0; i < op.Arguments.Count; i++)
        {
            if (!MatchClass(graphState, node.CanonicalChildIds[i], op.Arguments[i], bindings, ct))
            {
                bindings.Clear();
                foreach (var kvp in checkpoint)
                {
                    bindings.Add(kvp.Key, kvp.Value);
                }

                return false;
            }
        }

        return true;
    }

    private static bool TryDirectFlatOperationMatch(
        CobraGraphState graphState,
        CobraNodeRecord node,
        IExpression pattern,
        ImmutableDictionary<string, int>.Builder bindings)
    {
        if (pattern is not Operation op)
        {
            return false;
        }

        if (node.Head != ENode.GetHead(op) || node.CanonicalChildIds.Length != op.Arguments.Count)
        {
            return false;
        }

        var checkpoint = bindings.ToImmutable();
        for (int i = 0; i < op.Arguments.Count; i++)
        {
            if (op.Arguments[i] is Wild wild)
            {
                int childClassId = graphState.Find(node.CanonicalChildIds[i]);
                if (!CheckConstraint(graphState, graphState.GetClass(childClassId), wild.Constraint))
                {
                    RestoreBindings(bindings, checkpoint);
                    return false;
                }

                if (bindings.TryGetValue(wild.Name, out int existing))
                {
                    if (existing != childClassId)
                    {
                        RestoreBindings(bindings, checkpoint);
                        return false;
                    }
                }
                else
                {
                    bindings.Add(wild.Name, childClassId);
                }
            }
            else if (op.Arguments[i] is Atom atom)
            {
                int childClassId = graphState.Find(node.CanonicalChildIds[i]);
                var childClass = graphState.GetClass(childClassId);
                if (!childClass.NodeIds.Select(graphState.GetNode).Any(candidate => candidate.Head == ENode.GetHead(atom)))
                {
                    RestoreBindings(bindings, checkpoint);
                    return false;
                }
            }
            else
            {
                RestoreBindings(bindings, checkpoint);
                return false;
            }
        }

        return true;
    }

    private static bool CanUseDirectFlatOperationMatch(IExpression pattern)
    {
        return pattern is Operation op && op.Arguments.All(static arg => arg is Wild || arg is Atom);
    }

    private static bool MatchClass(
        CobraGraphState graphState,
        int classId,
        IExpression pattern,
        ImmutableDictionary<string, int>.Builder bindings,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        classId = graphState.Find(classId);

        if (pattern is Wild wild)
        {
            if (!CheckConstraint(graphState, graphState.GetClass(classId), wild.Constraint))
            {
                return false;
            }

            if (bindings.TryGetValue(wild.Name, out int existing))
            {
                return existing == classId;
            }

            bindings.Add(wild.Name, classId);
            return true;
        }

        var eClass = graphState.GetClass(classId);
        var checkpoint = bindings.ToImmutable();
        foreach (int nodeId in eClass.NodeIds)
        {
            CobraNodeRecord node = graphState.GetNode(nodeId);
            if (CanUseDirectFlatOperationMatch(pattern))
            {
                if (TryDirectFlatOperationMatch(graphState, node, pattern, bindings))
                {
                    return true;
                }
            }
            else if (TryMatch(graphState, node, pattern, bindings, ct))
            {
                return true;
            }

            RestoreBindings(bindings, checkpoint);
        }

        return false;
    }

    private static void RestoreBindings(
        ImmutableDictionary<string, int>.Builder bindings,
        ImmutableDictionary<string, int> checkpoint)
    {
        bindings.Clear();
        foreach (var kvp in checkpoint)
        {
            bindings.Add(kvp.Key, kvp.Value);
        }
    }

    private static ENode ToENode(CobraNodeRecord node)
    {
        return new ENode(node.Head, ImmutableList.CreateRange(node.CanonicalChildIds));
    }

    private static ENode GetOrCreateENode(int nodeId, CobraNodeRecord node, Dictionary<int, ENode> eNodeCache)
    {
        if (eNodeCache.TryGetValue(nodeId, out var eNode))
        {
            return eNode;
        }

        eNode = ToENode(node);
        eNodeCache[nodeId] = eNode;
        return eNode;
    }
}
