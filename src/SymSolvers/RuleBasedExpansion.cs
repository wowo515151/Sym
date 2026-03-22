using System;
using Sym.Core;
using Sym.Core.Rewriters;

namespace SymSolvers;

public static class RuleBasedExpansion
{
    public static IExpression Expand(IExpression expression, SolveContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (expression is null) return expression!;

        var idx = RuleIndexProvider.GetRuleIndex(context);
        var result = Rewriter.RewriteFully(expression, idx.AllRules, context.MaxIterations, context.Assumptions, context.CancellationToken);
        return result.RewrittenExpression!;
    }
}
