using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymRules;

namespace SymSolvers
{
    public static class CalculusHelper
    {
        public static IExpression DifferentiateExpression(IExpression expr, Symbol variable)
        {
            var derivative = new Derivative(expr, variable).Canonicalize();
            var packs = RulePackLibrary.GetRulePacks();
            var diffPack = packs.FirstOrDefault(p => p.Name == "DifferentiationStrategy");
            var algPack = packs.FirstOrDefault(p => p.Name == "AlgebraicStrategy");
            var basicPack = packs.FirstOrDefault(p => p.Name == "Rules");
            
            var allRules = ImmutableList<Sym.Core.Rule>.Empty;
            if (diffPack != null) allRules = allRules.AddRange(diffPack.Rules);
            if (algPack != null) allRules = allRules.AddRange(algPack.Rules);
            if (basicPack != null) allRules = allRules.AddRange(basicPack.Rules);

            // If no rules are available, return the (possibly partially simplified) derivative.
            if (allRules.IsEmpty) return derivative;

            var context = new SolveContext(null, allRules, maxIterations: 30);
            var strategy = new EGraphSolverStrategy();
            
            var result = strategy.Solve(derivative, context);
            
            if (result.IsSuccess && result.ResultExpression != null)
            {
                var simplified = result.ResultExpression.Canonicalize();
                if (simplified is Equality eq)
                {
                    simplified = eq.RightOperand;
                }
                if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                    System.Console.WriteLine($"DEBUG: CalculusHelper differentiated {expr.ToDisplayString()} -> {simplified.ToDisplayString()}");
                return simplified;
            }
            
            return derivative;
        }
    }
}
