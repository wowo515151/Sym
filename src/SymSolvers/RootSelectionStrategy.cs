using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

public sealed class RootSelectionStrategy : ISolverStrategy
{
    private sealed record RootCandidate(Rational Value, IExpression Expression);

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(problem, "Problem expression cannot be null.");

        if (!TryGetSelectionMode(context, out var mode))
        {
            return SolveResult.Failure(problem, "Root selection mode not specified.");
        }

        var target = context.TargetVariable ?? FindFirstSymbol(problem);
        if (target is null) return SolveResult.Failure(problem, "Target variable must be specified or inferable.");

        var equalities = new List<Equality>();
        var inequalities = new List<IExpression>();
        ExtractConstraints(problem, equalities, inequalities);

        var candidates = new List<RootCandidate>();

        if (equalities.Count == 1)
        {
            var eq = equalities[0];
            var residual = new Add(eq.LeftOperand, new Multiply(new Number(-1m), eq.RightOperand).Canonicalize()).Canonicalize();
            if (Polynomial.TryCreate(residual, target, out var poly))
            {
                var roots = poly.FactorLinear().LinearRoots;
                foreach (var root in roots)
                {
                    if (TryConvertRoot(root, out var expr))
                    {
                        candidates.Add(new RootCandidate(root, expr));
                    }
                }
            }
        }
        else if (problem is Vector vec)
        {
            foreach (var arg in vec.Arguments)
            {
                if (arg is Equality eq && ExpressionPropertyValidator.TryGetIsolatedSolutionSymbol(eq, out var sym, out var val) &&
                    string.Equals(sym.Name, target.Name, StringComparison.Ordinal))
                {
                    if (NumericEvaluator.TryEvaluate(val, ImmutableDictionary<string, decimal>.Empty, out var num, out _))
                    {
                        candidates.Add(new RootCandidate(Rational.FromDecimal(num), val));
                    }
                }
            }
        }

        if (candidates.Count == 0)
        {
            return SolveResult.Failure(problem, "No selectable roots available.");
        }

        var filtered = candidates
            .Where(c => SatisfiesConstraints(c.Value, target, inequalities))
            .ToList();

        if (filtered.Count == 0)
        {
            return SolveResult.Failure(problem, "No roots satisfy inequality constraints.");
        }

        RootCandidate selected = mode == RootSelectionMode.Max
            ? filtered.OrderByDescending(c => c.Value.ToDecimal()).First()
            : filtered.OrderBy(c => c.Value.ToDecimal()).First();

        var result = new Equality(target, selected.Expression).Canonicalize();
        var trace = context.EnableTracing ? ImmutableList.Create(problem, result) : null;
        return SolveResult.Success(result, $"Selected {mode.ToString().ToLowerInvariant()} root.", trace);
    }

    public static bool HasRootSelectionMode(SolveContext context)
        => TryGetSelectionMode(context, out _);

    private static bool TryGetSelectionMode(SolveContext context, out RootSelectionMode mode)
    {
        mode = default;
        if (context.AdditionalData is null) return false;
        if (!context.AdditionalData.TryGetValue(SolverOptionKeys.RootSelectionMode, out var raw)) return false;

        if (raw is string text)
        {
            if (text.Equals("max", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("maximum", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("largest", StringComparison.OrdinalIgnoreCase))
            {
                mode = RootSelectionMode.Max;
                return true;
            }

            if (text.Equals("min", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("minimum", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("smallest", StringComparison.OrdinalIgnoreCase))
            {
                mode = RootSelectionMode.Min;
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertRoot(Rational root, out IExpression expression)
    {
        try
        {
            expression = root.ToExpression();
            return true;
        }
        catch
        {
            expression = null!;
            return false;
        }
    }

    private static void ExtractConstraints(IExpression expr, List<Equality> equalities, List<IExpression> inequalities)
    {
        if (expr is Equality eq)
        {
            equalities.Add(eq);
            return;
        }

        if (ExpressionClassification.IsInequalityExpression(expr))
        {
            inequalities.Add(expr);
            return;
        }

        if (ExpressionClassification.IsConjunction(expr))
        {
            foreach (var part in ExpressionClassification.FlattenConjunction(expr))
            {
                ExtractConstraints(part, equalities, inequalities);
            }
            return;
        }

        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                ExtractConstraints(arg, equalities, inequalities);
            }
        }
    }

    private static bool SatisfiesConstraints(Rational root, Symbol target, IReadOnlyList<IExpression> inequalities)
    {
        decimal value;
        try
        {
            value = root.ToDecimal();
        }
        catch
        {
            return false;
        }

        foreach (var inequality in inequalities)
        {
            if (!EvaluateInequality(inequality, target, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateInequality(IExpression expr, Symbol target, decimal value)
    {
        if (ExpressionClassification.IsConjunction(expr))
        {
            foreach (var part in ExpressionClassification.FlattenConjunction(expr))
            {
                if (!EvaluateInequality(part, target, value)) return false;
            }
            return true;
        }

        if (expr is Function fn && ExpressionClassification.IsInequalityName(fn.Name))
        {
            var assignments = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                [target.Name] = value
            };

            if (!NumericEvaluator.TryEvaluate(fn.Arguments[0], assignments, out var left, out _) ||
                !NumericEvaluator.TryEvaluate(fn.Arguments[1], assignments, out var right, out _))
            {
                return true;
            }

            return fn.Name.ToLowerInvariant() switch
            {
                "lt" => left < right,
                "le" => left <= right,
                "gt" => left > right,
                "ge" => left >= right,
                "ne" => left != right,
                _ => true
            };
        }

        return true;
    }

    private static Symbol? FindFirstSymbol(IExpression expression)
    {
        if (expression is Symbol s) return s;
        if (expression is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                var found = FindFirstSymbol(arg);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private enum RootSelectionMode
    {
        Max,
        Min
    }
}
