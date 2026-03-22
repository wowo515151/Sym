//Copyright Warren Harding 2025.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sym.Atoms;
using Sym.Operations;

namespace Sym.Core.EGraph
{
    public struct Match
    {
        public Rule Rule { get; }
        public int RootClassId { get; }
        public ImmutableDictionary<string, int> Bindings { get; }

        public Match(Rule rule, int rootClassId, ImmutableDictionary<string, int> bindings)
        {
            Rule = rule;
            RootClassId = rootClassId;
            Bindings = bindings;
        }
    }

    public class MatchHistory
    {
        private readonly ConcurrentDictionary<(Rule, int), int> _checkedClasses = new();
        private readonly ConcurrentDictionary<(Rule, ENode), int> _checkedNodes = new();

        public bool IsClassUpToDate(Rule rule, int classId, int currentGeneration) => 
            _checkedClasses.TryGetValue((rule, classId), out int lastGen) && lastGen >= currentGeneration;
        
        public void MarkClassChecked(Rule rule, int classId, int currentGeneration) => 
            _checkedClasses[(rule, classId)] = currentGeneration;

        public bool IsNodeChecked(Rule rule, ENode node, int currentGeneration) => 
            _checkedNodes.TryGetValue((rule, node), out int lastGen) && lastGen >= currentGeneration;
        
        public void MarkNodeChecked(Rule rule, ENode node, int currentGeneration) => 
            _checkedNodes[(rule, node)] = currentGeneration;

        public void Clear()
        {
            _checkedClasses.Clear();
            _checkedNodes.Clear();
        }
    }

    public static class EGraphMatcher
    {
        public static List<Match> FindMatches(EGraph graph, IEnumerable<Rule> rules, MatchHistory? history = null, int maxConcurrency = 8, CancellationToken ct = default)
        {
            return FindMatches(graph, rules, classIds: null, rulesByClass: null, eligibleNodesByClass: null, history, maxConcurrency, ct);
        }

        public static List<Match> FindMatches(EGraph graph, IEnumerable<Rule> rules, IReadOnlyList<int>? classIds, MatchHistory? history = null, int maxConcurrency = 8, CancellationToken ct = default)
        {
            return FindMatches(graph, rules, classIds, rulesByClass: null, eligibleNodesByClass: null, history, maxConcurrency, ct);
        }

        public static List<Match> FindMatches(EGraph graph, IEnumerable<Rule> rules, IReadOnlyList<int>? classIds, IReadOnlyDictionary<int, IReadOnlyList<Rule>>? rulesByClass, MatchHistory? history = null, int maxConcurrency = 8, CancellationToken ct = default)
        {
            return FindMatches(graph, rules, classIds, rulesByClass, eligibleNodesByClass: null, history, maxConcurrency, ct);
        }

        public static List<Match> FindMatches(EGraph graph, IEnumerable<Rule> rules, IReadOnlyList<int>? classIds, IReadOnlyDictionary<int, IReadOnlyList<Rule>>? rulesByClass, IReadOnlyDictionary<int, IReadOnlyDictionary<Rule, HashSet<ENode>>>? eligibleNodesByClass, MatchHistory? history = null, int maxConcurrency = 8, CancellationToken ct = default)
        {
            var matches = new ConcurrentBag<Match>();
            var ruleList = rules.ToList();
            var targetClassIds = classIds?.ToList() ?? Enumerable.Range(0, graph.ClassCount).ToList();

            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = ct };

            Parallel.ForEach(targetClassIds, options, rootId =>
            {
                var eClass = graph.GetClass(rootId);
                if (eClass.Parent != rootId) return; // Skip non-canonical classes

                int currentGen = eClass.Generation;
                var classRuleList = rulesByClass != null && rulesByClass.TryGetValue(rootId, out var plannedRules)
                    ? plannedRules
                    : ruleList;

                foreach (var rule in classRuleList)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    
                    if (rule.Pattern is Wild wild)
                    {
                        if (history != null && history.IsClassUpToDate(rule, rootId, currentGen)) continue;

                        if (CheckConstraint(eClass, wild.Constraint))
                        {
                            var bindings = ImmutableDictionary.CreateBuilder<string, int>();
                            bindings.Add(wild.Name, rootId);
                            matches.Add(new Match(rule, rootId, bindings.ToImmutable()));
                        }
                        
                        history?.MarkClassChecked(rule, rootId, currentGen);
                    }
                    else
                    {
                        var candidateNodes = eligibleNodesByClass != null &&
                                             eligibleNodesByClass.TryGetValue(rootId, out var ruleNodeMap) &&
                                             ruleNodeMap.TryGetValue(rule, out var eligibleNodes)
                            ? eClass.Nodes.Where(node => eligibleNodes.Contains(node))
                            : eClass.Nodes;

                        foreach (var node in candidateNodes)
                        {
                            if (history != null && history.IsNodeChecked(rule, node, currentGen)) continue;

                            var bindings = ImmutableDictionary.CreateBuilder<string, int>();
                            if (TryMatch(graph, node, rule.Pattern, bindings, options.CancellationToken))
                            {
                                matches.Add(new Match(rule, rootId, bindings.ToImmutable()));
                            }
                            
                            history?.MarkNodeChecked(rule, node, currentGen);
                        }
                    }
                }
            });

            return matches.OrderBy(m => m.Rule.Name ?? string.Empty).ThenBy(m => m.RootClassId).ToList();
        }

        private static bool CheckConstraint(EClass eClass, WildConstraint constraint)
        {
            if (constraint == WildConstraint.None) return true;
            
            // Use Shape metadata if available
            if (eClass.Data is Shape shape && shape.IsValid)
            {
                switch (constraint)
                {
                    case WildConstraint.Scalar: if (shape.IsScalar) return true; break;
                    case WildConstraint.Vector: if (shape.IsVector) return true; break;
                    case WildConstraint.Matrix: if (shape.IsMatrix) return true; break;
                    case WildConstraint.Tensor: if (shape.IsTensor || shape.IsMatrix || shape.IsVector) return true; break;
                    // Constant constraint checks for explicit Number nodes, which Shape doesn't guarantee alone
                }
            }

            return eClass.Nodes.Any(node => SatisfiesConstraint(node, constraint));
        }

        private static bool SatisfiesConstraint(ENode node, WildConstraint constraint)
        {
             if (constraint == WildConstraint.None) return true;
             
             bool isNumber = node.Head.StartsWith("Num:", StringComparison.Ordinal);
             bool isMatrix = node.Head == "Matrix" || node.Head == "MatMul" || node.Head == "FusedMatMulAdd" || node.Head == "FusedMatMulAddRelu";
             bool isVector = node.Head == "Vector";
             // Conv2D produces rank 4 tensor usually.
             bool isTensorOp = node.Head == "Conv2D" || node.Head == "FusedConv2DRelu" || node.Head == "TensorAdd" || node.Head == "TensorMul" || node.Head == "Transpose" || node.Head == "Relu";
             bool isTensor = isMatrix || isVector || isTensorOp;

             return constraint switch
             {
                 WildConstraint.Scalar => !isTensor, // Conservative approximation
                 WildConstraint.Constant => isNumber,
                 WildConstraint.Vector => isVector,
                 WildConstraint.Matrix => isMatrix,
                 WildConstraint.Tensor => isTensor,
                 _ => true
             };
        }

        private static bool TryMatch(EGraph graph, ENode node, IExpression pattern, ImmutableDictionary<string, int>.Builder bindings, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            
            if (pattern is Atom atom) 
            {
                bool match = node.Head == ENode.GetHead(atom);
                // if (!match && Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                //    Console.WriteLine($"DEBUG: Atom mismatch: node.Head={node.Head} vs pattern.Head={ENode.GetHead(atom)}");
                return match;
            }

            if (pattern is Operation op)
            {
                string opHead = ENode.GetHead(op);
                if (node.Head != opHead)
                {
                    return false;
                }
                if (node.Children.Count != op.Arguments.Count)
                {
                    // if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                    //    Console.WriteLine($"DEBUG: Op child count mismatch: node.Children.Count={node.Children.Count} vs pattern.Arguments.Count={op.Arguments.Count}");
                    return false;
                }

                var checkpoint = bindings.ToImmutable();
                for (int i = 0; i < op.Arguments.Count; i++)
                {
                    if (!MatchClass(graph, node.Children[i], op.Arguments[i], bindings, ct))
                    {
                        // if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                        //    Console.WriteLine($"DEBUG: Op child {i} mismatch");
                        bindings.Clear();
                        foreach(var kvp in checkpoint) bindings.Add(kvp.Key, kvp.Value);
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        private static bool TryDirectFlatOperationMatch(EGraph graph, ENode node, IExpression pattern, ImmutableDictionary<string, int>.Builder bindings)
        {
            if (pattern is not Operation op)
            {
                return false;
            }

            if (node.Head != ENode.GetHead(op) || node.Children.Count != op.Arguments.Count)
            {
                return false;
            }

            var checkpoint = bindings.ToImmutable();
            for (int i = 0; i < op.Arguments.Count; i++)
            {
                if (op.Arguments[i] is Wild wild)
                {
                    int childClassId = graph.Find(node.Children[i]);
                    if (!CheckConstraint(graph.GetClass(childClassId), wild.Constraint))
                    {
                        bindings.Clear();
                        foreach (var kvp in checkpoint) bindings.Add(kvp.Key, kvp.Value);
                        return false;
                    }

                    if (bindings.TryGetValue(wild.Name, out int existing))
                    {
                        if (existing != childClassId)
                        {
                            bindings.Clear();
                            foreach (var kvp in checkpoint) bindings.Add(kvp.Key, kvp.Value);
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
                    int childClassId = graph.Find(node.Children[i]);
                    var childClass = graph.GetClass(childClassId);
                    if (!childClass.Nodes.Any(candidate => candidate.Head == ENode.GetHead(atom)))
                    {
                        bindings.Clear();
                        foreach (var kvp in checkpoint) bindings.Add(kvp.Key, kvp.Value);
                        return false;
                    }
                }
                else
                {
                    bindings.Clear();
                    foreach (var kvp in checkpoint) bindings.Add(kvp.Key, kvp.Value);
                    return false;
                }
            }

            return true;
        }

        private static bool CanUseDirectFlatOperationMatch(IExpression pattern)
        {
            if (pattern is not Operation op)
            {
                return false;
            }

            return op.Arguments.All(arg => arg is Wild || arg is Atom);
        }

        private static bool MatchClass(EGraph graph, int classId, IExpression pattern, ImmutableDictionary<string, int>.Builder bindings, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            classId = graph.Find(classId); 

            if (pattern is Wild wild)
            {
                if (!CheckConstraint(graph.GetClass(classId), wild.Constraint))
                {
                    if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                        Console.WriteLine($"DEBUG: Wild constraint mismatch for {wild.Name}");
                    return false;
                }
                if (bindings.TryGetValue(wild.Name, out int existing)) return existing == classId;
                bindings.Add(wild.Name, classId);
                return true;
            }

            var eClass = graph.GetClass(classId);
            var checkpoint = bindings.ToImmutable();

            // if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            //    Console.WriteLine($"DEBUG: Class {classId} has nodes: {string.Join(", ", eClass.Nodes.Select(n => n.Head))}");

            foreach (var node in eClass.Nodes)
            {
                if (CanUseDirectFlatOperationMatch(pattern))
                {
                    if (TryDirectFlatOperationMatch(graph, node, pattern, bindings)) return true;
                }
                else if (TryMatch(graph, node, pattern, bindings, ct))
                {
                    return true;
                }
                bindings.Clear();
                foreach(var kvp in checkpoint) bindings.Add(kvp.Key, kvp.Value);
            }
            // if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            //    Console.WriteLine($"DEBUG: Class {classId} has no nodes matching pattern {pattern.ToDisplayString()}");
            return false;
        }
    }
}
