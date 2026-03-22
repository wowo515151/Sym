// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Hybrid Newton solver using symbolic derivatives for faster convergence.
/// </summary>
public class NewtonHybridStrategy : ISolverStrategy
{
    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is not Equality eq) return SolveResult.Failure(problem, "NewtonHybridStrategy requires an Equality.");

        var target = context.TargetVariable ?? FindFirstSymbol(eq);
        if (target is null) return SolveResult.Failure(problem, "Target variable must be specified or inferable.");

        var residual = new Add(eq.LeftOperand, new Multiply(new Number(-1m), eq.RightOperand).Canonicalize()).Canonicalize();
        IExpression function = residual;
        IExpression derivative = CalculusHelper.DifferentiateExpression(function, target);

        var guess = context.GetInt("InitialGuess", 0);
        var tol = context.GetInt("NumericToleranceMicros", 10) / 1_000_000m;
        var maxIter = context.GetInt("NumericMaxIterations", 25);
        var residualLimit = GetDecimalOption(context, SolverOptionKeys.HybridResidualLimit, 1_000_000m);
        var stallLimit = Math.Max(2, context.GetInt(SolverOptionKeys.HybridStallLimit, 3));
        var useIsolation = GetBoolOption(context, SolverOptionKeys.HybridIntervalIsolation, true);

        var assignments = new Dictionary<string, decimal> { { target.Name, guess } };
        decimal current = guess;

        if (context.EnableTracing) Console.WriteLine($"DEBUG: NewtonHybridStrategy starting on: {residual.ToDisplayString()} Target: {target.Name} Guess: {guess}");

        if (!TryEvaluate(residual, assignments, out var currentResidual, out var err))
        {
            if (context.EnableTracing) Console.WriteLine($"DEBUG: NewtonHybridStrategy evaluation failed: {err}");
            return SolveResult.Failure(problem, $"Numeric evaluation failed: {err}");
        }

        var bracket = FindBracket(residual, target, current, assignments, currentResidual, useIsolation);
        if (context.EnableTracing && bracket != null) Console.WriteLine($"DEBUG: NewtonHybridStrategy found bracket: [{bracket.Value.left}, {bracket.Value.right}]");

        var bestResidual = decimal.Abs(currentResidual);
        var bestPoint = current;
        var stagnant = 0;

        for (int i = 0; i < maxIter; i++)
        {
            context.ThrowIfCancellationRequested();
            if (context.EnableTracing) Console.WriteLine($"DEBUG: NewtonHybridStrategy iter {i}: x={current} residual={currentResidual}");

            if (decimal.Abs(currentResidual) < tol)
            {
                var snapped = TrySnapToExactIntegerRoot(residual, target, current) ?? current;
                var solution = new Equality(target, new Number(snapped)).Canonicalize();
                var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(problem, solution) : null;
                if (context.EnableTracing) Console.WriteLine($"DEBUG: NewtonHybridStrategy converged to {snapped} in {i} iterations.");
                return SolveResult.Success(solution, $"Hybrid Newton converged in {i} iterations.", trace);
            }

            if (decimal.Abs(currentResidual) > residualLimit && bracket is not null)
            {
                current = Mid(bracket.Value.left, bracket.Value.right);
                assignments[target.Name] = current;
                if (!TryEvaluate(residual, assignments, out currentResidual, out var overflowErr))
                {
                    return SolveResult.Failure(problem, $"Numeric evaluation failed: {overflowErr}");
                }
                stagnant++;
                continue;
            }

            if (!TryEvaluate(derivative, assignments, out var fp, out var errD) || fp == 0m)
            {
                if (bracket is null)
                {
                    return SolveResult.Failure(problem, $"Derivative evaluation failed: {errD ?? "zero derivative"}");
                }

                current = Mid(bracket.Value.left, bracket.Value.right);
                assignments[target.Name] = current;
                if (!TryEvaluate(residual, assignments, out currentResidual, out var midErr))
                {
                    return SolveResult.Failure(problem, $"Numeric evaluation failed during bracketing: {midErr}");
                }
                stagnant++;
                continue;
            }

            var step = currentResidual / fp;
            decimal next = current - step;
            decimal nextResidual;
            int backtrack = 0;

            while (true)
            {
                assignments[target.Name] = next;
                if (TryEvaluate(residual, assignments, out nextResidual, out var nextErr))
                {
                    if (decimal.Abs(nextResidual) <= decimal.Abs(currentResidual) || backtrack >= 6)
                    {
                        break;
                    }
                }
                else
                {
                    if (bracket is not null && backtrack >= 2)
                    {
                        next = Mid(bracket.Value.left, bracket.Value.right);
                        assignments[target.Name] = next;
                        if (TryEvaluate(residual, assignments, out nextResidual, out var midEvalErr))
                        {
                            break;
                        }
                        return SolveResult.Failure(problem, $"Numeric evaluation failed: {midEvalErr ?? nextErr}");
                    }
                }

                step /= 2m;
                next = current - step;
                backtrack++;
            }

            if (bracket is not null && (next <= bracket.Value.left || next >= bracket.Value.right))
            {
                next = Mid(bracket.Value.left, bracket.Value.right);
                assignments[target.Name] = next;
                if (!TryEvaluate(residual, assignments, out nextResidual, out var midErr))
                {
                    return SolveResult.Failure(problem, $"Numeric evaluation failed during bracketing: {midErr}");
                }
            }

            if (decimal.Abs(nextResidual) > decimal.Abs(currentResidual) && bracket is not null)
            {
                var midpoint = Mid(bracket.Value.left, bracket.Value.right);
                assignments[target.Name] = midpoint;
                if (TryEvaluate(residual, assignments, out var midResidual, out _))
                {
                    next = midpoint;
                    nextResidual = midResidual;
                }
            }

            current = next;
            currentResidual = nextResidual;

            var absResidual = decimal.Abs(currentResidual);
            if (absResidual < bestResidual)
            {
                bestResidual = absResidual;
                bestPoint = current;
                stagnant = 0;
            }
            else
            {
                stagnant++;
            }

            if (bracket is not null)
            {
                // Maintain bracket invariant (endpoints and their residuals).
                if (SignChange(bracket.Value.fLeft, currentResidual))
                {
                    bracket = (bracket.Value.left, current, bracket.Value.fLeft, currentResidual);
                }
                else if (SignChange(currentResidual, bracket.Value.fRight))
                {
                    bracket = (current, bracket.Value.right, currentResidual, bracket.Value.fRight);
                }

                if (stagnant >= stallLimit)
                {
                    var midpoint = Mid(bracket.Value.left, bracket.Value.right);
                    assignments[target.Name] = midpoint;
                    if (TryEvaluate(residual, assignments, out var midResidual, out _))
                    {
                        current = midpoint;
                        currentResidual = midResidual;
                    }
                    stagnant = 0;
                }
            }
        }

        var bestSolution = new Equality(target, new Number(bestPoint)).Canonicalize();
        var traceBest = context.EnableTracing ? ImmutableList.Create<IExpression>(problem, bestSolution) : null;
        return SolveResult.Failure(bestSolution, $"Hybrid Newton did not converge within iteration limit. Best residual={bestResidual}.", traceBest);
    }

    private static decimal? TrySnapToExactIntegerRoot(IExpression residual, Symbol target, decimal value)
    {
        var nearest = decimal.Round(value, 0);
        if (decimal.Abs(value - nearest) > 0.001m)
        {
            return null;
        }

        var substituted = Substitute(residual, target, new Number(nearest)).Canonicalize();
        if (substituted is Number n && n.Value == 0m)
        {
            return nearest;
        }

        return null;
    }

    private static IExpression Substitute(IExpression expr, Symbol target, IExpression replacement)
    {
        if (expr is Symbol s && s.InternalEquals(target))
        {
            return replacement;
        }

        if (expr is Operation op)
        {
            var args = op.Arguments.Select(a => Substitute(a, target, replacement)).ToImmutableList();
            return op.WithArguments(args);
        }

        return expr;
    }

    private static (decimal left, decimal right, decimal fLeft, decimal fRight)? FindBracket(
        IExpression residual,
        Symbol target,
        decimal start,
        Dictionary<string, decimal> assignments,
        decimal startResidual,
        bool useIsolation)
    {
        var offsets = useIsolation
            ? new[] { 0.25m, 0.5m, 1m, 2m, 3m, 4m, 6m, 8m }
            : new[] { 0.5m, 1m, 2m, 4m };

        foreach (var step in offsets)
        {
            var left = start - step;
            var right = start + step;

            assignments[target.Name] = left;
            var leftOk = TryEvaluate(residual, assignments, out var fl, out _);
            assignments[target.Name] = right;
            var rightOk = TryEvaluate(residual, assignments, out var fr, out _);

            if (leftOk && SignChange(fl, startResidual))
            {
                assignments[target.Name] = start;
                return (left, start, fl, startResidual);
            }

            if (rightOk && SignChange(startResidual, fr))
            {
                assignments[target.Name] = start;
                return (start, right, startResidual, fr);
            }

            if (leftOk && rightOk && SignChange(fl, fr))
            {
                assignments[target.Name] = start;
                return (left, right, fl, fr);
            }
        }

        assignments[target.Name] = start;
        return null;
    }

    private static bool TryEvaluate(IExpression expr, Dictionary<string, decimal> assignments, out decimal value, out string? error)
    {
        if (NumericEvaluator.TryEvaluate(expr, assignments, out value, out var evalError))
        {
            error = evalError;
            return true;
        }

        error = evalError;
        value = 0m;
        return false;
    }

    private static bool SignChange(decimal a, decimal b)
    {
        try
        {
            return a * b < 0;
        }
        catch (OverflowException)
        {
            // If they are so large they overflow when multiplied, check their signs directly
            return (a > 0 && b < 0) || (a < 0 && b > 0);
        }
    }

    private static decimal Mid(decimal a, decimal b)
    {
        try
        {
            return (a + b) / 2m;
        }
        catch (OverflowException)
        {
            // If a+b overflows, we can use a/2 + b/2
            return (a / 2m) + (b / 2m);
        }
    }

    private static decimal GetDecimalOption(SolveContext context, string key, decimal defaultValue)
    {
        if (context.AdditionalData is not null && context.AdditionalData.TryGetValue(key, out var raw) && raw is not null)
        {
            switch (raw)
            {
                case decimal d: return d;
                case double dbl: return NumericEvaluator.SafeToDecimal(dbl);
                case int i: return i;
                case long l: return l;
                case string s when decimal.TryParse(s, out var parsed): return parsed;
            }
        }

        return defaultValue;
    }

    private static bool GetBoolOption(SolveContext context, string key, bool defaultValue)
    {
        if (context.AdditionalData is not null && context.AdditionalData.TryGetValue(key, out var raw) && raw is not null)
        {
            return raw switch
            {
                bool b => b,
                string s when bool.TryParse(s, out var parsed) => parsed,
                _ => defaultValue
            };
        }

        return defaultValue;
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
}
