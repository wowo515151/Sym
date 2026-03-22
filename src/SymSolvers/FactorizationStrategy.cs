using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using Sym.CSharpIO;

namespace SymSolvers;

/// <summary>
/// Factorization strategy handling common factors, multivariate monomials, polynomial GCDs,
/// and optional full linear factorization for univariate polynomials.
/// </summary>
public class FactorizationStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "FactorizationStrategy";
    private readonly RulePackStrategy _rulePackStrategy;

    public FactorizationStrategy()
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
        var traceBuilder = context.EnableTracing ? ImmutableList.CreateBuilder<IExpression>() : null;
        if (traceBuilder != null) traceBuilder.Add(problem);
        
        // 1. Apply rules from the pack first
        if (_rulePackStrategy != null)
        {
            var res = _rulePackStrategy.Solve(current, context);
            if (res.IsSuccess && res.ResultExpression != null && !res.ResultExpression.InternalEquals(current))
            {
                current = res.ResultExpression;
                if (traceBuilder != null) traceBuilder.Add(current);
            }
        }

        bool changedAtLeastOnce = !current.InternalEquals(problem);

        // Iteratively factor until no more changes.
        for (int i = 0; i < 10; i++)
        {
            context.ThrowIfCancellationRequested();
            var last = current;
            current = FactorizeStep(current, context);
            if (current.InternalEquals(last)) break;
            
            if (traceBuilder != null) traceBuilder.Add(current);
            changedAtLeastOnce = true;
        }

        return SolveResult.Success(current, changedAtLeastOnce ? "Factorization applied." : "No changes performed.", traceBuilder?.ToImmutable());
    }

    private IExpression FactorizeStep(IExpression problem, SolveContext context)
    {
        context.ThrowIfCancellationRequested();

        // Recursively handle Equality
        if (problem is Equality eq)
        {
            var left = FactorizeStep(eq.LeftOperand, context);
            var right = FactorizeStep(eq.RightOperand, context);
            return new Equality(left, right).Canonicalize();
        }

        if (problem is Multiply mul)
        {
            var newArgs = mul.Arguments.Select(arg => FactorizeStep(arg, context)).ToImmutableList();
            return new Multiply(newArgs).Canonicalize();
        }

        if (problem is Vector vec)
        {
            var newArgs = vec.Arguments.Select(arg => FactorizeStep(arg, context)).ToImmutableList();
            return new Vector(newArgs).Canonicalize();
        }

        if (problem is Divide div)
        {
            var num = FactorizeStep(div.Numerator, context);
            var den = FactorizeStep(div.Denominator, context);
            return new Divide(num, den).Canonicalize();
        }

        if (problem is Power pow)
        {
            var b = FactorizeStep(pow.Base, context);
            var e = FactorizeStep(pow.Exponent, context);
            return new Power(b, e).Canonicalize();
        }

        if (problem is not Add add)
        {
            var enableLinearRec = context.GetBool(SolverOptionKeys.EnableLinearFactorization, true);
            if (enableLinearRec)
            {
                var linear = TryLinearFactorization(problem, context);
                if (linear != null && linear.IsSuccess && linear.ResultExpression != null) 
                {
                    return linear.ResultExpression;
                }
            }
            return problem;
        }

        // If it's a constraint bundle (sum of equalities), recurse into arguments
        if (add.Arguments.Any(a => a is Equality || a is Vector))
        {
            var newArgs = add.Arguments.Select(arg => FactorizeStep(arg, context)).ToImmutableList();
            return new Add(newArgs).Canonicalize();
        }

        var enableLinear = context.GetBool(SolverOptionKeys.EnableLinearFactorization, true);

        var commonFactorResult = TryCommonFactor(add, context);
        if (commonFactorResult != null && commonFactorResult.IsSuccess && commonFactorResult.ResultExpression != null && !commonFactorResult.ResultExpression.InternalEquals(add)) 
        {
            return commonFactorResult.ResultExpression;
        }

        var multi = TryMultivariateFactor(add, context);
        if (multi != null && multi.IsSuccess && multi.ResultExpression != null && !multi.ResultExpression.InternalEquals(add))
        {
            return multi.ResultExpression;
        }

        var polyGcd = TryPolynomialGcdFactor(add, context);
        if (polyGcd != null && polyGcd.IsSuccess && polyGcd.ResultExpression != null && !polyGcd.ResultExpression.InternalEquals(add))
        {
            return polyGcd.ResultExpression;
        }

        var diffSquares = TryDifferenceOfSquares(add, context);
        if (diffSquares != null && diffSquares.IsSuccess && diffSquares.ResultExpression != null && !diffSquares.ResultExpression.InternalEquals(add))
        {
            return diffSquares.ResultExpression;
        }

        if (enableLinear)
        {
            var linear = TryLinearFactorization(problem, context);
            if (linear != null && linear.IsSuccess && linear.ResultExpression != null && !linear.ResultExpression.InternalEquals(problem))
            {
                return linear.ResultExpression;
            }
        }

        return problem;
    }

    private static SolveResult? TryDifferenceOfSquares(Add add, SolveContext context)
    {
        if (add.Arguments.Count != 2) return null;

        var t1 = add.Arguments[0];
        var t2 = add.Arguments[1];

        // Console.WriteLine($"Checking DiffSquares: {t1} + {t2}");

        if (IsNegativeSquare(t1, out var root1) && IsPositiveSquare(t2, out var root2))
        {
            // -B + A -> A - B -> (sqrtA - sqrtB)(sqrtA + sqrtB)
            return ApplyDiff(root2, root1, add, context);
        }

        if (IsPositiveSquare(t1, out var r1) && IsNegativeSquare(t2, out var r2))
        {
            // A - B
            return ApplyDiff(r1, r2, add, context);
        }

        return null;
    }

    private static SolveResult ApplyDiff(IExpression rootA, IExpression rootB, IExpression original, SolveContext context)
    {
        // (A - B)(A + B)
        var term1 = new Add(rootA, new Multiply(new Number(-1m), rootB)).Canonicalize();
        var term2 = new Add(rootA, rootB).Canonicalize();
        var factored = new Multiply(term1, term2).Canonicalize();
        
        // Console.WriteLine($"DiffSquares success: {factored.ToDisplayString()}");
        var trace = context.EnableTracing ? ImmutableList.Create(original, factored) : null;
        return SolveResult.Success(factored, "Difference of squares factored.", trace);
    }

    private static bool IsPositiveSquare(IExpression expr, out IExpression root)
    {
        root = null!;
        if (expr is Number n && n.Value > 0)
        {
            var sqrt = Math.Sqrt((double)n.Value);
            if (Math.Abs(sqrt - Math.Round(sqrt)) < 1e-9)
            {
                root = new Number((decimal)sqrt);
                return true;
            }
            return false;
        }

        if (expr is Power p && p.Exponent is Number exp && exp.Value % 2 == 0)
        {
            root = new Power(p.Base, new Number(exp.Value / 2)).Canonicalize();
            return true;
        }
        
        return false;
    }

    private static bool IsNegativeSquare(IExpression expr, out IExpression root)
    {
        root = null!;
        // Check for -1 * square
        if (expr is Number n && n.Value < 0)
        {
            var val = -n.Value;
            var sqrt = Math.Sqrt((double)val);
            if (Math.Abs(sqrt - Math.Round(sqrt)) < 1e-9)
            {
                root = new Number((decimal)sqrt);
                return true;
            }
            return false;
        }

        if (expr is Multiply m && m.Arguments.Count == 2)
        {
            // Look for -1 * Square
            if (m.Arguments[0] is Number num && num.Value == -1m)
            {
                return IsPositiveSquare(m.Arguments[1], out root);
            }
            // Or Square * -1
            if (m.Arguments[1] is Number num2 && num2.Value == -1m)
            {
                return IsPositiveSquare(m.Arguments[0], out root);
            }
        }

        return false;
    }

    private static SolveResult? TryCommonFactor(Add add, SolveContext context)
    {
        var terms = add.Arguments;
        if (terms.Count < 2) return null;

        var factorLists = terms
            .Select(t => t is Multiply m ? m.Arguments : ImmutableList.Create(t))
            .ToList();

        // 1. Identify common symbolic factors
        ImmutableList<IExpression> common = factorLists.First().Where(f => f is not Number).ToImmutableList();
        foreach (var list in factorLists.Skip(1))
        {
            context.ThrowIfCancellationRequested();
            var newCommon = new List<IExpression>();
            var listMutable = list.ToList();
            foreach (var item in common)
            {
                var idx = listMutable.FindIndex(x => x.InternalEquals(item));
                if (idx >= 0)
                {
                    newCommon.Add(item);
                    listMutable.RemoveAt(idx);
                }
            }
            common = newCommon.ToImmutableList();
            if (common.Count == 0) break;
        }

        // 2. Identify common numeric GCD
        decimal numericGcd = 0;
        bool first = true;
        foreach (var list in factorLists)
        {
            var nums = list.OfType<Number>().Select(n => n.Value).ToList();
            decimal termConst = nums.Count == 0 ? 1m : nums.Aggregate(1m, (a, b) => a * b);
            if (first) { numericGcd = Math.Abs(termConst); first = false; }
            else numericGcd = Gcd(numericGcd, Math.Abs(termConst));
        }

        if (common.Count == 0 && (numericGcd <= 1 || Math.Abs(numericGcd - Math.Round(numericGcd)) > 1e-9m)) return null;

        var finalCommonFactors = new List<IExpression>();
        if (numericGcd > 1 && Math.Abs(numericGcd - Math.Round(numericGcd)) < 1e-9m)
        {
            finalCommonFactors.Add(new Number(Math.Round(numericGcd)));
        }
        finalCommonFactors.AddRange(common);

        IExpression commonFactor = finalCommonFactors.Count == 1 ? finalCommonFactors[0] : new Multiply(finalCommonFactors.ToImmutableList()).Canonicalize();

        var remainderTerms = terms.Select(t =>
        {
            var factors = (t is Multiply m ? m.Arguments : ImmutableList.Create(t)).ToList();
            
            // Remove symbolic common factors
            foreach (var c in common)
            {
                int idx = factors.FindIndex(f => f.InternalEquals(c));
                if (idx >= 0) factors.RemoveAt(idx);
            }

            // Divide by numeric GCD
            if (numericGcd > 1)
            {
                var nums = factors.OfType<Number>().ToList();
                decimal termConst = nums.Count == 0 ? 1m : nums.Aggregate(1m, (acc, n) => acc * n.Value);
                foreach (var n in nums) factors.Remove(n);
                factors.Insert(0, new Number(termConst / numericGcd));
            }

            if (factors.Count == 0) return (IExpression)new Number(1m);
            if (factors.Count == 1) return factors[0];
            return new Multiply(factors.ToImmutableList()).Canonicalize();
        }).ToImmutableList();

        var result = new Multiply(commonFactor, new Add(remainderTerms).Canonicalize()).Canonicalize();
        var trace = context.EnableTracing ? ImmutableList.Create(add, result) : null;
        var message = result.InternalEquals(add) ? "No changes performed." : "Factorization applied.";
        return SolveResult.Success(result, message, trace);
    }

    private static decimal Gcd(decimal a, decimal b)
    {
        while (Math.Abs(b) > 1e-9m)
        {
            a %= b;
            var t = a;
            a = b;
            b = t;
        }
        return a;
    }

    private static SolveResult? TryPolynomialGcdFactor(Add add, SolveContext context)
    {
        var variable = context.TargetVariable ?? FindFirstSymbol(add);
        if (variable is null) return null;

        var polys = new List<Polynomial>();
        foreach (var term in add.Arguments)
        {
            if (!Polynomial.TryCreate(term, variable, out var p))
            {
                return null;
            }
            polys.Add(p);
        }

        if (polys.Count == 0) return null;

        var gcd = polys[0];
        for (int i = 1; i < polys.Count && !gcd.IsZero; i++)
        {
            context.ThrowIfCancellationRequested();
            gcd = Polynomial.Gcd(gcd, polys[i], context.CancellationToken);
        }

        // Nothing common beyond a constant.
        if (gcd.IsZero || (gcd.Degree == 0 && gcd.Coefficients[0].IsOne))
        {
            return null;
        }

        var remainderTerms = new List<IExpression>();
        foreach (var p in polys)
        {
            var (q, r) = p.DivideWithRemainder(gcd);
            if (!r.IsZero)
            {
                // Guard: unexpected remainder means bail out to keep stability.
                return null;
            }
            remainderTerms.Add(q.ToExpression(variable));
        }

        var factorExpr = gcd.ToExpression(variable);
        var remainderExpr = remainderTerms.Count switch
        {
            0 => new Number(0m),
            1 => remainderTerms[0].Canonicalize(),
            _ => new Add(remainderTerms.OrderBy(t => t.ToDisplayString()).ToImmutableList()).Canonicalize()
        };

        var factored = new Multiply(factorExpr, remainderExpr).Canonicalize();
        var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(add, factored) : null;
        var message = factored.InternalEquals(add) ? "No changes performed." : "Polynomial GCD factored.";
        return SolveResult.Success(factored, message, trace);
    }

    private static SolveResult? TryLinearFactorization(IExpression expr, SolveContext context)
    {
        var variable = context.TargetVariable;
        if (variable != null && !expr.ContainsSymbol(variable))
        {
            variable = null;
        }
        variable ??= FindFirstSymbol(expr);

        if (variable is null) return null;

        if (!Polynomial.TryCreate(expr, variable, out var polynomial))
        {
            return null;
        }

        if (polynomial.Degree < 2) return null;
        if (polynomial.Degree > 8) return null;

        var factorization = polynomial.FactorLinear(context.CancellationToken);
        var factoredExpr = factorization.ToExpression(variable).Canonicalize();
        if (factoredExpr.InternalEquals(expr)) return null;

        var trace = context.EnableTracing ? ImmutableList.Create(expr, factoredExpr) : null;
        return SolveResult.Success(factoredExpr, "Linear factorization applied.", trace);
    }

    private static SolveResult? TryMultivariateFactor(Add add, SolveContext context)
    {
        var monos = new List<MultivariateMonomial>();
        foreach (var term in add.Arguments)
        {
            if (!MultivariateMonomial.TryParse(term, out var mono))
            {
                return null;
            }
            monos.Add(mono);
        }

        if (monos.Count == 0) return null;

        var gcd = monos[0];
        for (int i = 1; i < monos.Count && !gcd.Coefficient.IsZero; i++)
        {
            context.ThrowIfCancellationRequested();
            gcd = MultivariateMonomial.Gcd(gcd, monos[i]);
        }

        // Skip if gcd is trivial (1).
        if (gcd.Coefficient.IsOne && gcd.Exponents.Count == 0)
        {
            return null;
        }

        var remainderTerms = new List<IExpression>();
        foreach (var m in monos)
        {
            var quotient = m.Divide(gcd);
            if (quotient.Coefficient.IsZero)
            {
                return null;
            }
            remainderTerms.Add(quotient.ToExpression());
        }

        var factorExpr = gcd.ToExpression();
        var remainderExpr = remainderTerms.Count switch
        {
            0 => new Number(0m),
            1 => remainderTerms[0].Canonicalize(),
            _ => new Add(remainderTerms.OrderBy(t => t.ToDisplayString()).ToImmutableList()).Canonicalize()
        };

        var factored = new Multiply(factorExpr, remainderExpr).Canonicalize();
        var trace = context.EnableTracing ? ImmutableList.Create<IExpression>(add, factored) : null;
        var message = factored.InternalEquals(add) ? "No changes performed." : "Multivariate monomial factorization applied.";
        return SolveResult.Success(factored, message, trace);
    }

    private static Symbol? FindFirstSymbol(IExpression expr)
    {
        if (expr is Symbol s) return s;
        if (expr is Operation op)
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
