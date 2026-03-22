using System;
using Sym.Core;
using Sym.Core.EGraph;

namespace SymSolvers.EGraphSolver
{
    public static class CostModelFactory
    {
        public static ICostModel GetCostModel(SolveContext context)
        {
            var costModelName = context.GetString(SolverOptionKeys.CostModel, "Default");
            var preferredForm = context.GetString("PreferredForm", string.Empty);
            var costExprStr = context.GetString("Cost", string.Empty);

            if (!string.IsNullOrEmpty(costExprStr) || string.Equals(costModelName, "InformationDensity", StringComparison.OrdinalIgnoreCase))
            {
                return new InformationDensityCostModel();
            }

            if (string.Equals(costModelName, "Tensor", StringComparison.OrdinalIgnoreCase))
            {
                return new TensorCostModel();
            }

            if (string.Equals(preferredForm, "Factored", StringComparison.OrdinalIgnoreCase) || string.Equals(costModelName, "Factored", StringComparison.OrdinalIgnoreCase))
            {
                return new FactoredCostModel();
            }

            if (string.Equals(preferredForm, "Expanded", StringComparison.OrdinalIgnoreCase) || string.Equals(costModelName, "Expanded", StringComparison.OrdinalIgnoreCase))
            {
                return new ExpandedCostModel();
            }

            return new StructuralCostModel();
        }
    }
}
