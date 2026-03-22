// Copyright Warren Harding 2026
using System;
using Sym.Core.EGraph;
using Sym.Core;

namespace SymSolvers.EGraphSolver
{
    public class ExpandedCostModel : ICostModel
    {
        public Func<ENode, long> GetCostFunction(SolveContext context, Sym.Core.EGraph.EGraph graph)
        {
            return node => {
                if (node.Head == "Pow") return 10L;
                if (node.Head == "Add") return 1L;
                if (node.Head == "Mul") return 5L;
                return 1L;
            };
        }
    }
}
