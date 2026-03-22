using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Strategy for polynomial division and remainder theorem.
/// </summary>
public sealed class PolynomialDivisionStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "PolynomialDivisionStrategy";
    private readonly RulePackStrategy _rulePackStrategy;

    public PolynomialDivisionStrategy()
    {
        var packs = SymRules.RulePackLibrary.GetRulePacks();
        var pack = packs.FirstOrDefault(p => p.Name == "AlgebraicStrategy");
        if (pack != null) _rulePackStrategy = new RulePackStrategy(pack);
        else _rulePackStrategy = null!;
    }

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        IExpression current = problem;

        // 1. Apply rules from the pack
        if (_rulePackStrategy != null)
        {
            var res = _rulePackStrategy.Solve(current, context);
            if (res.IsSuccess && res.ResultExpression != null)
            {
                current = res.ResultExpression;
            }
        }

        var trace = context.EnableTracing ? ImmutableList.CreateBuilder<IExpression>() : null;
        if (trace != null) trace.Add(problem);
        if (trace != null && !current.InternalEquals(problem)) trace.Add(current);

        var changed = !current.InternalEquals(problem);

        var simplified = Simplify(current);
        if (!simplified.InternalEquals(current))
        {
            current = simplified;
            changed = true;
            trace?.Add(current);
        }

        return SolveResult.Success(current, changed ? "Polynomial division applied." : "No changes performed.", trace?.ToImmutable());
    }

    private static IExpression Simplify(IExpression expr)
    {
        // 1. x^n % (x - a) -> a^n
        if (expr is Function f && f.Name.Equals("mod", StringComparison.OrdinalIgnoreCase) && f.Arguments.Count == 2)
        {
            var modDividend = f.Arguments[0];
            var modDivisor = f.Arguments[1];

            if (modDivisor is Subtract sub && sub.RightOperand is Number a && sub.LeftOperand is Symbol x)
            {
                if (modDividend.ContainsSymbol(x))
                {
                    // Polynomial Remainder Theorem: P(x) % (x - a) = P(a)
                    return SubstitutionStrategy.SubstituteInternal(modDividend, new Dictionary<string, IExpression> { [x.Name] = a }).Canonicalize();
                }
            }
            if (modDivisor is Add add && add.Arguments.Count == 2)
            {
                var x2 = add.Arguments.OfType<Symbol>().FirstOrDefault();
                var a2 = add.Arguments.OfType<Number>().FirstOrDefault();
                if (x2 != null && a2 != null && modDividend.ContainsSymbol(x2))
                {
                    // P(x) % (x + a) = P(-a)
                    return SubstitutionStrategy.SubstituteInternal(modDividend, new Dictionary<string, IExpression> { [x2.Name] = new Number(-a2.Value) }).Canonicalize();
                }
            }
        }

        // 2. Polynomial division
        IExpression? dividend = null;
        IExpression? divisor = null;

        if (expr is Divide div)
        {
            dividend = div.Numerator;
            divisor = div.Denominator;
        }
        else if (expr is Multiply mul && mul.Arguments.Any(a => a is Power p && p.Exponent is Number n && n.Value == -1m))
        {
            var pow = mul.Arguments.OfType<Power>().First(a => a.Exponent is Number n && n.Value == -1m);
            divisor = pow.Base;
            var rest = mul.Arguments.Remove(pow);
            dividend = rest.Count == 0 ? new Number(1m) : (rest.Count == 1 ? rest[0] : new Multiply(rest).Canonicalize());
        }

        if (dividend != null && divisor != null)
        {
            Symbol? x = null;
            if (divisor is Add add2 && add2.Arguments.Count == 2)
            {
                x = add2.Arguments.OfType<Symbol>().FirstOrDefault();
            }
            else if (divisor is Subtract sub2 && sub2.LeftOperand is Symbol xs)
            {
                x = xs;
            }

            if (x != null)
            {
                // Try synthetic division for simple cases
                if (TrySyntheticDivision(dividend, divisor, x, out var quotient, out var remainder))
                {
                    if (remainder.InternalEquals(new Number(0m))) return quotient;
                    return new Add(quotient, new Divide(remainder, divisor)).Canonicalize();
                }
            }
        }

        if (expr is Operation op)
        {
            var newArgs = op.Arguments.Select(Simplify).ToImmutableList();
            return op.WithArguments(newArgs).Canonicalize();
        }

        return expr;
    }

    private static bool TrySyntheticDivision(IExpression dividend, IExpression divisor, Symbol x, out IExpression quotient, out IExpression remainder)
    {
        quotient = null!;
        remainder = null!;

        // Currently only handle (x + a) or (x - a)
        decimal a = 0m;
        if (divisor is Add add && add.Arguments.Count == 2 && add.Arguments.Contains(x))
        {
            var aExpr = add.Arguments.First(arg => !arg.InternalEquals(x));
            if (aExpr is Number n) a = -n.Value;
            else return false;
        }
        else if (divisor is Subtract sub && sub.LeftOperand.InternalEquals(x))
        {
            if (sub.RightOperand is Number n) a = n.Value;
            else return false;
        }
        else return false;

        // Extract coefficients of dividend
        if (!Polynomial.TryCreate(dividend, x, out var poly)) return false;

        var coeffs = poly.Coefficients;
        int degree = poly.Degree;
        
        // Correct synthetic division:
        // Dividend: a_n x^n + ... + a_0
        // Divisor: x - a
        // q_{n-1} = a_n
        // q_{i-1} = a_i + q_i * a
        // remainder = a_0 + q_0 * a
        
        var q = new Rational[degree];
        var rem = coeffs[degree];
        for (int i = degree - 1; i >= 0; i--)
        {
            q[i] = rem;
            rem = coeffs[i] + rem * Rational.FromDecimal(a);
        }

        var qTerms = new List<IExpression>();
        for (int i = 0; i < q.Length; i++)
        {
            if (q[i].IsZero) continue;
            IExpression term = q[i].ToExpression();
            if (i > 0)
            {
                var pow = i == 1 ? (IExpression)x : new Power(x, new Number(i)).Canonicalize();
                term = new Multiply(term, pow).Canonicalize();
            }
            qTerms.Add(term);
        }

        quotient = qTerms.Count == 0 ? new Number(0m) : (qTerms.Count == 1 ? qTerms[0] : new Add(qTerms.ToImmutableList())).Canonicalize();
        remainder = rem.ToExpression();
        return true;
    }
}
