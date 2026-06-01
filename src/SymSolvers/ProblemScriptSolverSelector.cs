using System;
using System.Collections.Immutable;
using System.Linq;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

internal static class ProblemScriptSolverSelector
{
    public static ISolverStrategy CreateSolveStrategy(IExpression problem, SolveContext context)
    {
        if (LooksLikeSeries(problem))
        {
            return new SeriesExpansionStrategy();
        }

        if (LooksLikeIntegration(problem))
        {
            return new IntegrationStrategy();
        }

        if (LooksLikeLimit(problem))
        {
            return new LimitStrategy();
        }

        if (LooksLikeDifferentialEquation(problem))
        {
            return new DifferentialEquationStrategy();
        }

        if (LooksLikeRecurrence(problem))
        {
            return new DiscreteRecurrenceStrategy();
        }

        if (LooksLikeInequality(problem))
        {
            return new InequalitySolveStrategy();
        }

        if (LooksLikeLinearAlgebra(problem))
        {
            return new LinearAlgebraStrategy();
        }

        return EGraphBackendSelector.CreateSolveStrategy(context);
    }

    private static bool LooksLikeSeries(IExpression problem)
        => ContainsExpression(problem, expr => expr is SeriesExpansion || HasFunctionName(expr, "SeriesExpansion"));

    private static bool LooksLikeIntegration(IExpression problem)
        => ContainsExpression(problem, expr =>
            expr is Integral or DefiniteIntegral ||
            HasFunctionName(expr, "Integral") ||
            HasFunctionName(expr, "DefiniteIntegral"));

    private static bool LooksLikeLimit(IExpression problem)
        => ContainsExpression(problem, expr => expr is Limit || HasFunctionName(expr, "Limit"));

    private static bool LooksLikeDifferentialEquation(IExpression problem)
        => EnumerateEqualities(problem).Any(ContainsDerivative);

    private static bool LooksLikeRecurrence(IExpression problem)
        => EnumerateEqualities(problem).Any(IsRecurrenceRelation);

    private static bool LooksLikeInequality(IExpression problem)
        => ContainsExpression(problem, expr =>
            expr is Function fn &&
            (string.Equals(fn.Name, "le", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(fn.Name, "lt", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(fn.Name, "ge", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(fn.Name, "gt", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(fn.Name, "and", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(fn.Name, "or", StringComparison.OrdinalIgnoreCase)));

    private static bool LooksLikeLinearAlgebra(IExpression problem)
        => ContainsExpression(problem, expr =>
            expr is Matrix ||
            expr is MatrixMultiply ||
            expr is Equality eq && (ContainsMatrixyShape(eq.LeftOperand) || ContainsMatrixyShape(eq.RightOperand)) ||
            expr is Function fn && IsLinearAlgebraFunction(fn));

    private static bool IsLinearAlgebraFunction(Function fn)
        => string.Equals(fn.Name, "determinant", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(fn.Name, "inverse", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsMatrixyShape(IExpression expr)
        => ContainsExpression(expr, candidate =>
            candidate is Matrix or MatrixMultiply ||
            candidate is Function fn && IsLinearAlgebraFunction(fn));

    private static bool ContainsExpression(IExpression expr, Func<IExpression, bool> predicate)
    {
        if (predicate(expr))
        {
            return true;
        }

        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                if (ContainsExpression(arg, predicate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ImmutableArray<Equality> EnumerateEqualities(IExpression problem)
    {
        if (problem is Equality eq)
        {
            return [eq];
        }

        if (problem is Vector vector)
        {
            return vector.Arguments.OfType<Equality>().ToImmutableArray();
        }

        return [];
    }

    private static bool ContainsDerivative(IExpression expr)
        => ContainsExpression(expr, candidate => candidate is Derivative || HasFunctionName(candidate, "Derivative"));

    private static bool IsRecurrenceRelation(Equality eq)
    {
        if (eq.LeftOperand is not Function leftFn || leftFn.Arguments.Count != 1 || leftFn.Arguments[0] is not Sym.Atoms.Symbol)
        {
            return false;
        }

        return ContainsExpression(eq.RightOperand, expr =>
            expr is Function fn &&
            string.Equals(fn.Name, leftFn.Name, StringComparison.Ordinal) &&
            fn.Arguments.Count == 1);
    }

    private static bool HasFunctionName(IExpression expr, string expectedName)
        => expr is Function fn && string.Equals(fn.Name, expectedName, StringComparison.OrdinalIgnoreCase);
}
