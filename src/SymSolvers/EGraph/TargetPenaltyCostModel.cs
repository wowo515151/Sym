using System;
using Sym.Core.EGraph;
using Sym.Core;

namespace SymSolvers.EGraphSolver
{
    public class TargetPenaltyCostModel : ICostModel
    {
        private readonly ICostModel _inner;
        private readonly int _targetClassId;

        public TargetPenaltyCostModel(ICostModel inner, int targetClassId)
        {
            _inner = inner;
            _targetClassId = targetClassId;
        }

        public Func<ENode, long> GetCostFunction(SolveContext context, Sym.Core.EGraph.EGraph graph)
        {
            var innerFunc = _inner.GetCostFunction(context, graph);
            return node =>
            {
                long cost = innerFunc(node);
                if (_targetClassId != -1)
                {
                    foreach (var childId in node.Children)
                    {
                        if (graph.Find(childId) == _targetClassId)
                        {
                            return 1000000000L; // Heavily penalize expressions containing the target variable
                        }
                    }
                }
                return cost;
            };
        }
    }
}
