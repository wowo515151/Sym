using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;

namespace SymCobra.Core;

public static class CobraShapeAnalysis
{
    public static void Analyze(CobraGraphState graphState, System.Threading.CancellationToken ct = default)
    {
        Analyze(graphState, prioritizedClassIds: null, ct);
    }

    public static void Analyze(CobraGraphState graphState, IReadOnlyList<int>? prioritizedClassIds, System.Threading.CancellationToken ct = default)
    {
        bool changed = true;
        int maxIterations = 100;
        int iteration = 0;

        while (changed && iteration < maxIterations)
        {
            changed = false;
            iteration++;
            ct.ThrowIfCancellationRequested();

            var rootIds = BuildClassOrder(graphState, prioritizedClassIds);
            foreach (int classId in rootIds)
            {
                var cobraClass = graphState.GetClass(classId);
                Shape? existingShape = cobraClass.Metadata.TryGetValue("shape", out object? shapeObject) &&
                                       shapeObject is Shape typedShape &&
                                       typedShape.IsValid
                    ? typedShape
                    : null;
                Shape? newShape = existingShape;

                foreach (int nodeId in cobraClass.NodeIds)
                {
                    var calculated = ComputeShape(graphState, graphState.GetNode(nodeId));
                    if (calculated.IsValid && !calculated.IsWildcardShape)
                    {
                        if (newShape == null || (newShape.IsScalar && !calculated.IsScalar))
                        {
                            newShape = calculated;
                        }
                        else if (!newShape.Equals(calculated) && newShape.IsValid && !calculated.IsScalar)
                        {
                            newShape = Shape.Error;
                            break;
                        }
                    }
                }

                if (newShape != null && !newShape.Equals(existingShape) && graphState.UpdateClassShape(classId, newShape))
                {
                    changed = true;
                }
            }
        }
    }

    private static IReadOnlyList<int> BuildClassOrder(CobraGraphState graphState, IReadOnlyList<int>? prioritizedClassIds)
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

        var ordered = new List<int>(roots.Count);
        var seen = new HashSet<int>();
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

    private static Shape ComputeShape(CobraGraphState graphState, CobraNodeRecord node)
    {
        if (node.Head.StartsWith("Num:", StringComparison.Ordinal)) return Shape.Scalar;
        if (node.Head.StartsWith("Sym:", StringComparison.Ordinal)) return Shape.Wildcard;
        if (node.Head.StartsWith("Wild:", StringComparison.Ordinal)) return Shape.Wildcard;

        var childExprs = new List<IExpression>(node.CanonicalChildIds.Length);
        foreach (int childId in node.CanonicalChildIds)
        {
            var childClass = graphState.GetClass(childId);
            if (childClass.Metadata.TryGetValue("shape", out object? childShapeObject) &&
                childShapeObject is Shape childShape &&
                childShape.IsValid)
            {
                childExprs.Add(new Symbol("dummy", childShape));
            }
            else
            {
                childExprs.Add(new Symbol("dummy", Shape.Wildcard));
            }
        }

        try
        {
            var expr = ExpressionFactory.Create(node.Head, childExprs.ToImmutableList());
            return expr.Shape;
        }
        catch
        {
            return Shape.Error;
        }
    }
}
