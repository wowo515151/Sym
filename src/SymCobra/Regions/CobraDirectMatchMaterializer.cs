// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraDirectMatchMaterializer
{
    internal static bool CanBuild(EGraph graph, CobraDirectMatchPair pair)
    {
        return TryBuildBindings(new MaterializationContext(graph), pair, out _);
    }

    public static IReadOnlyList<Match> Materialize(EGraph graph, CobraDirectMatchPlan plan)
    {
        return MaterializeForExecution(graph, plan).ToMatches();
    }

    public static CobraDirectMatchExecutionResult MaterializeForExecution(EGraph graph, CobraDirectMatchPlan plan)
    {
        var context = new MaterializationContext(graph);
        var orderedPairs = OrderPairs(context, plan);
        var matches = new List<Match>();
        var simpleApplications = new List<CobraDirectRuleApplication>();
        foreach (var pair in orderedPairs)
        {
            if (TryBuildBindings(context, pair, out var bindings))
            {
                RuleShapeConditions.TryEvaluate(
                    pair.Rule.Condition,
                    bindingName => context.TryGetShape(bindings, bindingName, out var shape) ? shape : null,
                    out bool hasStructuredShapeCondition,
                    out bool structuredShapeConditionResult);
                if (hasStructuredShapeCondition && !structuredShapeConditionResult)
                {
                    continue;
                }

                RuleBindingConditions.TryEvaluate(
                    pair.Rule.Condition,
                    bindingName => context.TryGetLiteral(bindings, bindingName, out var literal) ? literal : null,
                    out bool hasStructuredBindingCondition,
                    out bool structuredBindingConditionResult);
                if (hasStructuredBindingCondition && !structuredBindingConditionResult)
                {
                    continue;
                }

                RuleTransforms.TryEvaluate(
                    pair.Rule.Transform,
                    bindingName => context.TryGetLiteral(bindings, bindingName, out var literal) ? literal : null,
                    out bool hasStructuredTransform,
                    out IExpression? structuredTransformResult);
                if (hasStructuredTransform && structuredTransformResult is null)
                {
                    continue;
                }

                if (IsSimpleRule(pair.Rule, hasStructuredTransform))
                {
                    simpleApplications.Add(new CobraDirectRuleApplication(pair.ClassId, pair.Rule, bindings, structuredTransformResult));
                }
                else
                {
                    matches.Add(new Match(pair.Rule, pair.ClassId, bindings.ToImmutableDictionary(StringComparer.Ordinal)));
                }
            }
        }

        return new CobraDirectMatchExecutionResult(matches, simpleApplications);
    }

    private static IReadOnlyList<CobraDirectMatchPair> OrderPairs(MaterializationContext context, CobraDirectMatchPlan plan)
    {
        var pairs = plan.PairsByClass
            .SelectMany(classEntry => classEntry.Value.Values.SelectMany(static pairs => pairs))
            .ToArray();
        if (pairs.Length <= 1)
        {
            return pairs;
        }

        var classIds = pairs.Select(pair => pair.ClassId).ToArray();
        var nodeArities = pairs.Select(pair => pair.Node.Children.Count).ToArray();
        var nestedFlags = pairs.Select(pair => pair.Rule.Pattern is Operation op && op.Arguments.Any(static arg => arg is Operation) ? 1 : 0).ToArray();
        var allNodeCounts = context.AllNodeCounts;
        var allGenerations = context.AllGenerations;

        if (CobraCudaNative.TryScoreDirectPairsV2ById(classIds, nodeArities, nestedFlags, allNodeCounts, allGenerations, out var scores) ||
            CobraCudaNative.TryScoreDirectPairsV2(
                classIds,
                nodeArities,
                classIds.Select(id => allGenerations[id]).ToArray(),
                classIds.Select(id => allNodeCounts[id]).ToArray(),
                nestedFlags,
                out scores))
        {
            return pairs
                .Zip(scores, static (pair, score) => new { pair, score })
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.pair.ClassId)
                .ThenBy(item => item.pair.Rule.Pattern.ToDisplayString(), StringComparer.Ordinal)
                .Select(item => item.pair)
                .ToArray();
        }

        if (CobraCudaNative.TryScoreDirectPairs(
            classIds,
            nodeArities,
            classIds.Select(id => allGenerations[id]).ToArray(),
            classIds.Select(id => allNodeCounts[id]).ToArray(),
            out scores))
        {
            return pairs
                .Zip(scores, static (pair, score) => new { pair, score })
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.pair.ClassId)
                .ThenBy(item => item.pair.Rule.Pattern.ToDisplayString(), StringComparer.Ordinal)
                .Select(item => item.pair)
                .ToArray();
        }

        return pairs
            .OrderByDescending(pair => allGenerations[pair.ClassId])
            .ThenByDescending(pair => allNodeCounts[pair.ClassId])
            .ThenBy(pair => pair.ClassId)
            .ToArray();
    }

    private static bool TryBuildBindings(MaterializationContext context, CobraDirectMatchPair pair, out Dictionary<string, int> bindings)
    {
        bindings = new Dictionary<string, int>(StringComparer.Ordinal);
        if (pair.Rule.Pattern is Wild rootWild)
        {
            int classId = context.FindClassId(pair.ClassId);
            return context.SatisfiesConstraint(classId, rootWild.Constraint) &&
                   TryBindWild(bindings, new List<string>(), rootWild.Name, classId);
        }

        if (pair.Rule.Pattern is Atom rootAtom)
        {
            return pair.Node.Head == ENode.GetHead(rootAtom) && pair.Node.Children.Count == 0;
        }

        if (pair.Rule.Pattern is not Operation op)
        {
            return false;
        }

        if (pair.Node.Head != ENode.GetHead(op) || pair.Node.Children.Count != op.Arguments.Count)
        {
            return false;
        }

        var bindingOrder = new List<string>();
        for (int i = 0; i < op.Arguments.Count; i++)
        {
            if (op.Arguments[i] is Wild wild)
            {
                int childClassId = context.FindClassId(pair.Node.Children[i]);
                if (!context.SatisfiesConstraint(childClassId, wild.Constraint))
                {
                    return false;
                }

                if (!TryBindWild(bindings, bindingOrder, wild.Name, childClassId))
                {
                    return false;
                }
            }
            else if (op.Arguments[i] is Atom atom)
            {
                int childClassId = context.FindClassId(pair.Node.Children[i]);
                if (!context.HasHead(childClassId, ENode.GetHead(atom)))
                {
                    return false;
                }
            }
            else if (op.Arguments[i] is Operation childOp)
            {
                int childClassId = context.FindClassId(pair.Node.Children[i]);
                if (!TryBindNestedChildOperation(context, childClassId, childOp, bindings, bindingOrder, remainingDepth: CobraNodeMatchEncoding.MaxDirectNestedOperationDepth))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSimpleRule(Rule rule, bool structuredTransformHandled)
    {
        return rule.Transform is null || structuredTransformHandled;
    }

    private static bool TryBindNestedChildOperation(
        MaterializationContext context,
        int classId,
        Operation operation,
        Dictionary<string, int> bindings,
        List<string> bindingOrder,
        int remainingDepth)
    {
        var eClass = context.GetClass(classId);

        foreach (var node in eClass.Nodes)
        {
            if (node.Head != ENode.GetHead(operation) || node.Children.Count != operation.Arguments.Count)
            {
                continue;
            }

            int checkpoint = bindingOrder.Count;
            if (TryBindOperationArguments(context, node, operation, bindings, bindingOrder, remainingDepth))
            {
                return true;
            }

            RollBackBindings(bindings, bindingOrder, checkpoint);
        }

        return false;
    }

    private static bool TryBindOperationArguments(
        MaterializationContext context,
        ENode node,
        Operation operation,
        Dictionary<string, int> bindings,
        List<string> bindingOrder,
        int remainingDepth)
    {
        int checkpoint = bindingOrder.Count;

        for (int i = 0; i < operation.Arguments.Count; i++)
        {
            if (operation.Arguments[i] is Wild wild)
            {
                int childClassId = context.FindClassId(node.Children[i]);
                if (!context.SatisfiesConstraint(childClassId, wild.Constraint))
                {
                    RollBackBindings(bindings, bindingOrder, checkpoint);
                    return false;
                }

                if (!TryBindWild(bindings, bindingOrder, wild.Name, childClassId))
                {
                    RollBackBindings(bindings, bindingOrder, checkpoint);
                    return false;
                }
            }
            else if (operation.Arguments[i] is Atom atom)
            {
                int childClassId = context.FindClassId(node.Children[i]);
                if (!context.HasHead(childClassId, ENode.GetHead(atom)))
                {
                    RollBackBindings(bindings, bindingOrder, checkpoint);
                    return false;
                }
            }
            else if (operation.Arguments[i] is Operation childOp && remainingDepth > 0)
            {
                int childClassId = context.FindClassId(node.Children[i]);
                if (!TryBindNestedChildOperation(context, childClassId, childOp, bindings, bindingOrder, remainingDepth - 1))
                {
                    RollBackBindings(bindings, bindingOrder, checkpoint);
                    return false;
                }
            }
            else
            {
                RollBackBindings(bindings, bindingOrder, checkpoint);
                return false;
            }
        }

        return true;
    }

    private static bool TryBindWild(Dictionary<string, int> bindings, List<string> bindingOrder, string name, int classId)
    {
        if (bindings.TryGetValue(name, out int existing))
        {
            return existing == classId;
        }

        bindings.Add(name, classId);
        bindingOrder.Add(name);
        return true;
    }

    private static void RollBackBindings(Dictionary<string, int> bindings, List<string> bindingOrder, int checkpoint)
    {
        for (int i = bindingOrder.Count - 1; i >= checkpoint; i--)
        {
            bindings.Remove(bindingOrder[i]);
        }

        if (bindingOrder.Count > checkpoint)
        {
            bindingOrder.RemoveRange(checkpoint, bindingOrder.Count - checkpoint);
        }
    }

    private sealed class MaterializationContext
    {
        private readonly EGraph _graph;
        private readonly Dictionary<int, int> _findCache = new();
        private readonly HashSet<string>?[] _headSets;
        private readonly int[] _constraintMasks;

        public MaterializationContext(EGraph graph)
        {
            _graph = graph;
            AllNodeCounts = new int[graph.ClassCount];
            AllGenerations = new int[graph.ClassCount];
            _headSets = new HashSet<string>[graph.ClassCount];
            _constraintMasks = new int[graph.ClassCount];

            for (int classId = 0; classId < graph.ClassCount; classId++)
            {
                var eClass = graph.GetClass(classId);
                AllNodeCounts[classId] = eClass.Nodes.Count;
                AllGenerations[classId] = eClass.Generation;
                _constraintMasks[classId] = ComputeConstraintMask(eClass);
            }
        }

        public int[] AllNodeCounts { get; }

        public int[] AllGenerations { get; }

        public EClass GetClass(int classId) => _graph.GetClass(classId);

        public int FindClassId(int rawClassId)
        {
            if (_findCache.TryGetValue(rawClassId, out int canonicalId))
            {
                return canonicalId;
            }

            canonicalId = _graph.Find(rawClassId);
            _findCache[rawClassId] = canonicalId;
            return canonicalId;
        }

        public bool HasHead(int classId, string head)
        {
            var headSet = _headSets[classId];
            if (headSet is null)
            {
                headSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in _graph.GetClass(classId).Nodes)
                {
                    headSet.Add(node.Head);
                }

                _headSets[classId] = headSet;
            }

            return headSet.Contains(head);
        }

        public bool SatisfiesConstraint(int classId, WildConstraint constraint)
        {
            if (constraint == WildConstraint.None)
            {
                return true;
            }

            int mask = _constraintMasks[classId];
            return constraint switch
            {
                WildConstraint.Scalar => (mask & ConstraintScalarMask) != 0,
                WildConstraint.Constant => (mask & ConstraintConstantMask) != 0,
                WildConstraint.Vector => (mask & ConstraintVectorMask) != 0,
                WildConstraint.Matrix => (mask & ConstraintMatrixMask) != 0,
                WildConstraint.Tensor => (mask & ConstraintTensorMask) != 0,
                _ => true
            };
        }

        public bool TryGetShape(IReadOnlyDictionary<string, int> bindings, string bindingName, out Shape shape)
        {
            shape = Shape.Error;
            if (!bindings.TryGetValue(bindingName, out int classId))
            {
                return false;
            }

            classId = FindClassId(classId);
            if (_graph.GetClass(classId).Data is not Shape classShape ||
                !classShape.IsValid ||
                classShape.IsWildcardShape)
            {
                return false;
            }

            shape = classShape;
            return true;
        }

        public bool TryGetLiteral(IReadOnlyDictionary<string, int> bindings, string bindingName, out IExpression literal)
        {
            literal = null!;
            if (!bindings.TryGetValue(bindingName, out int classId))
            {
                return false;
            }

            classId = FindClassId(classId);
            foreach (var node in _graph.GetClass(classId).Nodes)
            {
                if (node.Children.Count != 0)
                {
                    continue;
                }

                if (TryParseLiteral(node.Head, out literal))
                {
                    return true;
                }
            }

            return false;
        }

        private const int ConstraintScalarMask = 1 << 0;
        private const int ConstraintConstantMask = 1 << 1;
        private const int ConstraintVectorMask = 1 << 2;
        private const int ConstraintMatrixMask = 1 << 3;
        private const int ConstraintTensorMask = 1 << 4;

        private static bool TryParseLiteral(string head, out IExpression literal)
        {
            if (head.StartsWith("Num:", StringComparison.Ordinal) &&
                decimal.TryParse(head.AsSpan(4), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal numericValue))
            {
                literal = new Number(numericValue);
                return true;
            }

            if (head.StartsWith("Sym:", StringComparison.Ordinal))
            {
                literal = new Symbol(head.Substring(4));
                return true;
            }

            literal = null!;
            return false;
        }

        private static int ComputeConstraintMask(EClass eClass)
        {
            int mask = 0;
            if (eClass.Data is Shape shape && shape.IsValid)
            {
                if (shape.IsScalar) mask |= ConstraintScalarMask;
                if (shape.IsVector) mask |= ConstraintVectorMask | ConstraintTensorMask;
                if (shape.IsMatrix) mask |= ConstraintMatrixMask | ConstraintTensorMask;
                if (shape.IsTensor) mask |= ConstraintTensorMask;
            }

            foreach (var node in eClass.Nodes)
            {
                bool isNumber = node.Head.StartsWith("Num:", StringComparison.Ordinal);
                bool isMatrix = node.Head == "Matrix" || node.Head == "MatMul" || node.Head == "FusedMatMulAdd" || node.Head == "FusedMatMulAddRelu";
                bool isVector = node.Head == "Vector";
                bool isTensorOp = node.Head == "Conv2D" || node.Head == "FusedConv2DRelu" || node.Head == "TensorAdd" || node.Head == "TensorMul" || node.Head == "Transpose" || node.Head == "Relu";
                bool isTensor = isMatrix || isVector || isTensorOp;

                if (isNumber) mask |= ConstraintConstantMask | ConstraintScalarMask;
                if (!isTensor) mask |= ConstraintScalarMask;
                if (isVector) mask |= ConstraintVectorMask | ConstraintTensorMask;
                if (isMatrix) mask |= ConstraintMatrixMask | ConstraintTensorMask;
                if (isTensorOp) mask |= ConstraintTensorMask;
            }

            return mask;
        }
    }
}
