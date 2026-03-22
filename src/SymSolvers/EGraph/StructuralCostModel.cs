using System;
using Sym.Core.EGraph;
using Sym.Core;

namespace SymSolvers.EGraphSolver
{
    public class StructuralCostModel : ICostModel
    {
        public Func<ENode, long> GetCostFunction(SolveContext context, Sym.Core.EGraph.EGraph graph)
        {
            return node =>
            {
                if (node.Head.StartsWith("Num:")) return 1;
                if (node.Head.StartsWith("Sym:")) return 10;
                if (node.Head == "Derivative") return 1000;
                return 100;
            };
        }
    }
}
