using System.Collections.Generic;
using System.Collections.Immutable;
using Sym.Core;
using Sym.Core.EGraph;
using SymRules;

namespace SymSolvers
{
    public class RulePackStrategy : ISolverStrategy, INamedSolverStrategy
    {
        public string Name { get; }
        public string Description { get; }
        private readonly ImmutableList<Sym.Core.Rule> _rules;
        private readonly EGraphSolverStrategy _solver;

        public RulePackStrategy(RulePack pack)
        {
            Name = pack.Name;
            Description = pack.Description;
            _rules = pack.Rules;
            _solver = new EGraphSolverStrategy();
        }

        public SolveResult Solve(IExpression? problem, SolveContext context)
        {
            if (problem is null) return SolveResult.Failure(null, "Problem cannot be null.");

            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                System.Console.WriteLine($"DEBUG: RulePackStrategy '{Name}' has {_rules.Count} rules.");

            // Create a new context with ONLY this pack's rules? 
            // Or append? Usually specific strategies want specific rules.
            // But we might want to keep global assumptions etc.
            
            // For a pure RulePackStrategy, we likely only want the pack's rules to apply.
            var localContext = new SolveContext(
                context.TargetVariable,
                _rules, // Use ONLY the pack rules
                context.MaxIterations,
                context.EnableTracing,
                context.AdditionalData,
                context.CancellationToken,
                context.SharedEGraph,
                context.MaxConcurrency
            );

            return _solver.Solve(problem, localContext);
        }
    }
}
