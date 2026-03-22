// Copyright Warren Harding 2026
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Decomposes proper rational functions with linear factors (including multiplicities) into partial fractions.
/// </summary>
public class PartialFractionStrategy : ISolverStrategy
{
    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        if (problem is Operation op && !(problem is Divide || (problem is Power p && p.Exponent is Number n && n.Value < 0)))
        {
            // Recurse into container operations like Vector, Equality, Add, etc.
            // But be careful not to recurse into Rational constructs we want to process (Multiply/Divide/Power-neg).
            // Actually, TryExtractRational handles Multiply/Divide/Power.
            // So we should only recurse if TryExtractRational returns false OR if it's a container like Vector/Equality.
            // But TryExtractRational is aggressive.
            // Let's recurse first if it's a container (Vector, Equality, Function 'and').
            
            if (problem is Sym.Operations.Vector || problem is Equality || (problem is Function fn && fn.Name.Equals("and", StringComparison.OrdinalIgnoreCase)))
            {
                var newArgs = new List<IExpression>();
                var argsChanged = false;
                var recurseContext = new SolveContext(null, context.Rules, context.MaxIterations, context.EnableTracing, context.AdditionalData, context.CancellationToken);
                foreach (var arg in op.Arguments)
                {
                    var res = Solve(arg, recurseContext);
                    if (res.IsSuccess && res.ResultExpression != null && !res.ResultExpression.InternalEquals(arg))
                    {
                        newArgs.Add(res.ResultExpression);
                        argsChanged = true;
                    }
                    else
                    {
                        newArgs.Add(arg);
                    }
                }
                if (argsChanged)
                {
                    return SolveResult.Success(op.WithArguments(newArgs.ToImmutableList()).Canonicalize(), "Recursed partial fraction.");
                }
                return SolveResult.Success(problem, "No changes in recursion.");
            }
        }

        if (!TryExtractRational(problem, out var numeratorExpr, out var denominatorExpr))
        {
            return SolveResult.Success(problem, "No changes performed.");
        }

        var variable = InferVariable(problem, context.TargetVariable);
        if (variable is null)
        {
            return SolveResult.Failure(problem, "PartialFractionStrategy requires a target variable or a univariate rational function.");
        }

        if (!Polynomial.TryCreate(numeratorExpr, variable, out var numerator) ||
            !Polynomial.TryCreate(denominatorExpr, variable, out var denominator))
        {
            return SolveResult.Failure(problem, "Unable to parse numerator or denominator as a polynomial.");
        }


        if (denominator.Degree <= 0)
        {
            return SolveResult.Success(problem, "No changes performed.");
        }

        var (quotientPoly, remainderPoly) = numerator.DivideWithRemainder(denominator);
        var denomFactorization = denominator.FactorLinear();

        // Only handle denominators that fully factor into linear terms (possibly with multiplicity).
        if (denomFactorization.Residual.Degree > 0 && denomFactorization.Residual.Coefficients.Count > 1 && !IsIrreducibleQuadratic(denomFactorization.Residual))
        {
            return SolveResult.Success(problem, "Denominator is not fully factorizable into linear terms.");
        }

        var linearRoots = denomFactorization.LinearRoots;
        if (linearRoots.Count == 0 && !IsIrreducibleQuadratic(denomFactorization.Residual))
        {
            return SolveResult.Success(problem, "No partial fraction expansion available.");
        }

        var terms = BuildPartialFractions(variable, quotientPoly, remainderPoly, denominator, linearRoots, denomFactorization.Residual);
        if (terms is null)
        {
            return SolveResult.Success(problem, "Partial fraction expansion could not be computed.");
        }

        var resultExpr = terms.Count switch
        {
            0 => problem,
            1 => terms[0],
            _ => new Add(terms.ToImmutableList()).Canonicalize()
        };

        var trace = context.EnableTracing ? ImmutableList.Create(problem, resultExpr) : null;
        var changed = !resultExpr.InternalEquals(problem);
        var message = changed ? "Partial fraction expansion applied." : "No changes performed.";
        return SolveResult.Success(resultExpr, message, trace);
    }

    private static bool TryExtractRational(IExpression expression, out IExpression numerator, out IExpression denominator)
    {
        // Prefer an explicit Divide node when present.
        if (expression is Divide div)
        {
            numerator = div.Numerator;
            denominator = div.Denominator;
            return true;
        }

        // Canonical form often represents division as multiplication by negative powers.
        // Example: A / B => Multiply(A, Power(B, -1)) => Multiply(A, Power(base, -n))
        if (expression is Power pow && pow.Exponent is Number expNum && expNum.Value < 0m)
        {
            var pos = -expNum.Value;
            numerator = new Number(1m);
            denominator = pos == 1m
                ? pow.Base
                : new Power(pow.Base, new Number(pos)).Canonicalize();
            return true;
        }

        if (expression is not Multiply mul)
        {
            numerator = new Number(0m);
            denominator = new Number(1m);
            return false;
        }

        var numeratorFactors = new List<IExpression>();
        var denominatorFactors = new List<IExpression>();

        foreach (var factor in mul.Arguments)
        {
            if (factor is Power factorPow && factorPow.Exponent is Number factorExp && factorExp.Value < 0m)
            {
                var pos = -factorExp.Value;
                denominatorFactors.Add(pos == 1m
                    ? factorPow.Base
                    : new Power(factorPow.Base, new Number(pos)).Canonicalize());
                continue;
            }

            numeratorFactors.Add(factor);
        }

        if (denominatorFactors.Count == 0)
        {
            numerator = new Number(0m);
            denominator = new Number(1m);
            return false;
        }

        numerator = numeratorFactors.Count switch
        {
            0 => new Number(1m),
            1 => numeratorFactors[0],
            _ => new Multiply(numeratorFactors.ToImmutableList()).Canonicalize()
        };

        denominator = denominatorFactors.Count switch
        {
            1 => denominatorFactors[0],
            _ => new Multiply(denominatorFactors.ToImmutableList()).Canonicalize()
        };

        return true;
    }

    private static Symbol? InferVariable(IExpression expr, Symbol? preferred)
    {
        if (preferred is not null) return preferred;
        return FindSymbol(expr);
    }

    private static Symbol? FindSymbol(IExpression expr)
    {
        if (expr is Symbol s) return s;
        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                var found = FindSymbol(arg);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static List<IExpression>? BuildPartialFractions(
        Symbol variable,
        Polynomial quotientPoly,
        Polynomial remainderPoly,
        Polynomial denominator,
        IReadOnlyList<Rational> roots,
        Polynomial residual)
    {
        var terms = new List<IExpression>();
        if (!(quotientPoly.Degree == 0 && quotientPoly.Coefficients[0].IsZero))
        {
            terms.Add(quotientPoly.ToExpression(variable));
        }

        var basis = BuildBasis(variable, roots, residual);
        if (basis.Count == 0) return terms;

        var system = BuildSystem(basis, remainderPoly, denominator);
        if (system is null) return null;

        var solutions = LinearSolveHelper.Solve(system.Value.Matrix, system.Value.Vector, out var failureReason);
        if (solutions is null) return null;

        for (int i = 0; i < basis.Count; i++)
        {
            var coeff = solutions[i];
            if (coeff.IsZero) continue;
            terms.Add(basis[i].BuildExpression(coeff));
        }

        return terms;
    }

    private static List<BasisTerm> BuildBasis(Symbol variable, IReadOnlyList<Rational> roots, Polynomial residual)
    {
        var basis = new List<BasisTerm>();
        var multiplicities = new Dictionary<Rational, int>();
        foreach (var r in roots)
        {
            multiplicities[r] = multiplicities.TryGetValue(r, out var count) ? count + 1 : 1;
        }

        foreach (var kvp in multiplicities)
        {
            for (int p = 1; p <= kvp.Value; p++)
            {
                var root = kvp.Key;
                var power = p;
                basis.Add(new BasisTerm(
                    x => Rational.One / Pow(x - root, power),
                    coeff =>
                    {
                        var linear = new Subtract(variable, root.ToExpression()).Canonicalize();
                        IExpression denom = power == 1 ? linear : new Power(linear, new Number(power)).Canonicalize();
                        return new Divide(coeff.ToExpression(), denom).Canonicalize();
                    }));
            }
        }

        if (IsIrreducibleQuadratic(residual))
        {
            var qExpr = residual.ToExpression(variable);
            basis.Add(new BasisTerm(
                x => x / Pow(residual.Evaluate(x), 1),
                coeff => new Divide(new Multiply(coeff.ToExpression(), variable).Canonicalize(), qExpr).Canonicalize()));
            basis.Add(new BasisTerm(
                x => Rational.One / Pow(residual.Evaluate(x), 1),
                coeff => new Divide(coeff.ToExpression(), qExpr).Canonicalize()));
        }

        return basis;
    }

    private static (Rational[,] Matrix, Rational[] Vector)? BuildSystem(
        IReadOnlyList<BasisTerm> basis,
        Polynomial remainder,
        Polynomial denominator)
    {
        var n = basis.Count;
        if (n == 0) return null;

        var matrix = new Rational[n, n];
        var vector = new Rational[n];

        var samples = new List<Rational>();
        var candidate = -n;
        while (samples.Count < n + 2 && candidate < n * 6)
        {
            var x = Rational.FromInteger(candidate);
            candidate++;

            if (denominator.Evaluate(x).IsZero) continue;
            samples.Add(x);
        }

        if (samples.Count < n)
        {
            return null;
        }

        for (int i = 0; i < n; i++)
        {
            var x = samples[i];
            vector[i] = remainder.Evaluate(x) / denominator.Evaluate(x);
            for (int j = 0; j < n; j++)
            {
                matrix[i, j] = basis[j].Evaluate(x);
            }
        }

        return (matrix, vector);
    }

    private static Rational Pow(Rational value, int power)
    {
        var result = Rational.One;
        for (int i = 0; i < power; i++)
        {
            result *= value;
        }
        return result;
    }

    private static bool IsIrreducibleQuadratic(Polynomial p)
    {
        if (p.Degree != 2) return false;
        var a = p.Coefficients[2];
        var b = p.Coefficients[1];
        var c = p.Coefficients[0];

        var disc = b * b - Rational.FromInteger(4) * a * c;
        if (disc.IsZero) return false;
        if (disc.TrySqrt(out var sqrt))
        {
            // Perfect square discriminant => reducible.
            return false;
        }
        return true;
    }

    private sealed record BasisTerm(Func<Rational, Rational> Evaluate, Func<Rational, IExpression> BuildExpression);
}
