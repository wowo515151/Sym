// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;

namespace SymCobra.Core;

public static class CobraGraphExtractor
{
    private struct Cost
    {
        public long Value;
        public int BestNodeId;
    }

    public static IExpression ExtractBest(
        CobraGraphState graphState,
        int classId,
        Func<ENode, bool>? filter = null,
        Func<ENode, long>? costFunction = null,
        IReadOnlyList<int>? prioritizedClassIds = null,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? preferredNodesByClass = null,
        CancellationToken ct = default)
    {
        var costs = new Dictionary<int, Cost>();
        CalculateCosts(graphState, costs, filter, costFunction, prioritizedClassIds, preferredNodesByClass, ct);
        return Reconstruct(graphState, classId, costs);
    }

    public static IExpression ExtractBestEffort(
        CobraGraphState graphState,
        int classId,
        Func<ENode, bool>? filter,
        Func<ENode, long>? costFunction,
        IReadOnlyList<int>? prioritizedClassIds = null,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? preferredNodesByClass = null,
        CancellationToken softCt = default,
        CancellationToken hardCt = default)
    {
        var costs = new Dictionary<int, Cost>();
        try
        {
            CalculateCosts(graphState, costs, filter, costFunction, prioritizedClassIds, preferredNodesByClass, softCt);
        }
        catch (OperationCanceledException) when (!hardCt.IsCancellationRequested)
        {
            // Return the best expression discovered so far when only the soft timeout fired.
        }

        hardCt.ThrowIfCancellationRequested();
        return Reconstruct(graphState, classId, costs);
    }

    private static void CalculateCosts(
        CobraGraphState graphState,
        Dictionary<int, Cost> costs,
        Func<ENode, bool>? filter,
        Func<ENode, long>? costFunction,
        IReadOnlyList<int>? prioritizedClassIds,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? preferredNodesByClass,
        CancellationToken ct)
    {
        IReadOnlyList<int> orderedRootIds = BuildOrderedRootIds(graphState, prioritizedClassIds);

        // Stage 1: Heuristic (Fast initial bounds)
        int heuristicIterations = Math.Min(3, graphState.ClassCount);
        RunCostIterations(graphState, costs, filter, costFunction, orderedRootIds, preferredNodesByClass, heuristicIterations, ct);

        // Stage 2: Pruning (Identify viable nodes)
        var lowerBounds = new Dictionary<int, long>();
        foreach (int id in orderedRootIds)
        {
            long minBaseCost = long.MaxValue;
            var eClass = graphState.GetClass(id);
            foreach (int nodeId in eClass.NodeIds)
            {
                var node = graphState.GetNode(nodeId);
                var legacyNode = ToENode(node);
                if (filter != null && !filter(legacyNode)) continue;
                long baseCost = costFunction?.Invoke(legacyNode) ?? GetBaseCost(legacyNode);
                if (baseCost < minBaseCost) minBaseCost = baseCost;
            }
            lowerBounds[id] = minBaseCost == long.MaxValue ? 0 : minBaseCost;
        }

        var propagatedLowerBounds = new Dictionary<int, long>(lowerBounds);
        foreach (int id in orderedRootIds)
        {
            long minBound = long.MaxValue;
            var eClass = graphState.GetClass(id);
            foreach (int nodeId in eClass.NodeIds)
            {
                var node = graphState.GetNode(nodeId);
                var legacyNode = ToENode(node);
                if (filter != null && !filter(legacyNode)) continue;
                long baseCost = costFunction?.Invoke(legacyNode) ?? GetBaseCost(legacyNode);
                long bound = baseCost;
                foreach (int childId in node.CanonicalChildIds)
                {
                    int childRoot = graphState.Find(childId);
                    bound += lowerBounds.TryGetValue(childRoot, out long cb) ? cb : 0;
                }
                if (bound < minBound) minBound = bound;
            }
            if (minBound != long.MaxValue) propagatedLowerBounds[id] = minBound;
        }

        var prunedNodesByClass = new Dictionary<int, IReadOnlyList<int>>();
        foreach (int id in orderedRootIds)
        {
            ct.ThrowIfCancellationRequested();
            var kept = new List<int>();
            long classUpperBound = costs.TryGetValue(id, out Cost c) ? c.Value : long.MaxValue;

            foreach (int nodeId in EnumerateCandidateNodeIds(graphState, id, preferredNodesByClass))
            {
                var node = graphState.GetNode(nodeId);
                var legacyNode = ToENode(node);
                if (filter != null && !filter(legacyNode)) continue;

                if (classUpperBound != long.MaxValue)
                {
                    long baseCost = costFunction?.Invoke(legacyNode) ?? GetBaseCost(legacyNode);
                    long nodeLowerBound = baseCost;
                    foreach (int childId in node.CanonicalChildIds)
                    {
                        int childRoot = graphState.Find(childId);
                        nodeLowerBound += propagatedLowerBounds.TryGetValue(childRoot, out long cb) ? cb : 0;
                    }

                    if (nodeLowerBound > classUpperBound)
                    {
                        // Pruned: Best possible cost through this node is strictly worse than known upper bound.
                        continue;
                    }
                }
                kept.Add(nodeId);
            }
            prunedNodesByClass[id] = kept;
        }

        // Stage 3: Exact Refinement (Converge on reduced search space)
        int maxIterations = Math.Max(1, graphState.ClassCount * 2);
        RunCostIterations(graphState, costs, filter, costFunction, orderedRootIds, prunedNodesByClass, maxIterations, ct);
    }

    private static void RunCostIterations(
        CobraGraphState graphState,
        Dictionary<int, Cost> costs,
        Func<ENode, bool>? filter,
        Func<ENode, long>? costFunction,
        IReadOnlyList<int> orderedRootIds,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? candidateNodesByClass,
        int maxIterations,
        CancellationToken ct)
    {
        bool changed = true;
        int iteration = 0;
        
        var classCostChanged = new HashSet<int>(orderedRootIds);
        var nodeCostCache = new Dictionary<int, long>();
        var classChildDependencies = new Dictionary<int, HashSet<int>>();

        foreach (int classId in orderedRootIds)
        {
            classChildDependencies[classId] = new HashSet<int>();
            foreach (int nodeId in EnumerateCandidateNodeIds(graphState, classId, candidateNodesByClass))
            {
                var node = graphState.GetNode(nodeId);
                foreach (int childId in node.CanonicalChildIds)
                {
                    classChildDependencies[classId].Add(graphState.Find(childId));
                }
            }
        }

        while (changed && iteration < maxIterations)
        {
            ct.ThrowIfCancellationRequested();
            changed = false;
            iteration++;
            
            var nextClassCostChanged = new HashSet<int>();

            foreach (int id in orderedRootIds)
            {
                ct.ThrowIfCancellationRequested();
                
                // Skip if no children changed cost
                bool needsUpdate = iteration == 1;
                if (!needsUpdate)
                {
                    foreach (int dep in classChildDependencies[id])
                    {
                        if (classCostChanged.Contains(dep))
                        {
                            needsUpdate = true;
                            break;
                        }
                    }
                }
                
                if (!needsUpdate)
                {
                    continue;
                }

                var bestInClass = new Cost { Value = long.MaxValue, BestNodeId = -1 };

                foreach (int nodeId in EnumerateCandidateNodeIds(graphState, id, candidateNodesByClass))
                {
                    ct.ThrowIfCancellationRequested();

                    CobraNodeRecord node = graphState.GetNode(nodeId);
                    
                    long nodeCost;
                    if (!nodeCostCache.TryGetValue(nodeId, out nodeCost))
                    {
                        ENode legacyNode = ToENode(node);
                        if (filter != null && !filter(legacyNode))
                        {
                            nodeCostCache[nodeId] = long.MaxValue;
                            continue;
                        }
                        nodeCost = costFunction?.Invoke(legacyNode) ?? GetBaseCost(legacyNode);
                        nodeCostCache[nodeId] = nodeCost;
                    }
                    else if (nodeCost == long.MaxValue)
                    {
                        continue;
                    }

                    bool possible = true;
                    long currentCost = nodeCost;
                    foreach (int childId in node.CanonicalChildIds)
                    {
                        int childRoot = graphState.Find(childId);
                        if (!costs.TryGetValue(childRoot, out Cost childCost))
                        {
                            possible = false;
                            break;
                        }

                        if (childCost.Value == long.MaxValue)
                        {
                            possible = false;
                            break;
                        }

                        currentCost += childCost.Value;
                    }

                    if (possible && currentCost < bestInClass.Value)
                    {
                        bestInClass = new Cost { Value = currentCost, BestNodeId = nodeId };
                    }
                }

                if (bestInClass.Value != long.MaxValue &&
                    (!costs.TryGetValue(id, out Cost current) || bestInClass.Value < current.Value))
                {
                    costs[id] = bestInClass;
                    nextClassCostChanged.Add(id);
                    changed = true;
                }
            }
            
            classCostChanged = nextClassCostChanged;
        }
    }

    private static IReadOnlyList<int> BuildOrderedRootIds(CobraGraphState graphState, IReadOnlyList<int>? prioritizedClassIds)
    {
        var roots = new List<int>();
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            if (graphState.Find(classId) == classId && graphState.HasMaterializedNodes(classId))
            {
                roots.Add(classId);
            }
        }

        if (prioritizedClassIds == null || prioritizedClassIds.Count == 0)
        {
            return roots;
        }

        var seen = new HashSet<int>();
        var ordered = new List<int>(roots.Count);
        foreach (int classId in prioritizedClassIds)
        {
            int rootId = graphState.Find(classId);
            if (seen.Add(rootId))
            {
                ordered.Add(rootId);
            }
        }

        foreach (int rootId in roots)
        {
            if (seen.Add(rootId))
            {
                ordered.Add(rootId);
            }
        }

        return ordered;
    }

    private static IEnumerable<int> EnumerateCandidateNodeIds(
        CobraGraphState graphState,
        int classId,
        IReadOnlyDictionary<int, IReadOnlyList<int>>? preferredNodesByClass)
    {
        int rootId = graphState.Find(classId);
        var eClass = graphState.GetClass(rootId);
        if (preferredNodesByClass == null ||
            !preferredNodesByClass.TryGetValue(rootId, out IReadOnlyList<int>? preferredNodes) ||
            preferredNodes.Count == 0)
        {
            return eClass.NodeIds;
        }

        var availableNodeIds = eClass.NodeIds.Count <= 8
            ? null
            : eClass.NodeIds.ToHashSet();
        var ordered = new List<int>(eClass.NodeIds.Count);
        var used = new HashSet<int>();

        foreach (int preferredNodeId in preferredNodes)
        {
            bool isAvailable = availableNodeIds?.Contains(preferredNodeId) ?? eClass.NodeIds.Contains(preferredNodeId);
            if (used.Add(preferredNodeId) && isAvailable)
            {
                ordered.Add(preferredNodeId);
            }
        }

        foreach (int nodeId in eClass.NodeIds)
        {
            if (used.Add(nodeId))
            {
                ordered.Add(nodeId);
            }
        }

        return ordered;
    }

    private static IExpression Reconstruct(CobraGraphState graphState, int classId, Dictionary<int, Cost> costs)
    {
        return Reconstruct(graphState, classId, costs, new HashSet<int>());
    }

    private static IExpression Reconstruct(
        CobraGraphState graphState,
        int classId,
        Dictionary<int, Cost> costs,
        HashSet<int> visited)
    {
        int rootId = graphState.Find(classId);
        if (visited.Contains(rootId))
        {
            CobraNodeRecord simplest = graphState.GetClass(rootId)
                .NodeIds
                .Select(graphState.GetNode)
                .OrderBy(static node => node.Arity)
                .FirstOrDefault();

            if (simplest.Head == null)
            {
                throw new InvalidOperationException("Empty E-Class encountered during COBRA extraction.");
            }

            if (simplest.Arity == 0)
            {
                return NodeToExpression(graphState, simplest, costs, visited, rootId);
            }

            throw new InvalidOperationException($"Cycle detected in COBRA extraction for Class {rootId}");
        }

        visited.Add(rootId);
        try
        {
            if (!costs.TryGetValue(rootId, out Cost cost))
            {
                var eClass = graphState.GetClass(rootId);
                if (eClass.NodeIds.Count > 0)
                {
                    return NodeToExpression(graphState, graphState.GetNode(eClass.NodeIds[0]), costs, visited, rootId);
                }

                throw new InvalidOperationException($"Could not extract expression for COBRA Class {rootId}");
            }

            return NodeToExpression(graphState, graphState.GetNode(cost.BestNodeId), costs, visited, rootId);
        }
        finally
        {
            visited.Remove(rootId);
        }
    }

    private static IExpression NodeToExpression(
        CobraGraphState graphState,
        CobraNodeRecord node,
        Dictionary<int, Cost> costs,
        HashSet<int> visited,
        int classId)
    {
        if (node.Head.StartsWith("Sym:", StringComparison.Ordinal) &&
            graphState.GetClass(classId).Metadata.TryGetValue("shape", out object? shapeValue) &&
            shapeValue is Shape shape &&
            shape.IsValid)
        {
            return new Symbol(node.Head.Substring(4), shape);
        }

        var children = node.CanonicalChildIds
            .Select(childId => Reconstruct(graphState, childId, costs, visited))
            .ToImmutableList();

        return ENode.CreateExpression(node.Head, children);
    }

    private static ENode ToENode(CobraNodeRecord node)
    {
        return new ENode(node.Head, ImmutableList.CreateRange(node.CanonicalChildIds));
    }

    private static long GetBaseCost(ENode node)
    {
        if (node.Head.StartsWith("Num:", StringComparison.Ordinal))
        {
            return 1;
        }

        if (node.Head.StartsWith("Sym:", StringComparison.Ordinal))
        {
            return 10;
        }

        if (node.Head == "Derivative")
        {
            return 1000;
        }

        return 100;
    }
}
