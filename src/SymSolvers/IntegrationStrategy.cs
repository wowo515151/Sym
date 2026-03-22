using System;
using System.Collections.Immutable;
using System.Linq;
using Sym.Algebra;
using Sym.Atoms;
using Sym.Calculus;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.Operations;

namespace SymSolvers;

public class IntegrationStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "IntegrationStrategy";
    private readonly RulePackStrategy _rulePackStrategy;

    public IntegrationStrategy()
    {
        var packs = SymRules.RulePackLibrary.GetRulePacks();
        var pack = packs.FirstOrDefault(p => p.Name == "IntegrationStrategy");
        if (pack != null) _rulePackStrategy = new RulePackStrategy(pack);
        else _rulePackStrategy = null!;
    }

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");

        IExpression current = problem;

        // 1. Try rules first
        if (_rulePackStrategy != null)
        {
            var res = _rulePackStrategy.Solve(current, context);
            if (res.IsSuccess && res.ResultExpression != null && !res.ResultExpression.InternalEquals(current))
            {
                current = res.ResultExpression;
                // If it's no longer an Integral, we might be done
                if (!current.ContainsSymbol(s => s.Name == "Integral"))
                {
                     // check if it still has Integrals via ContainsOperation or similar
                }
            }
        }

        if (current is DefiniteIntegral definite)
        {
            return SolveDefinite(definite, context);
        }

        var integral = problem as Integral;
        if (integral is null)
        {
            var rules = CalculusRules.IntegrationRules.AddRange(AlgebraicSimplificationRules.SimplificationRules);
            var res = Rewriter.RewriteFully(problem, rules, 50, context.Assumptions);
            if (res.RewrittenExpression is Integral rewritten)
            {
                integral = rewritten;
            }
            else
            {
                return SolveResult.Failure(res.RewrittenExpression, "IntegrationStrategy requires an Integral or DefiniteIntegral expression.");
            }
        }

        return SolveIntegral(integral, context);
    }

    private SolveResult SolveIntegral(Integral integral, SolveContext context)
    {
        var variable = integral.Variable as Symbol;
        if (variable is null) return SolveResult.Failure(integral, "Integration variable must be a symbol.");

        IExpression target = integral.TargetExpression.Canonicalize();

        var integrated = Integrate(target, variable, context);
        if (integrated is null) return SolveResult.Failure(integral, "Unable to integrate expression with available rules.");
        return SolveResult.Success(integrated.Canonicalize(), "Integration completed.", context.EnableTracing ? ImmutableList.Create<IExpression>(integral, integrated) : null);
    }

    private SolveResult SolveDefinite(DefiniteIntegral definite, SolveContext context)
    {
        var target = definite.TargetExpression.Canonicalize();
        var antiderivative = Integrate(target, definite.Variable, context);
        if (antiderivative is null)
        {
            return SolveResult.Failure(definite, "Unable to integrate expression with available rules.");
        }

        if (definite.LowerBound is null || definite.UpperBound is null)
        {
            return SolveResult.Failure(definite, "DefiniteIntegral requires lower and upper bounds.");
        }

        var poles = FindPoles(target, definite.Variable, definite.LowerBound, definite.UpperBound);
        if (poles.Count > 0)
        {
            var trace = context.EnableTracing
                ? ImmutableList.Create<IExpression>(definite, antiderivative).AddRange(poles)
                : null;
            return SolveResult.Failure(definite, "Improper definite integral (discontinuity within bounds) is not supported.", trace);
        }

        var upperVal = SubstituteScalar(antiderivative, definite.Variable, definite.UpperBound);
        var lowerVal = SubstituteScalar(antiderivative, definite.Variable, definite.LowerBound);
        var value = new Add(upperVal, new Multiply(new Number(-1m), lowerVal).Canonicalize()).Canonicalize();

        return SolveResult.Success(value, "Definite integral evaluated.",
            context.EnableTracing ? ImmutableList.Create<IExpression>(definite, antiderivative, value) : null);
    }

    private static IExpression SubstituteScalar(IExpression expr, Symbol variable, IExpression bound)
    {
        if (expr is Symbol s && s.InternalEquals(variable)) return bound;
        if (expr is Operation op)
        {
            var args = op.Arguments.Select(a => SubstituteScalar(a, variable, bound)).ToImmutableList();
            return op.WithArguments(args).Canonicalize();
        }
        return expr;
    }

    private static IExpression? Integrate(IExpression target, Symbol variable, SolveContext context, bool allowPartialFractions = true, int depth = 0)
    {
        if (depth > 8) return null;

        // Constants
        if (!target.ContainsSymbol(variable))
        {
            return new Multiply(target, variable).Canonicalize();
        }

        if (target is Number numConst)
        {
            return new Multiply(numConst, variable).Canonicalize();
        }

        if (target is Symbol s && s.InternalEquals(variable))
        {
            return new Divide(new Power(s, new Number(2m)).Canonicalize(), new Number(2m)).Canonicalize();
        }

        if (target is Power pow && pow.Base is Symbol sym && sym.InternalEquals(variable) && pow.Exponent is Number nExp && nExp.Value != -1m)
        {
            var newExp = new Number(nExp.Value + 1m);
            return new Divide(new Power(sym, newExp).Canonicalize(), newExp).Canonicalize();
        }

        // Integral 1/(a*x + b) dx = (1/a) * log(a*x + b)
        if (TryGetReciprocalDenominatorPow1(target, out var linDen) &&
            TryLinear(linDen, variable, out var linA, out _) && linA != 0m)
        {
            return new Divide(new Function("log", ImmutableList.Create<IExpression>(linDen)).Canonicalize(), new Number(linA)).Canonicalize();
        }

        // Integral sqrt(a^2 - x^2) dx
        if (target is Power sqrt && sqrt.Exponent is Number half && half.Value == 0.5m && sqrt.Base is Add sum && TryMatchA2MinusX2(sum, variable, out var aSq))
        {
            var sqrtExpr = new Power(sum, new Number(0.5m)).Canonicalize();
            var term1 = new Multiply(new Number(0.5m), new Multiply(variable, sqrtExpr).Canonicalize()).Canonicalize();
            var term2 = new Multiply(new Number(0.5m * aSq), new Function("asin", ImmutableList.Create<IExpression>(new Divide(variable, new Number((decimal)Math.Sqrt((double)aSq))).Canonicalize())).Canonicalize()).Canonicalize();
            return new Add(term1, term2).Canonicalize();
        }

        if (TryIntegrateInverseForms(target, variable, out var inverseResult))
        {
            return inverseResult;
        }

        // Integral 1/(a^2 + x^2) dx = (1/a) atan(x/a)
        if (TryGetReciprocalDenominator(target, out var denom) && denom is Add addDen && addDen.Arguments.Count == 2)
        {
            Power? x2 = null;
            Power? a2 = null;
            Number? aSquared = null;

            foreach (var term in addDen.Arguments)
            {
                if (term is Power p && p.Exponent is Number e && e.Value == 2m)
                {
                    if (p.Base.InternalEquals(variable))
                    {
                        x2 = p;
                        continue;
                    }

                    if (p.Base is Number)
                    {
                        a2 = p;
                        continue;
                    }
                }

                if (term is Number c && c.Value > 0m)
                {
                    aSquared = c;
                    continue;
                }
            }

            if (x2 is not null && (a2 is not null || aSquared is not null))
            {
                decimal a;
                if (a2 is not null && a2.Base is Number aNum && aNum.Value > 0m)
                {
                    a = aNum.Value;
                }
                else
                {
                    var c = aSquared!.Value;
                    var asInt = (int)c;
                    if (c == asInt && asInt > 0)
                    {
                        var r = (int)Math.Round(Math.Sqrt(asInt));
                        a = r * r == asInt ? r : (decimal)Math.Sqrt((double)c);
                    }
                    else
                    {
                        a = (decimal)Math.Sqrt((double)c);
                    }
                }

                return new Divide(
                    new Function("atan", ImmutableList.Create<IExpression>(new Divide(variable, new Number(a)).Canonicalize())).Canonicalize(),
                    new Number(a)).Canonicalize();
            }
        }

        if (target is Function fn && fn.Arguments.Count == 1)
        {
            var arg = fn.Arguments[0].Canonicalize();
            var name = fn.Name.ToLowerInvariant();
            decimal a;
            decimal b;
            if (!arg.ContainsSymbol(variable))
            {
                if (name == "exp")
                {
                    return new Multiply(fn, variable).Canonicalize();
                }

                if (name == "log")
                {
                    return new Add(
                        new Multiply(arg, fn).Canonicalize(),
                        new Multiply(new Number(-1m), arg).Canonicalize()).Canonicalize();
                }
            }

            if (name == "exp" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                return new Divide(new Function("exp", ImmutableList.Create(arg)).Canonicalize(), new Number(a)).Canonicalize();
            }

            if (name == "log" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                var scaled = new Divide(arg, new Number(a)).Canonicalize();
                var adjustment = new Add(fn, new Number(-1m)).Canonicalize();
                return new Multiply(scaled, adjustment).Canonicalize();
            }

            if (name == "sin" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                return new Divide(new Multiply(new Number(-1m / a), new Function("cos", ImmutableList.Create(arg)).Canonicalize()).Canonicalize(), new Number(1m)).Canonicalize();
            }

            if (name == "cos" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                return new Divide(new Function("sin", ImmutableList.Create(arg)).Canonicalize(), new Number(a)).Canonicalize();
            }

            if (name == "tan" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                var u = new Function("log", ImmutableList.Create(new Function("cos", ImmutableList.Create(arg)).Canonicalize())).Canonicalize();
                return new Divide(new Multiply(new Number(-1m), u).Canonicalize(), new Number(a)).Canonicalize();
            }

            if (name == "asin" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                var sqrtTerm = new Power(new Add(new Number(1m), new Multiply(new Number(-1m), new Power(arg, new Number(2m))).Canonicalize()).Canonicalize(), new Number(0.5m)).Canonicalize();
                var numer = new Add(new Multiply(arg, new Function("asin", ImmutableList.Create(arg)).Canonicalize()).Canonicalize(), sqrtTerm).Canonicalize();
                return new Divide(numer, new Number(a)).Canonicalize();
            }

            if (name == "sinh" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                return new Divide(new Function("cosh", ImmutableList.Create(arg)).Canonicalize(), new Number(a)).Canonicalize();
            }

            if (name == "cosh" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                return new Divide(new Function("sinh", ImmutableList.Create(arg)).Canonicalize(), new Number(a)).Canonicalize();
            }

            if (name == "tanh" && TryLinear(arg, variable, out a, out b) && a != 0m)
            {
                var coshArg = new Function("cosh", ImmutableList.Create(arg)).Canonicalize();
                return new Divide(new Function("log", ImmutableList.Create<IExpression>(coshArg)).Canonicalize(), new Number(a)).Canonicalize();
            }
        }

        // Power-reduction for sin^2(ax) / cos^2(ax) when a is 1 and argument is the integration variable.
        if (target is Power trigPow &&
            trigPow.Base is Function trigFn &&
            trigFn.Arguments.Count == 1 &&
            trigPow.Exponent is Number trigExp &&
            trigExp.Value == 2m &&
            trigFn.Arguments[0] is Symbol argSym &&
            argSym.InternalEquals(variable))
        {
            var doubleArg = new Multiply(new Number(2m), variable).Canonicalize();
            if (string.Equals(trigFn.Name, "sin", StringComparison.OrdinalIgnoreCase))
            {
                // ∫sin^2(x) dx = x/2 - sin(2x)/4
                return new Add(
                    new Divide(variable, new Number(2m)).Canonicalize(),
                    new Multiply(new Number(-0.25m), new Function("sin", ImmutableList.Create<IExpression>(doubleArg)).Canonicalize()).Canonicalize()
                ).Canonicalize();
            }
            if (string.Equals(trigFn.Name, "cos", StringComparison.OrdinalIgnoreCase))
            {
                // ∫cos^2(x) dx = x/2 + sin(2x)/4
                return new Add(
                    new Divide(variable, new Number(2m)).Canonicalize(),
                    new Multiply(new Number(0.25m), new Function("sin", ImmutableList.Create<IExpression>(doubleArg)).Canonicalize()).Canonicalize()
                ).Canonicalize();
            }
        }

        if (target is Add add)
        {
            var parts = add.Arguments.Select(p => Integrate(p, variable, context, allowPartialFractions, depth + 1)).ToImmutableList();
            if (parts.Any(p => p is null)) return null;
            return new Add(parts!.Cast<IExpression>().ToImmutableList()).Canonicalize();
        }

        if (target is Multiply mult)
        {
            // Constant multiple pull-out
            var constFactor = mult.Arguments.FirstOrDefault(a => !a.ContainsSymbol(variable));
            if (constFactor is not null)
            {
                var remaining = mult.Arguments.Remove(constFactor);
                var inner = remaining.Count == 1 ? remaining[0] : new Multiply(remaining).Canonicalize();
                var innerInt = Integrate(inner, variable, context, allowPartialFractions, depth + 1);
                if (innerInt is null) return null;
                return new Multiply(constFactor, innerInt).Canonicalize();
            }

            // Integration by parts for poly * trig/exp/log
            var poly = mult.Arguments.FirstOrDefault(a => IsPolynomialIn(a, variable));
            if (poly is not null)
            {
                var remaining = mult.Arguments.Remove(poly);
                if (remaining.Count > 0)
                {
                    var other = remaining.Count == 1 ? remaining[0] : new Multiply(remaining).Canonicalize();
                    var polyPrime = DerivativeReducer(poly, variable); // u'
                    var v = Integrate(other, variable, context, allowPartialFractions, depth + 1);
                    if (v is not null && polyPrime is not null)
                    {
                        var uv = new Multiply(poly, v).Canonicalize();
                        var subIntegral = Integrate(new Multiply(polyPrime, v).Canonicalize(), variable, context, allowPartialFractions, depth + 1);
                        if (subIntegral is not null)
                        {
                            return new Add(uv, new Multiply(new Number(-1m), subIntegral).Canonicalize()).Canonicalize();
                        }
                    }
                }
            }

            // u-sub pattern: f'(g(x)) * h(g(x))
            var derivativeFactor = mult.Arguments.FirstOrDefault(a => a is Derivative);
            if (derivativeFactor is Derivative d && d.Variable is Symbol dv)
            {
                var inner = d.TargetExpression;
                if (dv.InternalEquals(variable))
                {
                    var remaining = mult.Arguments.Remove(derivativeFactor);
                    var h = remaining.Count == 1 ? remaining[0] : new Multiply(remaining).Canonicalize();
                    if (h.ContainsSymbol(inner as Symbol ?? variable))
                    {
                        var substituted = SubstituteScalar(h, variable, inner);
                        var integrated = Integrate(substituted, inner as Symbol ?? variable, context, allowPartialFractions, depth + 1);
                        if (integrated is not null)
                        {
                            return integrated;
                        }
                    }
                }
            }

            if (TryIntegrateExpTrigProduct(mult, variable, out var expTrigResult))
            {
                return expTrigResult;
            }
        }

        if (target is Divide div && div.Denominator is Power powDen && powDen.Exponent is Number denExp && denExp.Value == 1m)
        {
            // Integral f'(x)/f(x) dx = log|f(x)|
            if (DerivativeMatches(div.Numerator, powDen.Base, variable))
            {
                return new Function("log", ImmutableList.Create(powDen.Base)).Canonicalize();
            }
        }

        if (target is Divide divGeneric)
        {
            // Integral f'(x)/f(x) dx = log(f(x))
            if (DerivativeMatches(divGeneric.Numerator, divGeneric.Denominator, variable))
            {
                return new Function("log", ImmutableList.Create(divGeneric.Denominator)).Canonicalize();
            }
        }

        if (allowPartialFractions && TryIntegrateViaPartialFractions(target, variable, context, out var pfResult))
        {
            return pfResult;
        }

        // Fallback to rule-based integration
        var rules = CalculusRules.IntegrationRules.AddRange(AlgebraicSimplificationRules.SimplificationRules);
        var rewrite = Rewriter.RewriteFully(new Integral(target, variable), rules, 50, context.Assumptions);
        if (rewrite.RewrittenExpression is Integral)
        {
            return null;
        }

        return rewrite.RewrittenExpression;
    }

    private static bool TryMatchA2MinusX2(Add expr, Symbol variable, out decimal aSquared)
    {
        aSquared = 0m;
        Number? a2 = null;
        bool hasNegativeX2 = false;
        foreach (var term in expr.Arguments)
        {
            if (term is Number n) a2 = n;
            if (term is Multiply m && m.Arguments.Count == 2 && m.Arguments[0] is Number n1 && n1.Value == -1m && m.Arguments[1] is Power p && p.Base.InternalEquals(variable) && p.Exponent is Number e && e.Value == 2m)
            {
                hasNegativeX2 = true;
            }
        }
        if (hasNegativeX2 && a2 is not null && a2.Value > 0m)
        {
            aSquared = a2.Value;
            return true;
        }
        return false;
    }

    private static bool TryGetReciprocalDenominator(IExpression expr, out IExpression denominator)
    {
        denominator = expr;

        if (expr is Divide d && d.Numerator is Number n && n.Value == 1m)
        {
            denominator = d.Denominator;
            return true;
        }

        if (expr is Power p && p.Exponent is Number e && e.Value < 0m)
        {
            var pos = -e.Value;
            denominator = pos == 1m ? p.Base : new Power(p.Base, new Number(pos)).Canonicalize();
            return true;
        }

        if (expr is Multiply m)
        {
            Power? reciprocal = null;
            decimal coeff = 1m;
            foreach (var arg in m.Arguments)
            {
                if (arg is Number num)
                {
                    coeff *= num.Value;
                    continue;
                }

                if (arg is Power pow && pow.Exponent is Number ne && ne.Value < 0m)
                {
                    if (reciprocal is not null) return false;
                    reciprocal = pow;
                    continue;
                }

                return false;
            }

            if (reciprocal is not null && coeff == 1m)
            {
                var ne = (Number)reciprocal.Exponent;
                var pos = -ne.Value;
                denominator = pos == 1m ? reciprocal.Base : new Power(reciprocal.Base, new Number(pos)).Canonicalize();
                return true;
            }
        }

        return false;
    }

    private static bool TryGetReciprocalDenominatorPow1(IExpression expr, out IExpression denominator)
    {
        denominator = expr;

        if (expr is Divide d && d.Numerator is Number n && n.Value == 1m)
        {
            denominator = d.Denominator;
            return true;
        }

        if (expr is Power p && p.Exponent is Number e && e.Value == -1m)
        {
            denominator = p.Base;
            return true;
        }

        if (expr is Multiply m)
        {
            Power? reciprocal = null;
            decimal coeff = 1m;
            foreach (var arg in m.Arguments)
            {
                if (arg is Number num)
                {
                    coeff *= num.Value;
                    continue;
                }

                if (arg is Power pow && pow.Exponent is Number ne && ne.Value == -1m)
                {
                    if (reciprocal is not null) return false;
                    reciprocal = pow;
                    continue;
                }

                return false;
            }

            if (reciprocal is not null && coeff == 1m)
            {
                denominator = reciprocal.Base;
                return true;
            }
        }

        return false;
    }

    private static bool TryLinear(IExpression expr, Symbol variable, out decimal a, out decimal b)
    {
        a = 0m; b = 0m;
        if (expr is Add add)
        {
            decimal accConst = 0m; decimal accCoeff = 0m;
            foreach (var arg in add.Arguments)
            {
                if (arg is Number n) { accConst += n.Value; continue; }
                if (arg is Multiply m && m.Arguments.Count == 2 && m.Arguments[0] is Number k && m.Arguments[1].InternalEquals(variable))
                {
                    accCoeff += k.Value; continue;
                }
                if (arg.InternalEquals(variable))
                {
                    accCoeff += 1m; continue;
                }
                return false;
            }
            a = accCoeff; b = accConst; return true;
        }
        if (expr.InternalEquals(variable))
        {
            a = 1m; b = 0m; return true;
        }
        if (expr is Multiply mul && mul.Arguments.Count == 2 && mul.Arguments[0] is Number n0 && mul.Arguments[1].InternalEquals(variable))
        {
            a = n0.Value; b = 0m; return true;
        }
        if (expr is Number num)
        {
            a = 0m; b = num.Value; return true;
        }
        return false;
    }

    private static bool IsPolynomialIn(IExpression expr, Symbol variable)
    {
        return Polynomial.TryCreate(expr, variable, out _);
    }

    private static IExpression? DerivativeReducer(IExpression expr, Symbol variable)
    {
        var d = CalculusHelper.DifferentiateExpression(expr, variable);
        return d;
    }

    private static bool DerivativeMatches(IExpression candidate, IExpression of, Symbol variable)
    {
        var d = CalculusHelper.DifferentiateExpression(of, variable);
        return candidate.InternalEquals(d);
    }

    private static List<IExpression> FindPoles(IExpression expr, Symbol variable, IExpression lower, IExpression upper)
    {
        var poles = new List<IExpression>();
        if (lower is not Number l || upper is not Number u) return poles;

        var lo = Math.Min(l.Value, u.Value);
        var hi = Math.Max(l.Value, u.Value);

        IExpression? denominator = null;
        switch (expr)
        {
            case Divide d:
                denominator = d.Denominator;
                break;
            case Power p when p.Exponent is Number n && n.Value == -1m:
                denominator = p.Base;
                break;
            case Multiply m:
            {
                Power? reciprocal = null;
                foreach (var arg in m.Arguments)
                {
                    if (arg is Number) continue;
                    if (arg is Power pow && pow.Exponent is Number ne && ne.Value == -1m)
                    {
                        if (reciprocal is not null) { reciprocal = null; break; }
                        reciprocal = pow;
                        continue;
                    }
                    reciprocal = null;
                    break;
                }
                denominator = reciprocal?.Base;
                break;
            }
        }

        if (denominator is null) return poles;

        if (TryLinear(denominator, variable, out var a, out var b) && a != 0m)
        {
            var pole = -b / a;
            if (pole >= lo && pole <= hi)
            {
                poles.Add(new Number(pole));
            }
        }
        return poles;
    }

    private static bool TryIntegrateInverseForms(IExpression target, Symbol variable, out IExpression result)
    {
        result = null!;

        static bool MatchesSqrt(IExpression expr, out IExpression radicand)
        {
            radicand = expr;
            if (expr is Function fn && fn.Name.Equals("sqrt", StringComparison.OrdinalIgnoreCase) && fn.Arguments.Count == 1)
            {
                radicand = fn.Arguments[0];
                return true;
            }

            if (expr is Power p && p.Exponent is Number n && n.Value == 0.5m)
            {
                radicand = p.Base;
                return true;
            }

            return false;
        }

        if (TryGetReciprocalDenominator(target, out var denom))
        {
            if (MatchesSqrt(denom, out var radicand))
            {
                if (TryMatchQuadratic(radicand, variable, out var linear, out var constant, out var sign))
                {
                    if (!TryLinear(linear, variable, out var linCoeff, out _ ) || linCoeff == 0m) return false;
                    var scale = (decimal)Math.Sqrt((double)constant);
                    var normalized = new Divide(linear, new Number(scale)).Canonicalize();

                    if (sign == QuadraticSign.ConstantMinusSquare)
                    {
                        result = new Divide(new Function("asin", ImmutableList.Create<IExpression>(normalized)).Canonicalize(), new Number(linCoeff)).Canonicalize();
                        return true;
                    }

                    if (sign == QuadraticSign.ConstantPlusSquare)
                    {
                        result = new Divide(new Function("asinh", ImmutableList.Create<IExpression>(normalized)).Canonicalize(), new Number(linCoeff)).Canonicalize();
                        return true;
                    }

                    if (sign == QuadraticSign.SquareMinusConstant)
                    {
                        result = new Divide(new Function("acosh", ImmutableList.Create<IExpression>(normalized)).Canonicalize(), new Number(linCoeff)).Canonicalize();
                        return true;
                    }
                }
            }
            else if (TryMatchQuadratic(denom, variable, out var lin, out var constTerm, out var quadSign))
            {
                if (!TryLinear(lin, variable, out var linCoeff, out _ ) || linCoeff == 0m) return false;
                var scale = (decimal)Math.Sqrt((double)constTerm);
                var normalized = new Divide(lin, new Number(scale)).Canonicalize();

                if (quadSign == QuadraticSign.ConstantPlusSquare)
                {
                    result = new Divide(new Function("atan", ImmutableList.Create<IExpression>(normalized)).Canonicalize(), new Number(linCoeff)).Canonicalize();
                    return true;
                }

                if (quadSign == QuadraticSign.ConstantMinusSquare)
                {
                    result = new Divide(new Function("atanh", ImmutableList.Create<IExpression>(normalized)).Canonicalize(), new Number(linCoeff)).Canonicalize();
                    return true;
                }
            }
        }

        return false;
    }

    private enum QuadraticSign
    {
        ConstantPlusSquare,
        ConstantMinusSquare,
        SquareMinusConstant
    }

    private static bool TryMatchQuadratic(IExpression expr, Symbol variable, out IExpression linear, out decimal constant, out QuadraticSign sign)
    {
        linear = null!;
        constant = 0m;
        sign = QuadraticSign.ConstantPlusSquare;

        if (expr is not Add add || add.Arguments.Count < 2) return false;

        Power? square = null;
        Number? positiveConst = null;
        Number? negativeConst = null;
        bool squareNegated = false;

        foreach (var term in add.Arguments)
        {
            if (term is Power p && p.Exponent is Number exp && exp.Value == 2m && TryLinear(p.Base, variable, out _, out _))
            {
                square = p;
                continue;
            }

            if (term is Multiply m && m.Arguments.Count == 2 && m.Arguments[0] is Number n && n.Value == -1m && m.Arguments[1] is Power mp && mp.Exponent is Number me && me.Value == 2m && TryLinear(mp.Base, variable, out _, out _))
            {
                square = mp;
                squareNegated = true;
                continue;
            }

            if (term is Number nTerm)
            {
                if (nTerm.Value >= 0m)
                {
                    positiveConst = nTerm;
                }
                else
                {
                    negativeConst = nTerm;
                }
            }
        }

        if (square is null) return false;
        if (positiveConst is null && negativeConst is null) return false;

        linear = square.Base;
        constant = positiveConst?.Value ?? Math.Abs(negativeConst!.Value);

        if (squareNegated && positiveConst is not null)
        {
            sign = QuadraticSign.ConstantMinusSquare;
            return true;
        }

        if (!squareNegated && negativeConst is not null)
        {
            sign = QuadraticSign.SquareMinusConstant;
            return true;
        }

        sign = QuadraticSign.ConstantPlusSquare;
        return true;
    }

    private static bool TryIntegrateExpTrigProduct(Multiply mult, Symbol variable, out IExpression result)
    {
        result = null!;

        var expFactor = mult.Arguments.FirstOrDefault(a => a is Function f && f.Name.Equals("exp", StringComparison.OrdinalIgnoreCase)) as Function;
        var trigFactor = mult.Arguments.FirstOrDefault(a =>
            a is Function f &&
            (f.Name.Equals("sin", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("cos", StringComparison.OrdinalIgnoreCase))) as Function;

        if (expFactor is null || trigFactor is null) return false;
        if (expFactor.Arguments.Count != 1 || trigFactor.Arguments.Count != 1) return false;

        if (!TryLinear(expFactor.Arguments[0], variable, out var a, out _ ) || a == 0m) return false;
        if (!TryLinear(trigFactor.Arguments[0], variable, out var b, out _ ) || b == 0m) return false;

        var denom = a * a + b * b;
        if (denom == 0m) return false;

        var expTerm = new Function("exp", ImmutableList.Create(expFactor.Arguments[0])).Canonicalize();
        IExpression combined;
        if (trigFactor.Name.Equals("sin", StringComparison.OrdinalIgnoreCase))
        {
            combined = new Add(
                new Multiply(new Number(a), new Function("sin", ImmutableList.Create(trigFactor.Arguments[0])).Canonicalize()).Canonicalize(),
                new Multiply(new Number(-b), new Function("cos", ImmutableList.Create(trigFactor.Arguments[0])).Canonicalize()).Canonicalize()
            ).Canonicalize();
        }
        else
        {
            combined = new Add(
                new Multiply(new Number(a), new Function("cos", ImmutableList.Create(trigFactor.Arguments[0])).Canonicalize()).Canonicalize(),
                new Multiply(new Number(b), new Function("sin", ImmutableList.Create(trigFactor.Arguments[0])).Canonicalize()).Canonicalize()
            ).Canonicalize();
        }

        result = new Divide(new Multiply(expTerm, combined).Canonicalize(), new Number(denom)).Canonicalize();
        return true;
    }

    private static bool TryIntegrateViaPartialFractions(IExpression target, Symbol variable, SolveContext context, out IExpression result)
    {
        result = null!;

        bool MaybeRational(IExpression expr)
        {
            if (expr is Divide) return true;
            if (expr is Power p && p.Exponent is Number n && n.Value < 0m) return true;
            if (expr is Multiply m && m.Arguments.Any(a => a is Power pp && pp.Exponent is Number nn && nn.Value < 0m)) return true;
            return false;
        }

        if (!MaybeRational(target)) return false;

        var pfContext = new SolveContext(variable, context.Rules, context.MaxIterations, context.EnableTracing, context.AdditionalData, context.CancellationToken);
        var pf = new PartialFractionStrategy().Solve(target, pfContext);
        if (!pf.IsSuccess || pf.ResultExpression is null || pf.ResultExpression.InternalEquals(target))
        {
            return false;
        }

        var integrated = Integrate(pf.ResultExpression, variable, context, allowPartialFractions: false);
        if (integrated is null) return false;
        result = integrated;
        return true;
    }
}
