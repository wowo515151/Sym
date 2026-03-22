using Sym.Core.EGraph;

namespace SymSolvers.EGraphSolver
{
    public interface IExtractionFilter
    {
        bool Accept(ENode node);
    }
}
