//Copyright Warren Harding 2025.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;

namespace Sym.Core.EGraph
{
    public static class ShapeAnalysis
    {
        public static void Analyze(EGraph graph, System.Threading.CancellationToken ct = default)
        {
            Analyze(graph, prioritizedClassIds: null, ct);
        }

        public static void Analyze(EGraph graph, IReadOnlyList<int>? prioritizedClassIds, System.Threading.CancellationToken ct = default)
        {
            bool changed = true;
            int maxIterations = 100; // Prevent infinite loops in pathological cases
            int iteration = 0;

            while (changed && iteration < maxIterations)
            {
                changed = false;
                iteration++;
                ct.ThrowIfCancellationRequested();

                // Get all root IDs to iterate over valid classes
                var rootIds = BuildClassOrder(graph, prioritizedClassIds);

                foreach (var classId in rootIds)
                {
                    var eClass = graph.GetClass(classId);
                    
                    // If we already have a shape, we might still check for consistency, 
                    // but for now let's assume monotonicity (once valid, stays valid) 
                    // and only compute if missing or Error?
                    // Spec says: "If a contradiction ... invalid/Error".
                    
                    Shape? existingShape = eClass.Data as Shape;
                    Shape? newShape = (existingShape != null && existingShape.IsValid) ? existingShape : null;

                    foreach (var node in eClass.Nodes)
                    {
                        var calculated = ComputeShape(graph, node);
                        if (calculated.IsValid && !calculated.IsWildcardShape)
                        {
                            if (newShape == null || (newShape.IsScalar && !calculated.IsScalar))
                            {
                                newShape = calculated;
                            }
                            else if (!newShape.Equals(calculated) && newShape.IsValid && !calculated.IsScalar)
                            {
                                // Contradiction found between two different VALID non-scalar shapes
                                newShape = Shape.Error;
                                break;
                            }
                        }
                    }

                    // If we found a valid shape, or we want to persist Error
                    if (newShape != null && !newShape.Equals(existingShape))
                    {
                        eClass.Data = newShape;
                        eClass.Generation++; 
                        changed = true;
                    }
                }
            }
        }

        private static IReadOnlyList<int> BuildClassOrder(EGraph graph, IReadOnlyList<int>? prioritizedClassIds)
        {
            var rootIds = graph.GetRootIds();
            if (prioritizedClassIds == null || prioritizedClassIds.Count == 0)
            {
                return rootIds;
            }

            var ordered = new List<int>(rootIds.Count);
            var seen = new HashSet<int>();
            foreach (var classId in prioritizedClassIds)
            {
                int rootId = graph.Find(classId);
                if (seen.Add(rootId))
                {
                    ordered.Add(rootId);
                }
            }

            foreach (var classId in rootIds)
            {
                if (seen.Add(classId))
                {
                    ordered.Add(classId);
                }
            }

            return ordered;
        }

        private static Shape ComputeShape(EGraph graph, ENode node)
        {
            // For Symbols/Numbers/Wildcards, the shape should have been set during Add().
            // But if it wasn't (e.g. Num), we can infer it here.
            
            if (node.Head.StartsWith("Num:", StringComparison.Ordinal)) return Shape.Scalar;
            if (node.Head.StartsWith("Sym:", StringComparison.Ordinal)) 
            {
                // Symbol shapes are intrinsic, usually set in Add. 
                // If not set in EClass.Data yet, we can't infer it from the Name string.
                return Shape.Wildcard; 
            }
            if (node.Head.StartsWith("Wild:", StringComparison.Ordinal)) return Shape.Wildcard;

            // Resolve children shapes
            var childExprs = new List<IExpression>();
            foreach (var childId in node.Children)
            {
                var childClass = graph.GetClass(childId);
                if (childClass.Data is Shape s && s.IsValid)
                {
                    childExprs.Add(new Symbol("dummy", s));
                }
                else
                {
                    // If any child shape is unknown, we can't compute the op shape accurately
                    // unless the op handles wildcards.
                    childExprs.Add(new Symbol("dummy", Shape.Wildcard));
                }
            }

            try
            {
                // Instantiate the operation to use its Shape logic
                var expr = ExpressionFactory.Create(node.Head, childExprs.ToImmutableList());
                return expr.Shape;
            }
            catch
            {
                return Shape.Error;
            }
        }
    }
}
