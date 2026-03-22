// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;

namespace SymRules.Graph
{
    public static class GraphRules
    {
        public static ImmutableList<Rule> Rules { get; }

        static GraphRules()
        {
            Wild _a = new Wild("a");
            Wild _b = new Wild("b");
            Wild _c = new Wild("c");
            Wild _e1 = new Wild("e1");
            Wild _e2 = new Wild("e2");

            Rules = ImmutableList.Create<Rule>(
                // Re-parenting: Edge(a, b), Edge(b, c) -> Edge(a, c)
                // Allows bypassing intermediate nodes to reduce latency.
                new Rule(new Vector(new Edge(_a, _b), new Edge(_b, _c)), new Vector(new Edge(_a, _b), new Edge(_a, _c))),
                
                // Redundancy: Edge(a, a) -> Nil
                new Rule(new Edge(_a, _a), new Symbol("nil"))
            );
        }
    }
}
