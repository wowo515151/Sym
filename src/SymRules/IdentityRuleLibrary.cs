// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymRules;

using CoreRule = Sym.Core.Rule;

/// <summary>
/// Curated identities that are not tied to a single text rule pack, covering
/// recurrence-style reductions and piecewise expansions used across solvers.
/// </summary>
public static class IdentityRuleLibrary
{
    public static ImmutableList<CoreRule> RecurrenceRules { get; } = BuildRecurrenceRules();
    public static ImmutableList<CoreRule> PiecewiseRules { get; } = BuildPiecewiseRules();

    private static ImmutableList<CoreRule> BuildRecurrenceRules()
    {
        var n = new Wild("n");

        // factorial(n) when n is a positive integer number -> n * factorial(n-1)
        var factorialStep = new CoreRule(
            new Function("factorial", ImmutableList.Create<IExpression>(n)),
            new Multiply(n, new Function("factorial",
                ImmutableList.Create<IExpression>(new Add(n, new Number(-1m)).Canonicalize()))).Canonicalize(),
            bindings =>
            {
                if (!bindings.TryGetValue("n", out var expr) || expr is not Number num)
                {
                    return false;
                }

                var value = num.Value;
                return decimal.Truncate(value) == value && value > 0m;
            });

        var factorialBase = new CoreRule(
            new Function("factorial", ImmutableList.Create<IExpression>(new Number(0m))),
            new Number(1m));

        // Gamma recurrence for numeric inputs: gamma(n) = (n-1)*gamma(n-1) for n>1
        var gammaStepNumeric = new CoreRule(
            new Function("gamma", ImmutableList.Create<IExpression>(n)),
            new Multiply(new Add(n, new Number(-1m)).Canonicalize(),
                new Function("gamma", ImmutableList.Create<IExpression>(new Add(n, new Number(-1m)).Canonicalize()))).Canonicalize(),
            bindings =>
            {
                if (!bindings.TryGetValue("n", out var expr) || expr is not Number num)
                {
                    return false;
                }

                return num.Value > 1m;
            });

        var gammaBase = new CoreRule(
            new Function("gamma", ImmutableList.Create<IExpression>(new Number(1m))),
            new Number(1m));

        var k = new Wild("k");
        // factorial(n+1) / factorial(n) -> n+1
        var factDiv = new CoreRule(
            new Divide(new Function("factorial", new Add(n, new Number(1m)).Canonicalize()),
                       new Function("factorial", n)).Canonicalize(),
            new Add(n, new Number(1m)).Canonicalize());

        // factorial(n) / factorial(n+1) -> 1/(n+1)
        var factDivDown = new CoreRule(
            new Divide(new Function("factorial", n),
                       new Function("factorial", new Add(n, new Number(1m)).Canonicalize())).Canonicalize(),
            new Divide(new Number(1m), new Add(n, new Number(1m)).Canonicalize()).Canonicalize());

        // n! / (k! * (n-k)!) -> Combination(n, k)
        var nMinusK = new Add(n, new Multiply(new Number(-1m), k).Canonicalize()).Canonicalize();
        var combinationRule = new CoreRule(
            new Divide(new Function("factorial", n),
                       new Multiply(new Function("factorial", k),
                                    new Function("factorial", nMinusK)).Canonicalize()).Canonicalize(),
            new Function("Combination", n, k).Canonicalize());

        return ImmutableList.Create(factorialBase, factorialStep, gammaBase, gammaStepNumeric, factDiv, factDivDown, combinationRule);
    }

    private static ImmutableList<CoreRule> BuildPiecewiseRules()
    {
        var x = new Wild("x");
        var a = new Wild("a");
        var b = new Wild("b");
        var i = new Symbol("i");

        var nonNegativeGuard = new Function("ge", ImmutableList.Create<IExpression>(x, new Number(0m))).Canonicalize();
        var negativeGuard = new Function("lt", ImmutableList.Create<IExpression>(x, new Number(0m))).Canonicalize();

        var absRule = new CoreRule(
            new Function("abs", ImmutableList.Create<IExpression>(x)),
            new Piecewise(ImmutableList.Create<IExpression>(
                x, nonNegativeGuard,
                new Multiply(new Number(-1m), x).Canonicalize(), negativeGuard)).Canonicalize(),
            bindings => {
                if (bindings.TryGetValue("x", out var val))
                {
                    // Only apply real-only piecewise expansion if it doesn't look complex
                    return !val.ContainsSymbol(s => s.Name == "i" || s.Name == "j");
                }
                return true;
            });

        var complexAbsRule = new CoreRule(
            new Function("abs", new Add(a, new Multiply(b, i)).Canonicalize()),
            new Power(new Add(new Power(a, new Number(2)), new Power(b, new Number(2))).Canonicalize(), new Number(0.5m)).Canonicalize());

        var complexAbsRule2 = new CoreRule(
            new Function("abs", new Add(a, new Multiply(i, b)).Canonicalize()),
            new Power(new Add(new Power(a, new Number(2)), new Power(b, new Number(2))).Canonicalize(), new Number(0.5m)).Canonicalize());

        var conjRule = new CoreRule(
            new Function("conj", new Add(a, new Multiply(b, i)).Canonicalize()),
            new Subtract(a, new Multiply(b, i)).Canonicalize());

        var conjRule2 = new CoreRule(
            new Function("conj", new Add(a, new Multiply(i, b)).Canonicalize()),
            new Subtract(a, new Multiply(b, i)).Canonicalize());

        var conjRule3 = new CoreRule(
            new Function("conj", i),
            new Multiply(new Number(-1m), i).Canonicalize());

        var isRationalRule = new CoreRule(
            new Function("isrational", ImmutableList.Create<IExpression>(x)),
            new Symbol("true"),
            bindings => bindings.TryGetValue("x", out var val) && val is Number);

        var isIrrationalRule = new CoreRule(
            new Function("isirrational", ImmutableList.Create<IExpression>(x)),
            new Symbol("false"),
            bindings => bindings.TryGetValue("x", out var val) && val is Number);

        // Constant Folding Rules for EGraph saturation
        var constA = new Wild("a", WildConstraint.Constant);
        var constB = new Wild("b", WildConstraint.Constant);

        var constAdd = new CoreRule(
            new Add(constA, constB),
            new Number(0), // Placeholder, overridden by Transform
            transform: RuleTransforms.ConstantAdd("a", "b")) { Name = "constAdd" };

        var constMul = new CoreRule(
            new Multiply(constA, constB),
            new Number(0),
            transform: RuleTransforms.ConstantMultiply("a", "b")) { Name = "constMul" };

        var constPow = new CoreRule(
            new Power(constA, constB),
            new Number(0),
            transform: RuleTransforms.ConstantPower("a", "b")) { Name = "constPow" };

        var constDiv = new CoreRule(
            new Divide(constA, constB),
            new Number(0),
            transform: RuleTransforms.ConstantDivide("a", "b")) { Name = "constDiv" };

        var constSqrt = new CoreRule(
            new Function("sqrt", constA),
            new Number(0),
            transform: RuleTransforms.ConstantSquareRoot("a")) { Name = "constSqrt" };

        var constSqrt2 = new CoreRule(
            new Power(constA, new Number(0.5m)),
            new Number(0),
            transform: RuleTransforms.ConstantSquareRoot("a")) { Name = "constSqrt2" };

        var constAbs = new CoreRule(
            new Function("abs", constA),
            new Number(0),
            transform: RuleTransforms.ConstantAbsoluteValue("a")) { Name = "constAbs" };

        var constExp = new CoreRule(
            new Function("exp", constA),
            new Number(0),
            transform: RuleTransforms.ConstantExp("a")) { Name = "constExp" };

        var constLog = new CoreRule(
            new Function("log", constA),
            new Number(0),
            transform: RuleTransforms.ConstantLog("a")) { Name = "constLog" };

        var constSin = new CoreRule(
            new Function("sin", constA),
            new Number(0),
            transform: RuleTransforms.ConstantSin("a")) { Name = "constSin" };

        var constCos = new CoreRule(
            new Function("cos", constA),
            new Number(0),
            transform: RuleTransforms.ConstantCos("a")) { Name = "constCos" };

        var constGt = new CoreRule(
            new Function("gt", constA, constB),
            new Number(0),
            transform: RuleTransforms.ConstantGreaterThan("a", "b")) { Name = "constGt" };

        var constLt = new CoreRule(
            new Function("lt", constA, constB),
            new Number(0),
            transform: RuleTransforms.ConstantLessThan("a", "b")) { Name = "constLt" };

        var constGe = new CoreRule(
            new Function("ge", constA, constB),
            new Number(0),
            transform: RuleTransforms.ConstantGreaterThanOrEqual("a", "b")) { Name = "constGe" };

        var constLe = new CoreRule(
            new Function("le", constA, constB),
            new Number(0),
            transform: RuleTransforms.ConstantLessThanOrEqual("a", "b")) { Name = "constLe" };

        var constAnd = new CoreRule(
            new Function("and", a, b),
            new Number(0),
            transform: RuleTransforms.ConstantAnd("a", "b")) { Name = "constAnd" };

        var constOr = new CoreRule(
            new Function("or", a, b),
            new Number(0),
            transform: RuleTransforms.ConstantOr("a", "b")) { Name = "constOr" };

        var constNot = new CoreRule(
            new Function("not", a),
            new Number(0),
            transform: RuleTransforms.ConstantNot("a")) { Name = "constNot" };

        return ImmutableList.Create(absRule, complexAbsRule, complexAbsRule2, conjRule, conjRule2, conjRule3, isRationalRule, isIrrationalRule, constAdd, constMul, constPow, constDiv, constSqrt, constSqrt2, constAbs, constExp, constLog, constSin, constCos, constGt, constLt, constGe, constLe, constAnd, constOr, constNot);
    }
}
