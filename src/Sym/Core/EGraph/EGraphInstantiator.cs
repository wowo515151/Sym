//Copyright Warren Harding 2025.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Operations;
using SymCore;

namespace Sym.Core.EGraph
{
    public static class EGraphInstantiator
    {
        /// <summary>
        /// Instantiates a pattern/replacement expression into the E-Graph using the provided bindings.
        /// </summary>
        /// <param name="graph">The E-Graph to add nodes to.</param>
        /// <param name="template">The expression template (e.g., rule replacement).</param>
        /// <param name="bindings">The mapping from Wildcard names to Class IDs.</param>
        /// <returns>The Class ID of the instantiated expression.</returns>
        public static int Instantiate(EGraph graph, IExpression template, ImmutableDictionary<string, int> bindings)
        {
            if (template is Wild wild)
            {
                if (bindings.TryGetValue(wild.Name, out int classId))
                {
                    return classId;
                }
                // Some rule applications may produce partial bindings (e.g. during heuristics or higher-level pipelines).
                // If a wildcard is unbound, treat it as a symbolic variable rather than failing the entire saturation.
                return graph.Add(new Symbol(wild.Name));
            }

            if (template is Atom atom)
            {
                if (atom is Symbol s && bindings.TryGetValue(s.Name, out int classId))
                {
                    return classId;
                }
                return graph.Add(atom);
            }

            if (template is Operation op)
            {
                var childrenIds = ImmutableList.CreateBuilder<int>();
                foreach (var arg in op.Arguments)
                {
                    childrenIds.Add(Instantiate(graph, arg, bindings));
                }
                
                string head = ENode.GetHead(op);
                return graph.AddNode(new ENode(head, childrenIds.ToImmutable()));
            }

            throw new NotSupportedException($"Cannot instantiate expression of type {template.GetType().Name}");
        }
    }
}
