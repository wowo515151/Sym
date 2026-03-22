// Copyright Warren Harding 2026
using System;
using Sym.Core.EGraph;
using Sym.Core;

namespace SymSolvers.EGraphSolver
{
    public class FactoredCostModel : ICostModel
    {
        public Func<ENode, long> GetCostFunction(SolveContext context, Sym.Core.EGraph.EGraph graph)
        {
            return node => {
                if (node.Head == "Add" || node.Head == "Subtract") return 10L;
                if (node.Head == "Mul" || node.Head == "Pow") return 1L;
                return 1L;
            };
        }
    }
}
