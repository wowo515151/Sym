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
/// Solves ordinary differential equations (ODEs) for common forms:
/// separable and linear first-order equations, and constant-coefficient
/// linear equations with exponential or trigonometric solutions.
/// Rejects PDEs and unsupported forms with clear failures.
/// </summary>
public sealed class DifferentialEquationStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "DifferentialEquationStrategy";
    private readonly RulePackStrategy _rulePackStrategy;

    public DifferentialEquationStrategy()
    {
        var packs = SymRules.RulePackLibrary.GetRulePacks();
        var pack = packs.FirstOrDefault(p => p.Name == "DifferentiationStrategy");
        if (pack != null) _rulePackStrategy = new RulePackStrategy(pack);
        else _rulePackStrategy = null!;
    }

    public SolveResult Solve(IExpression? problem, SolveContext context)
    {
        if (context is null) return SolveResult.Failure(problem, "SolveContext cannot be null.");
        if (problem is null) return SolveResult.Failure(null, "Problem expression cannot be null.");
        var currentProblem = problem;
        if (currentProblem is not Equality eq) return SolveResult.Failure(problem, "DifferentialEquationStrategy requires an Equality expression.");

        var analysis = AnalyzeEquation(eq);
        if (!analysis.Success)
        {
            return SolveResult.Failure(problem, analysis.Message);
        }

        var meta = analysis.Metadata!;
        var trace = context.EnableTracing ? ImmutableList.CreateBuilder<IExpression>() : null;
        trace?.Add(eq);

        SolveAttempt? attempt = null;
        if (meta.MaxOrder > 1)
        {
            attempt = SolveConstantCoefficient(meta, eq, context);
        }
        else
        {
            attempt = SolveSeparable(meta, eq, context) ?? SolveLinearFirstOrder(meta, eq, context);
        }

        if (attempt is null)
        {
            return SolveResult.Failure(eq, "Unsupported differential equation form for the current ODE strategy.", trace?.ToImmutable());
        }

        if (!ValidateSolution(eq, meta, attempt.Solution, context, out var validationMessage))
        {
            trace?.Add(attempt.Solution);
            return SolveResult.Failure(attempt.Solution, validationMessage ?? "Candidate solution failed substitution validation.", trace?.ToImmutable());
        }

        trace?.Add(attempt.Solution);
        var finalMessage = $"{attempt.Message} {(validationMessage ?? "Solution validated by substitution.")}".Trim();
        return SolveResult.Success(attempt.Solution, finalMessage, trace?.ToImmutable());
    }

    private sealed record AnalysisResult(bool Success, string Message, DifferentialEquationMetadata? Metadata);
    private sealed record DifferentialEquationMetadata(Symbol IndependentVariable, IExpression Dependent, int MaxOrder);
    private sealed record SolveAttempt(IExpression Solution, string Message);

    private static AnalysisResult AnalyzeEquation(Equality eq)
    {
        Symbol? independent = null;
        IExpression? dependent = null;
        var maxOrder = 0;
        var pdeDetected = false;

        bool VisitDerivative(Derivative deriv)
        {
            if (deriv.Variable is not Symbol varSym)
            {
                return false;
            }

            independent ??= varSym;
            if (!independent.InternalEquals(varSym))
            {
                pdeDetected = true;
            }

            var baseExpr = GetDerivativeBase(deriv);
            if (baseExpr is Function fn && fn.Arguments.Count != 1)
            {
                pdeDetected = true;
            }

            if (baseExpr is not Function && baseExpr is not Symbol)
            {
                return false;
            }

            dependent ??= baseExpr;
            if (!dependent.InternalEquals(baseExpr))
            {
                return false;
            }

            var order = CountOrder(deriv);
            if (order < 1)
            {
                return false;
            }
            maxOrder = Math.Max(maxOrder, order);
            return true;
        }

        bool Traverse(IExpression expr)
        {
            if (expr is Derivative d)
            {
                return VisitDerivative(d);
            }

            if (expr is Operation op)
            {
                foreach (var arg in op.Arguments)
                {
                    if (!Traverse(arg)) return false;
                }
            }
            return true;
        }

        if (!Traverse(eq))
        {
            return new AnalysisResult(false, "Differential equation must reference a single dependent function and variable.", null);
        }

        if (pdeDetected)
        {
            return new AnalysisResult(false, "Partial differential equations are not supported by this solver.", null);
        }

        if (dependent is null || independent is null || maxOrder == 0)
        {
            return new AnalysisResult(false, "No differential operator found to solve.", null);
        }

        return new AnalysisResult(true, string.Empty, new DifferentialEquationMetadata(independent, dependent, maxOrder));
    }

    private static SolveAttempt? SolveSeparable(DifferentialEquationMetadata meta, Equality eq, SolveContext context)
    {
        if (!TryIsolateFirstDerivative(eq, meta, out var rhs))
        {
            return null;
        }

        if (!TryFactorSeparable(rhs, meta, out var xPart, out var yPart))
        {
            return null;
        }

        var ySymbol = new Symbol(GetDependentName(meta.Dependent));
        var yIntegrand = new Divide(new Number(1m), ReplaceDependent(yPart, meta.Dependent, ySymbol)).Canonicalize();
        var xIntegrand = ReplaceDependent(xPart, meta.Dependent, ySymbol);

        var leftInt = IntegrateExpression(yIntegrand, ySymbol, context);
        if (leftInt is null) return null;

        var rightInt = IntegrateExpression(xIntegrand, meta.IndependentVariable, context);
        if (rightInt is null) return null;

        var constant = new Symbol("C1");
        var separatedRaw = new Equality(leftInt, new Add(rightInt, constant).Canonicalize());
        var separated = (separatedRaw.Canonicalize() as Equality) ?? separatedRaw;

        var solved = SolveForDependent(separated, ySymbol, context);
        if (solved is null) return null;

        var solution = SubstituteSymbol(solved, ySymbol, meta.Dependent).Canonicalize();
        return new SolveAttempt(solution, "Solved separable first-order ODE.");
    }

    private static SolveAttempt? SolveLinearFirstOrder(DifferentialEquationMetadata meta, Equality eq, SolveContext context)
    {
        if (!TryExtractLinearFirstOrder(eq, meta, out var a, out var b, out var c))
        {
            return null;
        }

        if (IsZero(a))
        {
            return null;
        }

        var invA = new Power(a, new Number(-1m)).Canonicalize();
        var p = new Multiply(b, invA).Canonicalize();
        var q = new Multiply(new Number(-1m), new Multiply(c, invA).Canonicalize()).Canonicalize();

        var integralP = IntegrateExpression(p, meta.IndependentVariable, context);
        if (integralP is null) return null;

        var integratingFactor = new Function("exp", ImmutableList.Create<IExpression>(integralP)).Canonicalize();
        var muQ = new Multiply(integratingFactor, q).Canonicalize();
        var integralMuQ = IntegrateExpression(muQ, meta.IndependentVariable, context);
        if (integralMuQ is null)
        {
            if (!TryIntegrateMuQWhenQIsMultipleOfP(p, q, integratingFactor, out integralMuQ))
            {
                return null;
            }
        }

        var constant = new Symbol("C1");
        var numerator = new Add(integralMuQ, constant).Canonicalize();
        var yExpr = new Divide(numerator, integratingFactor).Canonicalize();
        var solution = new Equality(meta.Dependent, yExpr).Canonicalize();
        return new SolveAttempt(solution, "Solved linear first-order ODE via integrating factor.");
    }

    private static SolveAttempt? SolveConstantCoefficient(DifferentialEquationMetadata meta, Equality eq, SolveContext context)
    {
        if (!TryExtractConstantCoefficients(eq, meta, out var a2, out var a1, out var a0, out var freeTerm))
        {
            return null;
        }

        if (Math.Abs((double)a2) < 1e-12)
        {
            return null;
        }

        var disc = a1 * a1 - 4m * a2 * a0;
        var twoA = 2m * a2;
        var c1 = new Symbol("C1");
        var c2 = new Symbol("C2");
        IExpression body;

        if (disc > 0m)
        {
            var sqrt = SymCore.NumericConvert.SafeToDecimal(Math.Sqrt((double)disc));
            var r1 = (-a1 + sqrt) / twoA;
            var r2 = (-a1 - sqrt) / twoA;
            body = new Add(
                new Multiply(c1, ExpTerm(r1, meta.IndependentVariable)).Canonicalize(),
                new Multiply(c2, ExpTerm(r2, meta.IndependentVariable)).Canonicalize()).Canonicalize();
        }
        else if (Math.Abs((double)disc) < 1e-12)
        {
            var r = -a1 / twoA;
            var exp = ExpTerm(r, meta.IndependentVariable);
            body = new Multiply(new Add(c1, new Multiply(c2, meta.IndependentVariable).Canonicalize()).Canonicalize(), exp).Canonicalize();
        }
        else
        {
            var sqrt = SymCore.NumericConvert.SafeToDecimal(Math.Sqrt((double)(-disc)));
            var real = -a1 / twoA;
            var imag = sqrt / twoA;
            var exp = ExpTerm(real, meta.IndependentVariable);
            var cos = new Function("cos", ImmutableList.Create<IExpression>(new Multiply(new Number(imag), meta.IndependentVariable).Canonicalize())).Canonicalize();
            var sin = new Function("sin", ImmutableList.Create<IExpression>(new Multiply(new Number(imag), meta.IndependentVariable).Canonicalize())).Canonicalize();
            var inner = new Add(
                new Multiply(c1, cos).Canonicalize(),
                new Multiply(c2, sin).Canonicalize()).Canonicalize();
            body = new Multiply(exp, inner).Canonicalize();
        }

        var message = "Solved constant-coefficient linear ODE.";

        // Add a simple exponential particular solution when RHS is a single exponential term.
        if (!IsZero(freeTerm))
        {
            if (TryBuildExponentialParticular(meta, freeTerm, a2, a1, a0, out var particular))
            {
                body = new Add(body, particular).Canonicalize();
                message = "Solved constant-coefficient linear ODE with exponential forcing.";
            }
            else
            {
                return null;
            }
        }

        var solution = new Equality(meta.Dependent, body).Canonicalize();
        return new SolveAttempt(solution, message);
    }

    private static bool ValidateSolution(Equality original, DifferentialEquationMetadata meta, IExpression candidate, SolveContext context, out string? message)
    {
        message = null;
        IExpression solutionBody = candidate is Equality eq && IsDependentBase(eq.LeftOperand, meta.Dependent)
            ? eq.RightOperand
            : candidate;

        var substitution = new SolutionSubstitutor(meta.Dependent, meta.IndependentVariable, solutionBody);
        var left = substitution.Apply(original.LeftOperand);
        var right = substitution.Apply(original.RightOperand);
        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
        {
            Console.WriteLine($"DEBUG: ValidateSolution after sub - Left: {left.ToDisplayString()}");
            Console.WriteLine($"DEBUG: ValidateSolution after sub - Right: {right.ToDisplayString()}");
        }
        var residual = new Add(left, new Multiply(new Number(-1m), right).Canonicalize()).Canonicalize();

        residual = SimplifyResidual(residual, context);
        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            Console.WriteLine($"DEBUG: ValidateSolution residual: {residual.ToDisplayString()}");
        
        if (residual is Equality)
        {
            // If residual is still an equality (e.g. x = 2), it means we couldn't reduce it to 0.
            // But NumericEvaluator can't evaluate Equalities as expressions.
            // We should probably check if it's a true equality.
            // For now, if it's an equality, we might want to return false or try to evaluate it as a condition.
            if (NumericEvaluator.TryEvaluateCondition(residual, ImmutableDictionary<string, decimal>.Empty, out var isTrue, out _, allowSymbolAssignments: true) && isTrue)
            {
                return true;
            }
        }

        if (residual.InternalEquals(new Number(0m)))
        {
            return true;
        }

        var symbols = SymbolCollector.CollectSymbolsList(residual);
        var assignments = new Dictionary<string, decimal>();
        int seed = 1;
        foreach (var s in symbols)
        {
            if (s.InternalEquals(meta.IndependentVariable)) continue;
            assignments[s.Name] = seed++;
        }

        var samplePoints = new[] { -1m, 0m, 1m };
        foreach (var point in samplePoints)
        {
            assignments[meta.IndependentVariable.Name] = point;
            if (!NumericEvaluator.TryEvaluate(residual, assignments, out var value, out _))
            {
                message = "Validation failed: residual could not be evaluated.";
                return false;
            }
            if (decimal.Abs(value) > 1e-3m)
            {
                message = $"Validation failed: residual={value} at {meta.IndependentVariable.Name}={point}.";
                return false;
            }
        }

        message = "Solution validated via sampled substitution.";
        return true;
    }

    private static IExpression SimplifyResidual(IExpression residual, SolveContext context)
    {
        var rules = context.Rules;
        if (rules is null || rules.Count == 0)
        {
            rules = RuleProvider.BuildRules(context);
        }
        var simplifier = new EGraphSolverStrategy();
        var ctx = new SolveContext(null, rules, context.MaxIterations, context.EnableTracing, context.AdditionalData, context.CancellationToken);
        var simplified = simplifier.Solve(residual, ctx);
        return simplified.ResultExpression ?? residual;
    }

    private static IExpression? SolveForDependent(Equality separated, Symbol dependentSymbol, SolveContext context)
    {
        var solver = new Sym.Core.Strategies.EquationSolverStrategy();
        var solveCtx = new SolveContext(dependentSymbol, context.Rules, context.MaxIterations, context.EnableTracing, context.AdditionalData, context.CancellationToken);
        var result = solver.Solve(separated, solveCtx);
        if (!result.IsSuccess || result.ResultExpression is not Equality eqResult)
        {
            return null;
        }

        return eqResult;
    }

    private static IExpression SubstituteSymbol(IExpression expr, Symbol placeholder, IExpression replacement)
    {
        if (expr is Symbol s && s.InternalEquals(placeholder))
        {
            return replacement;
        }

        if (expr is Operation op)
        {
            var args = op.Arguments.Select(a => SubstituteSymbol(a, placeholder, replacement)).ToImmutableList();
            return op.WithArguments(args).Canonicalize();
        }

        return expr;
    }

    private static IExpression? IntegrateExpression(IExpression target, Symbol variable, SolveContext context)
    {
        var integral = new Integral(target, variable);
        var integrator = new IntegrationStrategy();
        var result = integrator.Solve(integral, new SolveContext(variable, context.Rules, context.MaxIterations, context.EnableTracing, context.AdditionalData, context.CancellationToken));
        if (!result.IsSuccess || result.ResultExpression is null)
        {
            return null;
        }
        return result.ResultExpression;
    }

    private static IExpression ReplaceDependent(IExpression expr, IExpression dependent, Symbol replacement)
    {
        if (expr.InternalEquals(dependent))
        {
            return replacement;
        }

        if (expr is Function fDep && dependent is Function depFn &&
            fDep.Name.Equals(depFn.Name, StringComparison.OrdinalIgnoreCase) &&
            fDep.Arguments.Count == depFn.Arguments.Count)
        {
            return replacement;
        }

        if (expr is Operation op)
        {
            var args = op.Arguments.Select(a => ReplaceDependent(a, dependent, replacement)).ToImmutableList();
            return op.WithArguments(args).Canonicalize();
        }

        return expr;
    }

    private static bool TryExtractLinearFirstOrder(Equality eq, DifferentialEquationMetadata meta, out IExpression a, out IExpression b, out IExpression c)
    {
        var difference = new Add(eq.LeftOperand, new Multiply(new Number(-1m), eq.RightOperand).Canonicalize()).Canonicalize();
        var terms = difference is Add add ? add.Arguments : ImmutableList.Create<IExpression>(difference);

        a = new Number(0m);
        b = new Number(0m);
        c = new Number(0m);

        foreach (var term in terms)
        {
            if (!TryClassifyTerm(term, meta.Dependent, meta.IndependentVariable, out var kind, out var coeff, out var order))
            {
                return false;
            }

            switch (kind)
            {
                case LinearTermKind.Derivative when order == 1:
                    a = new Add(a, coeff).Canonicalize();
                    break;
                case LinearTermKind.Dependent when order == 0:
                    b = new Add(b, coeff).Canonicalize();
                    break;
                case LinearTermKind.Independent:
                    c = new Add(c, term).Canonicalize();
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryExtractConstantCoefficients(Equality eq, DifferentialEquationMetadata meta, out decimal a2, out decimal a1, out decimal a0, out IExpression freeTerm)
    {
        a2 = 0m; a1 = 0m; a0 = 0m;
        freeTerm = new Number(0m);

        var difference = new Add(eq.LeftOperand, new Multiply(new Number(-1m), eq.RightOperand).Canonicalize()).Canonicalize();
        var terms = difference is Add add ? add.Arguments : ImmutableList.Create<IExpression>(difference);

        foreach (var term in terms)
        {
            if (!TryClassifyTerm(term, meta.Dependent, meta.IndependentVariable, out var kind, out var coeffExpr, out var order))
            {
                return false;
            }

            if (kind == LinearTermKind.Independent)
            {
                freeTerm = new Add(freeTerm, term).Canonicalize();
                continue;
            }

            if (kind == LinearTermKind.Dependent || kind == LinearTermKind.Derivative)
            {
                if (!TryEvaluateConstant(coeffExpr, out var coeff))
                {
                    return false;
                }

                if (order == 2)
                {
                    a2 += coeff;
                }
                else if (order == 1 && kind == LinearTermKind.Derivative)
                {
                    a1 += coeff;
                }
                else if (order == 0 && kind == LinearTermKind.Dependent)
                {
                    a0 += coeff;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryIsolateFirstDerivative(Equality eq, DifferentialEquationMetadata meta, out IExpression rhs)
    {
        rhs = null!;

        if (eq.LeftOperand is Derivative dl && IsDerivativeOfDependent(dl, meta.Dependent, meta.IndependentVariable, out var ordL) && ordL == 1 && !ContainsDerivative(eq.RightOperand))
        {
            rhs = eq.RightOperand;
            return true;
        }

        if (eq.RightOperand is Derivative dr && IsDerivativeOfDependent(dr, meta.Dependent, meta.IndependentVariable, out var ordR) && ordR == 1 && !ContainsDerivative(eq.LeftOperand))
        {
            rhs = eq.LeftOperand;
            return true;
        }

        var difference = new Add(eq.LeftOperand, new Multiply(new Number(-1m), eq.RightOperand).Canonicalize()).Canonicalize();
        var terms = difference is Add add ? add.Arguments : ImmutableList.Create<IExpression>(difference);

        IExpression derivativeCoeff = new Number(0m);
        IExpression remainder = new Number(0m);

        foreach (var term in terms)
        {
            if (!TryClassifyTerm(term, meta.Dependent, meta.IndependentVariable, out var kind, out var coeff, out var order))
            {
                return false;
            }

            if (kind == LinearTermKind.Derivative && order == 1)
            {
                derivativeCoeff = new Add(derivativeCoeff, coeff).Canonicalize();
            }
            else
            {
                remainder = new Add(remainder, term).Canonicalize();
            }
        }

        if (IsZero(derivativeCoeff))
        {
            return false;
        }

        var invCoeff = new Power(derivativeCoeff, new Number(-1m)).Canonicalize();
        rhs = new Multiply(new Number(-1m), new Multiply(remainder, invCoeff).Canonicalize()).Canonicalize();
        return true;
    }

    private static bool ContainsDerivative(IExpression expr)
    {
        if (expr is Derivative) return true;
        if (expr is Operation op) return op.Arguments.Any(ContainsDerivative);
        return false;
    }

    private static bool TryIntegrateMuQWhenQIsMultipleOfP(IExpression p, IExpression q, IExpression integratingFactor, out IExpression integralMuQ)
    {
        integralMuQ = null!;
        if (!TryGetNumericMultiple(q, p, out var multiple))
        {
            return false;
        }

        integralMuQ = new Multiply(new Number(multiple), integratingFactor).Canonicalize();
        return true;
    }

    private static bool TryGetNumericMultiple(IExpression numerator, IExpression denominator, out decimal multiple)
    {
        multiple = 0m;
        var (numCoeff, numRest) = SplitNumericCoefficient(numerator);
        var (denCoeff, denRest) = SplitNumericCoefficient(denominator);

        if (!numRest.InternalEquals(denRest))
        {
            return false;
        }

        if (denCoeff == 0m)
        {
            return false;
        }

        multiple = numCoeff / denCoeff;
        return true;
    }

    private static (decimal coeff, IExpression rest) SplitNumericCoefficient(IExpression expr)
    {
        if (expr is Number n)
        {
            return (n.Value, new Number(1m));
        }

        if (expr is Multiply mul)
        {
            decimal coeff = 1m;
            var restArgs = ImmutableList.CreateBuilder<IExpression>();

            foreach (var a in mul.Arguments)
            {
                if (a is Number an)
                {
                    coeff *= an.Value;
                }
                else
                {
                    restArgs.Add(a);
                }
            }

            var rest = restArgs.Count switch
            {
                0 => new Number(1m),
                1 => restArgs[0],
                _ => new Multiply(restArgs.ToImmutable()).Canonicalize()
            };

            return (coeff, rest);
        }

        return (1m, expr);
    }

    private static bool TryFactorSeparable(IExpression rhs, DifferentialEquationMetadata meta, out IExpression xPart, out IExpression yPart)
    {
        xPart = new Number(1m);
        yPart = new Number(1m);

        ImmutableList<IExpression> factors;
        if (rhs is Multiply mul)
        {
            factors = mul.Arguments;
        }
        else if (rhs is Divide div)
        {
            var numFactors = div.Numerator is Multiply nm ? nm.Arguments : ImmutableList.Create<IExpression>(div.Numerator);
            var denPow = new Power(div.Denominator, new Number(-1m)).Canonicalize();
            factors = numFactors.Add(denPow);
        }
        else
        {
            factors = ImmutableList.Create<IExpression>(rhs);
        }
        var xFactors = ImmutableList.CreateBuilder<IExpression>();
        var yFactors = ImmutableList.CreateBuilder<IExpression>();

        foreach (var factor in factors)
        {
            var hasDependent = ContainsDependent(factor, meta.Dependent);
            var hasIndependent = ContainsIndependent(factor, meta.IndependentVariable, meta.Dependent);
            if (hasDependent && hasIndependent)
            {
                return false;
            }

            if (hasDependent)
            {
                yFactors.Add(factor);
            }
            else
            {
                xFactors.Add(factor);
            }
        }

        xPart = xFactors.Count switch
        {
            0 => new Number(1m),
            1 => xFactors[0],
            _ => new Multiply(xFactors.ToImmutable()).Canonicalize()
        };

        yPart = yFactors.Count switch
        {
            0 => new Number(1m),
            1 => yFactors[0],
            _ => new Multiply(yFactors.ToImmutable()).Canonicalize()
        };

        return true;
    }

    private static bool TryClassifyTerm(IExpression term, IExpression dependent, Symbol independent, out LinearTermKind kind, out IExpression coefficient, out int order)
    {
        kind = LinearTermKind.NonLinear;
        coefficient = new Number(0m);
        order = 0;

        if (IsDependentBase(term, dependent))
        {
            kind = LinearTermKind.Dependent;
            coefficient = new Number(1m);
            return true;
        }

        if (term is Derivative d && IsDerivativeOfDependent(d, dependent, independent, out var ord))
        {
            kind = LinearTermKind.Derivative;
            coefficient = new Number(1m);
            order = ord;
            return true;
        }

        if (term is Multiply mul)
        {
            var derivativeFactor = mul.Arguments.FirstOrDefault(a => a is Derivative dd && IsDerivativeOfDependent(dd, dependent, independent, out _));
            var dependentFactor = mul.Arguments.FirstOrDefault(a => IsDependentBase(a, dependent));

            if (derivativeFactor is not null && dependentFactor is not null)
            {
                return false;
            }

            if (derivativeFactor is not null)
            {
                if (!IsDerivativeOfDependent((Derivative)derivativeFactor, dependent, independent, out var ordInner))
                {
                    return false;
                }
                var remaining = mul.Arguments.Remove(derivativeFactor);
                coefficient = remaining.Count switch
                {
                    0 => new Number(1m),
                    1 => remaining[0],
                    _ => new Multiply(remaining).Canonicalize()
                };
                if (ContainsDependent(coefficient, dependent))
                {
                    return false;
                }
                kind = LinearTermKind.Derivative;
                order = ordInner;
                return true;
            }

            if (dependentFactor is not null)
            {
                var remaining = mul.Arguments.Remove(dependentFactor);
                coefficient = remaining.Count switch
                {
                    0 => new Number(1m),
                    1 => remaining[0],
                    _ => new Multiply(remaining).Canonicalize()
                };
                if (ContainsDependent(coefficient, dependent))
                {
                    return false;
                }
                kind = LinearTermKind.Dependent;
                order = 0;
                return true;
            }
        }

        if (ContainsDependent(term, dependent))
        {
            return false;
        }

        kind = LinearTermKind.Independent;
        coefficient = term;
        return true;
    }

    internal static bool IsDerivativeOfDependent(Derivative deriv, IExpression dependent, Symbol independent, out int order)
    {
        order = 0;
        var current = deriv as IExpression;
        while (current is Derivative d)
        {
            order++;
            if (d.Variable is not Symbol varSym || !varSym.InternalEquals(independent))
            {
                return false;
            }
            current = d.TargetExpression;
        }

        return order > 0 && IsDependentBase(current, dependent);
    }

    private static bool IsDependentBase(IExpression expr, IExpression dependent)
    {
        if (expr.InternalEquals(dependent)) return true;

        if (expr is Function f && dependent is Function depFn &&
            f.Name.Equals(depFn.Name, StringComparison.OrdinalIgnoreCase) &&
            f.Arguments.Count == depFn.Arguments.Count &&
            !f.Arguments.Where((t, i) => !t.InternalEquals(depFn.Arguments[i])).Any())
        {
            return true;
        }

        return false;
    }

    private static bool ContainsDependent(IExpression expr, IExpression dependent)
    {
        if (IsDependentBase(expr, dependent)) return true;
        if (expr is Derivative d) return ContainsDependent(d.TargetExpression, dependent);
        if (expr is Operation op) return op.Arguments.Any(a => ContainsDependent(a, dependent));
        return false;
    }

    private static bool ContainsIndependent(IExpression expr, Symbol independent, IExpression dependent)
    {
        if (IsDependentBase(expr, dependent)) return false;
        if (expr is Derivative d && ContainsDependent(d.TargetExpression, dependent)) return false;
        if (expr is Symbol s) return s.InternalEquals(independent);
        if (expr is Operation op) return op.Arguments.Any(a => ContainsIndependent(a, independent, dependent));
        return false;
    }

    private static bool TryBuildExponentialParticular(DifferentialEquationMetadata meta, IExpression freeTerm, decimal a2, decimal a1, decimal a0, out IExpression particular)
    {
        particular = null!;
        var forcing = new Multiply(new Number(-1m), freeTerm).Canonicalize();
        var (coeff, rest) = SplitNumericCoefficient(forcing);
        if (rest is not Function fn || !string.Equals(fn.Name, "exp", StringComparison.OrdinalIgnoreCase) || fn.Arguments.Count != 1)
        {
            return false;
        }

        if (!TryExtractExpLinear(fn.Arguments[0], meta.IndependentVariable, out var k, out var shift))
        {
            return false;
        }

        if (Math.Abs((double)shift) > 1e-12)
        {
            coeff *= SymCore.NumericConvert.SafeToDecimal(Math.Exp((double)shift));
        }

        var charAtK = a2 * k * k + a1 * k + a0;
        var charPrimeAtK = 2m * a2 * k + a1;
        var charSecondAtK = 2m * a2;

        int power = 0;
        decimal denom = charAtK;
        if (Math.Abs((double)denom) < 1e-12)
        {
            power = 1;
            denom = charPrimeAtK;
            if (Math.Abs((double)denom) < 1e-12)
            {
                power = 2;
                denom = charSecondAtK;
            }
        }

        if (Math.Abs((double)denom) < 1e-12)
        {
            return false;
        }

        var amplitude = coeff / denom;
        var expArg = new Multiply(new Number(k), meta.IndependentVariable).Canonicalize();
        var exp = new Function("exp", ImmutableList.Create<IExpression>(expArg)).Canonicalize();
        IExpression candidate = new Multiply(new Number(amplitude), exp).Canonicalize();
        if (power > 0)
        {
            candidate = new Multiply(new Power(meta.IndependentVariable, new Number(power)).Canonicalize(), candidate).Canonicalize();
        }
        particular = candidate;
        return true;
    }

    private static bool TryExtractExpLinear(IExpression expr, Symbol variable, out decimal coeff, out decimal shift)
    {
        coeff = 0m; shift = 0m;

        if (expr is Symbol s && s.InternalEquals(variable))
        {
            coeff = 1m;
            return true;
        }

        if (expr is Number numOnly)
        {
            coeff = 0m;
            shift = numOnly.Value;
            return true;
        }

        if (expr is Multiply mul)
        {
            decimal scalar = 1m;
            Symbol? sym = null;
            foreach (var a in mul.Arguments)
            {
                if (a is Number n) scalar *= n.Value;
                else if (a is Symbol ss && ss.InternalEquals(variable)) sym = ss;
                else return false;
            }
            if (sym is null) return false;
            coeff = scalar;
            return true;
        }

        if (expr is Add add && add.Arguments.Count == 2)
        {
            if (TryExtractExpLinear(add.Arguments[0], variable, out var c1, out var s1) &&
                TryExtractExpLinear(add.Arguments[1], variable, out var c2, out var s2))
            {
                coeff = c1 + c2;
                shift = s1 + s2;
                return true;
            }
        }

        return false;
    }

    private static bool TryEvaluateConstant(IExpression expr, out decimal value)
    {
        value = 0m;
        if (expr is Number n)
        {
            value = n.Value;
            return true;
        }

        if (expr is Multiply mul)
        {
            decimal acc = 1m;
            foreach (var arg in mul.Arguments)
            {
                if (!TryEvaluateConstant(arg, out var part))
                {
                    return false;
                }
                acc *= part;
            }
            value = acc;
            return true;
        }

        if (expr is Add add)
        {
            decimal sum = 0m;
            foreach (var arg in add.Arguments)
            {
                if (!TryEvaluateConstant(arg, out var part))
                {
                    return false;
                }
                sum += part;
            }
            value = sum;
            return true;
        }

        if (expr is Power pow && pow.Base is Number num && pow.Exponent is Number exp)
        {
            try
            {
                value = SymCore.NumericConvert.SafeToDecimal(Math.Pow((double)num.Value, (double)exp.Value));
                return true;
            }
            catch { return false; }
        }

        return false;
    }

    private static int CountOrder(Derivative deriv)
    {
        var order = 0;
        var current = deriv as IExpression;
        while (current is Derivative d)
        {
            order++;
            current = d.TargetExpression;
        }
        return order;
    }

    private static IExpression GetDerivativeBase(Derivative deriv)
    {
        var current = deriv.TargetExpression;
        while (current is Derivative d)
        {
            current = d.TargetExpression;
        }
        return current;
    }

    private static IExpression ExpTerm(decimal rate, Symbol variable)
    {
        var arg = new Multiply(new Number(rate), variable).Canonicalize();
        return new Function("exp", ImmutableList.Create<IExpression>(arg)).Canonicalize();
    }

    private static bool IsZero(IExpression expr)
    {
        return expr is Number n && n.Value == 0m;
    }

    private static string GetDependentName(IExpression dependent)
    {
        return dependent switch
        {
            Symbol s => s.Name,
            Function f => f.Name,
            _ => "y"
        };
    }
}

internal enum LinearTermKind
{
    Derivative,
    Dependent,
    Independent,
    NonLinear
}

internal sealed class SolutionSubstitutor
{
    private readonly IExpression _dependent;
    private readonly Symbol _independent;
    private readonly IExpression _solution;

    public SolutionSubstitutor(IExpression dependent, Symbol independent, IExpression solution)
    {
        _dependent = dependent;
        _independent = independent;
        _solution = solution;
    }

    public IExpression Apply(IExpression expr)
    {
        if (expr.InternalEquals(_dependent))
        {
            return _solution;
        }

        if (expr is Derivative d && DifferentialEquationStrategy.IsDerivativeOfDependent(d, _dependent, _independent, out var order))
        {
            var derived = _solution;
            for (int i = 0; i < order; i++)
            {
                derived = CalculusHelper.DifferentiateExpression(derived, _independent).Canonicalize();
            }
            return derived;
        }

        if (expr is Operation op)
        {
            var args = op.Arguments.Select(Apply).ToImmutableList();
            return op.WithArguments(args).Canonicalize();
        }

        return expr;
    }
}
