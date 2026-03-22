// Copyright Warren Harding 2026
using System;
using Sym.Core.EGraph;
using Sym.Core;

namespace SymSolvers.EGraphSolver
{
    public interface ICostModel
    {
        Func<ENode, long> GetCostFunction(SolveContext context, Sym.Core.EGraph.EGraph graph);
    }
}
