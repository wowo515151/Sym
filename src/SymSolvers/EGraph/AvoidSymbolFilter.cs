using Sym.Core.EGraph;

namespace SymSolvers.EGraphSolver
{
    public class AvoidSymbolFilter : IExtractionFilter
    {
        private readonly string? _headToAvoid;

        public AvoidSymbolFilter(string? headToAvoid)
        {
            _headToAvoid = headToAvoid;
        }

        public bool Accept(ENode node)
        {
            return _headToAvoid == null || node.Head != _headToAvoid;
        }
    }
}
