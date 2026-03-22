// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Operations;

namespace Sym.Core.EGraph
{
    public static class EGraphExtract
    {
        private struct Cost
        {
            public long Value;
            public ENode BestNode;
        }

        /// <summary>
        /// Extracts the "best" expression from the E-Graph for a given Class ID.
        /// </summary>
        public static IExpression ExtractBest(EGraph graph, int classId, Func<ENode, bool>? filter = null, Func<ENode, long>? costFunction = null, CancellationToken ct = default)
        {
            var costs = new Dictionary<int, Cost>();
            CalculateCosts(graph, costs, filter, costFunction, prioritizedClassIds: null, preferredNodesByClass: null, ct);

            return Reconstruct(graph, classId, costs);
        }

        public static IExpression ExtractBest(
            EGraph graph,
            int classId,
            Func<ENode, bool>? filter,
            Func<ENode, long>? costFunction,
            IReadOnlyList<int>? prioritizedClassIds,
            IReadOnlyDictionary<int, IReadOnlyList<ENode>>? preferredNodesByClass,
            CancellationToken ct = default)
        {
            var costs = new Dictionary<int, Cost>();
            CalculateCosts(graph, costs, filter, costFunction, prioritizedClassIds, preferredNodesByClass, ct);

            return Reconstruct(graph, classId, costs);
        }

        /// <summary>
        /// Extracts the best expression found so far even if the soft cancellation token fires.
        /// Hard cancellation still aborts immediately.
        /// </summary>
        public static IExpression ExtractBestEffort(
            EGraph graph,
            int classId,
            Func<ENode, bool>? filter = null,
            Func<ENode, long>? costFunction = null,
            CancellationToken softCt = default,
            CancellationToken hardCt = default)
        {
            return ExtractBestEffort(
                graph,
                classId,
                filter,
                costFunction,
                prioritizedClassIds: null,
                preferredNodesByClass: null,
                softCt,
                hardCt);
        }

        public static IExpression ExtractBestEffort(
            EGraph graph,
            int classId,
            Func<ENode, bool>? filter,
            Func<ENode, long>? costFunction,
            IReadOnlyList<int>? prioritizedClassIds = null,
            IReadOnlyDictionary<int, IReadOnlyList<ENode>>? preferredNodesByClass = null,
            CancellationToken softCt = default,
            CancellationToken hardCt = default)
        {
            var costs = new Dictionary<int, Cost>();
            try
            {
                CalculateCosts(graph, costs, filter, costFunction, prioritizedClassIds, preferredNodesByClass, softCt);
            }
            catch (OperationCanceledException) when (!hardCt.IsCancellationRequested)
            {
                // Return the best expression discovered so far when only the soft timeout fired.
            }

            hardCt.ThrowIfCancellationRequested();
            return Reconstruct(graph, classId, costs);
        }

        public static IExpression? ExtractExact(EGraph graph, int rootId, IExpression target, CancellationToken ct = default)
        {
            var memo = new Dictionary<(int, IExpression), bool>();
            if (Contains(graph, rootId, target, memo, ct))
            {
                return target;
            }
            return null;
        }

        private static bool Contains(EGraph graph, int classId, IExpression target, Dictionary<(int, IExpression), bool> memo, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            classId = graph.Find(classId);
            
            if (memo.TryGetValue((classId, target), out bool res)) return res;

            var eClass = graph.GetClass(classId);
            bool found = false;

            if (target is Atom atom)
            {
                 string head = ENode.GetHead(atom);
                 if (eClass.Nodes.Any(n => n.Head == head && n.Children.Count == 0)) found = true;
            }
            else if (target is Operation op)
            {
                 string head = ENode.GetHead(op);
                 foreach(var node in eClass.Nodes)
                 {
                      if (node.Head == head && node.Children.Count == op.Arguments.Count)
                      {
                           bool childrenMatch = true;
                           for(int i=0; i<node.Children.Count; i++)
                           {
                                if (!Contains(graph, node.Children[i], op.Arguments[i], memo, ct))
                                {
                                     childrenMatch = false; 
                                     break;
                                }
                           }
                           if (childrenMatch) { found = true; break; }
                      }
                 }
            }
            
            memo[(classId, target)] = found;
            return found;
        }

        private static long GetBaseCost(ENode node)
        {
            if (node.Head.StartsWith("Num:")) return 1;
            if (node.Head.StartsWith("Sym:")) return 10; 
            if (node.Head == "Derivative") return 1000; 
            return 100; 
        }

        private static void CalculateCosts(
            EGraph graph,
            Dictionary<int, Cost> costs,
            Func<ENode, bool>? filter,
            Func<ENode, long>? costFunction,
            IReadOnlyList<int>? prioritizedClassIds,
            IReadOnlyDictionary<int, IReadOnlyList<ENode>>? preferredNodesByClass,
            CancellationToken ct = default)
        {
            bool changed = true;
            int maxIterations = graph.ClassCount * 2; 
            int iter = 0;
            var orderedRootIds = BuildOrderedRootIds(graph, prioritizedClassIds);

            while (changed && iter < maxIterations)
            {
                ct.ThrowIfCancellationRequested();
                changed = false;
                iter++;

                foreach (int id in orderedRootIds)
                {
                    ct.ThrowIfCancellationRequested();
                    var eClass = graph.GetClass(id);
                    Cost bestInClass = new Cost { Value = long.MaxValue, BestNode = default };

                    IEnumerable<ENode> candidateNodes = preferredNodesByClass != null &&
                                                         preferredNodesByClass.TryGetValue(id, out var preferredNodes) &&
                                                         preferredNodes.Count > 0
                        ? preferredNodes
                        : eClass.Nodes;

                    foreach (var node in candidateNodes)
                    {
                        ct.ThrowIfCancellationRequested();
                        
                        if (filter != null && !filter(node)) continue;

                        long nodeCost = costFunction?.Invoke(node) ?? GetBaseCost(node);
                        bool possible = true;

                        foreach (var childId in node.Children)
                        {
                            int childRoot = graph.Find(childId);
                            if (costs.TryGetValue(childRoot, out Cost childCost))
                            {
                                if (childCost.Value == long.MaxValue) { possible = false; break; }
                                nodeCost += childCost.Value;
                            }
                            else
                            {
                                possible = false;
                                break;
                            }
                        }

                        if (possible)
                        {
                            if (nodeCost < bestInClass.Value)
                            {
                                bestInClass = new Cost { Value = nodeCost, BestNode = node };
                            }
                        }
                    }

                    if (bestInClass.Value != long.MaxValue)
                    {
                        if (!costs.TryGetValue(id, out Cost current) || bestInClass.Value < current.Value)
                        {
                            costs[id] = bestInClass;
                            changed = true;
                        }
                    }
                }
            }
        }

        private static IReadOnlyList<int> BuildOrderedRootIds(EGraph graph, IReadOnlyList<int>? prioritizedClassIds)
        {
            if (prioritizedClassIds == null || prioritizedClassIds.Count == 0)
            {
                return graph.GetRootIds();
            }

            var roots = graph.GetRootIds();
            var seen = new HashSet<int>();
            var ordered = new List<int>(roots.Count);

            foreach (var classId in prioritizedClassIds)
            {
                int rootId = graph.Find(classId);
                if (seen.Add(rootId))
                {
                    ordered.Add(rootId);
                }
            }

            foreach (var rootId in roots)
            {
                if (seen.Add(rootId))
                {
                    ordered.Add(rootId);
                }
            }

            return ordered;
        }

        private static IExpression Reconstruct(EGraph graph, int id, Dictionary<int, Cost> costs)
        {
            return Reconstruct(graph, id, costs, new HashSet<int>());
        }

        private static IExpression Reconstruct(EGraph graph, int id, Dictionary<int, Cost> costs, HashSet<int> visited)
        {
            id = graph.Find(id);
            if (visited.Contains(id))
            {
                var eClass = graph.GetClass(id);
                var simplest = eClass.Nodes.OrderBy(n => n.Children.Count).FirstOrDefault();
                if (simplest.Head == null) throw new Exception("Empty E-Class encountered during extraction.");
                if (simplest.Children.Count == 0) return ENode.CreateExpression(simplest.Head, ImmutableList<IExpression>.Empty);
                throw new Exception($"Cycle detected in E-Graph extraction for Class {id}");
            }

            visited.Add(id);
            try
            {
                if (!costs.TryGetValue(id, out Cost cost))
                {
                    var eClass = graph.GetClass(id);
                    if (eClass.Nodes.Count > 0)
                    {
                        var fallbackNode = eClass.Nodes.First();
                        return NodeToExpression(graph, fallbackNode, costs, visited, id);
                    }
                    throw new Exception($"Could not extract expression for Class {id}");
                }

                return NodeToExpression(graph, cost.BestNode, costs, visited, id);
            }
            finally
            {
                visited.Remove(id);
            }
        }

        private static IExpression NodeToExpression(EGraph graph, ENode node, Dictionary<int, Cost> costs, HashSet<int> visited, int? classId = null)
        {
            if (classId.HasValue && node.Head.StartsWith("Sym:"))
            {
                var eClass = graph.GetClass(classId.Value);
                Shape? shape = eClass.Data as Shape;
                
                // if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                //    Console.WriteLine($"DEBUG: NodeToExpression for {node.Head} in class {classId.Value} has shape {shape?.ToDisplayString() ?? "null"}");

                if (shape != null && shape.IsValid)
                {
                    return new Symbol(node.Head.Substring(4), shape);
                }
            }

            var children = node.Children.Select(c => Reconstruct(graph, c, costs, visited)).ToImmutableList();
            return ENode.CreateExpression(node.Head, children);
        }
    }
}
