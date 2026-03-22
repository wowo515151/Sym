// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

internal static class LinearExtraction
{
    public static bool TryExtractLinear(IExpression expr, IReadOnlyList<IExpression> symbols, out decimal[] coefficients, out decimal constant)
    {
        coefficients = new decimal[symbols.Count];
        constant = 0m;
        return ExpressionHelpers.TryExtractLinearStruct(expr, symbols, ref coefficients, ref constant);
    }

    public static bool TryExtractLinearFromEquality(Equality eq, IReadOnlyList<IExpression> symbols, out decimal[] coefficients, out decimal constant, System.Threading.CancellationToken ct, int maxIterations = 10)
    {
        var diff = new Subtract(eq.LeftOperand, eq.RightOperand).Canonicalize();
        var prepared = ExpandAndSimplify(diff, maxIterations, ct);

        if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
        {
            Console.WriteLine($"DEBUG: TryExtractLinear diff: {prepared.ToDisplayString()}");
        }

        return TryExtractLinear(prepared, symbols, out coefficients, out constant);
    }

    private static IExpression ExpandAndSimplify(IExpression expr, int maxIterations, System.Threading.CancellationToken ct)
    {
        var simplifier = new EGraphSolverStrategy();
        var context = new SolveContext(cancellationToken: ct);

        var current = expr;
        for (int i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            var last = current;
            current = RuleBasedExpansion.Expand(current, context).Canonicalize();
            var simplified = simplifier.Solve(current, context);
            if (simplified.IsSuccess && simplified.ResultExpression is not null)
            {
                current = simplified.ResultExpression.Canonicalize();
            }
            if (current.InternalEquals(last)) break;
        }

        return current;
    }
}
