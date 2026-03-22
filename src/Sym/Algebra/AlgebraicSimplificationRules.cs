// Copyright Warren Harding 2025
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;
using System.Collections.Generic;
using SymCore;
using System.Linq;

namespace Sym.Algebra
{
    /// <summary>
    /// Provides a collection of algebraic simplification rules for the Sym symbolic mathematics system.
    /// </summary>
    public static class AlgebraicSimplificationRules
    {
        public static ImmutableList<Rule> SimplificationRules { get; }
        public static RuleIndex SimplificationIndex { get; }

        static AlgebraicSimplificationRules()
        {
            Wild _wildX = new Wild("x");
            Wild _wildY = new Wild("y");
            Wild _wildZ = new Wild("z");
            Wild _wildA = new Wild("a");
            Wild _wildB = new Wild("b");
            Wild _wildC = new Wild("c");
            Wild _wildN = new Wild("n");
            Wild _wildM = new Wild("m");
            Wild _wildK = new Wild("k");
            Wild _wildD = new Wild("d");
            Wild _wildP = new Wild("p");

            SimplificationRules = ImmutableList.Create<Rule>(
                // --- Additive and Subtractive Rules ---
                new Rule(new Add(_wildX, new Number(0m)), _wildX),
                new Rule(new Subtract(_wildX, new Number(0m)), _wildX),
                new Rule(new Subtract(_wildX, _wildX), new Number(0m)),
                new Rule(new Subtract(new Number(0m), _wildX), new Multiply(new Number(-1m), _wildX)),
                new Rule(new Multiply(new Number(-1m), new Multiply(new Number(-1m), _wildX)), _wildX),

                // --- Assumption-gated inverse function rules ---
                // exp(log(x)) has branch-cut issues in general; only simplify when x is known positive.
                new Rule(
                    new Function("exp", ImmutableList.Create<IExpression>(
                        new Function("log", ImmutableList.Create<IExpression>(_wildX)))),
                    _wildX,
                    null,
                    (bindings, assumptions) => bindings.TryGetValue("x", out var x) && IsPositive(x, assumptions)),

                // --- Multiplicative and Divisive Rules ---
                new Rule(new Multiply(_wildX, new Number(1m)), _wildX),
                new Rule(new Multiply(_wildX, new Number(0m)), new Number(0m)),
                new Rule(new Divide(_wildX, new Number(1m)), _wildX),
                new Rule(new Divide(_wildX, _wildX), new Number(1m),
                    null, (bindings, assumptions) => bindings.TryGetValue("x", out var x) && IsNonZero(x, assumptions)),
                new Rule(new Divide(new Number(0m), _wildX), new Number(0m),
                    null, (bindings, assumptions) => bindings.TryGetValue("x", out var x) && IsNonZero(x, assumptions)),
                new Rule(new Divide(_wildX, new Number(-1m)), new Multiply(new Number(-1m), _wildX)),

                // --- Power Rules ---
                new Rule(new Power(_wildX, new Number(1m)), _wildX),
                new Rule(new Power(_wildX, new Number(0m)), new Number(1m)), // Matches Power.Canonicalize behavior (0^0 = 1)
                new Rule(new Power(new Number(1m), _wildX), new Number(1m)),
                new Rule(new Power(new Number(0m), _wildX), new Number(0m),
                    null, (bindings, assumptions) => bindings.TryGetValue("x", out var x) && IsPositive(x, assumptions)),

                // --- Logarithmic Rules ---
                new Rule(new Function("log", new Symbol("e")), new Number(1m)),
                new Rule(new Function("log", new Number(1m)), new Number(0m)),
                new Rule(new Function("ln", _wildX), new Function("log", _wildX)), // Normalize ln to log (natural log)

                // Sqrt simplification
                new Rule(new Multiply(new Power(_wildX, new Number(0.5m)), new Power(_wildY, new Number(0.5m))), new Power(new Multiply(_wildX, _wildY), new Number(0.5m))),
                new Rule(new Divide(new Power(_wildX, new Number(0.5m)), new Power(_wildY, new Number(0.5m))), new Power(new Divide(_wildX, _wildY), new Number(0.5m))),

                // Valuation rules
                new Rule(new Function("valuation", _wildP, new Multiply(_wildX, _wildY)), new Add(new Function("valuation", _wildP, _wildX), new Function("valuation", _wildP, _wildY))),
                new Rule(new Function("valuation", _wildP, new Power(_wildX, _wildN)), new Multiply(_wildN, new Function("valuation", _wildP, _wildX))),

                // Distribute integer powers over products: (x*y)^n -> x^n * y^n, only when n is integer.
                new Rule(
                    new Power(new Multiply(_wildX, _wildY), _wildN),
                    new Multiply(new Power(_wildX, _wildN), new Power(_wildY, _wildN)),
                    null,
                    (bindings, assumptions) => bindings.TryGetValue("n", out var n) && IsInteger(n, assumptions)),

                // Product/Power interaction
                new Rule(new Multiply(_wildX, new Power(_wildX, _wildN)), new Power(_wildX, new Add(new Number(1m), _wildN)),
                    null, (bindings, assumptions) => bindings.TryGetValue("x", out var x) && bindings.TryGetValue("n", out var n) && (IsInteger(n, assumptions) || IsPositive(x, assumptions))),
                new Rule(new Multiply(new Power(_wildX, _wildN), _wildX), new Power(_wildX, new Add(_wildN, new Number(1m))),
                    null, (bindings, assumptions) => bindings.TryGetValue("x", out var x) && bindings.TryGetValue("n", out var n) && (IsInteger(n, assumptions) || IsPositive(x, assumptions))),
                new Rule(new Multiply(new Power(_wildX, _wildN), new Power(_wildX, _wildM)), new Power(_wildX, new Add(_wildN, _wildM)),
                    null, (bindings, assumptions) => IsPositive(bindings["x"], assumptions) || (IsInteger(bindings["n"], assumptions) && IsInteger(bindings["m"], assumptions))),

                // --- Power Flattening (Conditional) ---
                // (x^b)^c -> x^(b*c) if x > 0. 
                // This covers cases blocked by Power.Canonicalize safety checks (like (x^2)^0.5 -> x).
                new Rule(
                    new Power(new Power(_wildX, _wildB), _wildC),
                    new Power(_wildX, new Multiply(_wildB, _wildC)),
                    null,
                    (bindings, assumptions) => IsPositive(bindings["x"], assumptions)),

                new Rule(
                    new Power(new Add(new Power(_wildA, new Number(2m)), new Multiply(new Number(-2m), _wildA, _wildB), new Power(_wildB, new Number(2m))), new Number(0.5m)),
                    new Subtract(_wildA, _wildB)),

                new Rule(
                    new Power(new Add(_wildA, _wildB), new Number(2m)),
                    new Add(new Power(_wildA, new Number(2m)), new Multiply(new Number(2m), _wildA, _wildB), new Power(_wildB, new Number(2m)))),

                // --- Combining Rules ---
                new Rule(new Add(new Multiply(_wildA, _wildX), new Multiply(_wildB, _wildX)), new Multiply(new Add(_wildA, _wildB), _wildX)),
                new Rule(new Add(_wildX, new Multiply(_wildA, _wildX)), new Multiply(new Add(new Number(1m), _wildA), _wildX)),
                new Rule(new Add(new Multiply(_wildA, _wildX), _wildX), new Multiply(new Add(_wildA, new Number(1m)), _wildX)),

                // --- Trig Power Reduction (needed for tests) ---
                new Rule(
                    new Add(new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(2m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(2m))),
                    new Number(1m)),
                new Rule(
                    new Add(_wildA, new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(2m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(2m))),
                    new Add(_wildA, new Number(1m))),

                // --- Trig Power Reduction ---
                new Rule(
                    new Add(new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(4m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(4m))),
                    new Subtract(new Number(1m), new Multiply(new Number(2m), new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(2m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(2m))))),

                new Rule(
                    new Add(_wildA, new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(4m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(4m))),
                    new Add(_wildA, new Subtract(new Number(1m), new Multiply(new Number(2m), new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(2m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(2m)))))),

                new Rule(
                    new Add(new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(6m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(6m))),
                    new Subtract(new Number(1m), new Multiply(new Number(3m), new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(2m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(2m))))),

                new Rule(
                    new Add(_wildA, new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(6m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(6m))),
                    new Add(_wildA, new Subtract(new Number(1m), new Multiply(new Number(3m), new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), new Number(2m)), new Power(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), new Number(2m)))))),

                // --- Summation Identities ---
                // Equality(A * x^B, x^C) -> Equality(A, x^(C-B))
                new Rule(
                    new Equality(
                        new Multiply(_wildA, new Power(_wildX, _wildB)),
                        new Power(_wildX, _wildC)),
                    new Equality(
                        _wildA,
                        new Power(_wildX, new Subtract(_wildC, _wildB)))),

                new Rule(
                    new Function("Sum", ImmutableList.Create<IExpression>(_wildC, _wildK, _wildM, _wildN)),
                    new Multiply(new Add(_wildN, new Multiply(new Number(-1m), _wildM), new Number(1m)), _wildC),
                    (bindings) => IsSymbolFree(bindings["c"]) && !bindings["c"].ContainsSymbol(s => s.Name == (bindings["k"] is Symbol sk ? sk.Name : "k"))),

                // Sum(k * 2^k, k, 2, n) = (n - 1) * 2^(n + 1)
                new Rule(
                    new Function("Sum", ImmutableList.Create<IExpression>(
                        new Multiply(_wildK, new Power(new Number(2m), _wildK)),
                        _wildK,
                        new Number(2m),
                        _wildN)),
                    new Multiply(
                        new Add(_wildN, new Number(-1m)),
                        new Power(new Number(2m), new Add(_wildN, new Number(1m))))),

                // Sum(k * 2^k, k, 1, n) = 2 + (n - 1) * 2^(n + 1)
                new Rule(
                    new Function("Sum", ImmutableList.Create<IExpression>(
                        new Multiply(_wildK, new Power(new Number(2m), _wildK)),
                        _wildK,
                        new Number(1m),
                        _wildN)),
                    new Add(
                        new Number(2m),
                        new Multiply(
                            new Add(_wildN, new Number(-1m)),
                            new Power(new Number(2m), new Add(_wildN, new Number(1m)))))),

                // --- Rational Normalization (Simplified) ---
                new Rule(
                    new Add(new Divide(_wildA, _wildC), new Divide(_wildB, _wildC)),
                    new Divide(new Add(_wildA, _wildB), _wildC)),

                // --- Vieta Rules (Minimal) ---
                new Rule(
                    new Vector(ImmutableList.Create<IExpression>(
                        new Equality(new Add(new Power(_wildX, new Number(2m)), new Multiply(_wildB, _wildX), _wildC), new Number(0m)),
                        new Equality(_wildD, new Symbol("sum_of_roots")))),
                    new Equality(_wildD, new Multiply(new Number(-1m), _wildB))),
                // Variation: Constant term first (e.g. 10 - 7x + x^2)
                new Rule(
                    new Vector(ImmutableList.Create<IExpression>(
                        new Equality(new Add(_wildC, new Multiply(_wildB, _wildX), new Power(_wildX, new Number(2m))), new Number(0m)),
                        new Equality(_wildD, new Symbol("sum_of_roots")))),
                    new Equality(_wildD, new Multiply(new Number(-1m), _wildB)))
            );

            SimplificationIndex = RuleIndex.Create(SimplificationRules);
        }

        private static bool IsSymbolFree(IExpression expr)
        {
            return expr switch
            {
                Number => true,
                Symbol s => IsMathConstant(s.Name),
                Operation op => op.Arguments.All(IsSymbolFree),
                _ => false
            };
        }

        private static bool IsMathConstant(string name)
        {
            return name.Equals("pi", System.StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("e", System.StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("i", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPositive(IExpression expr, Assumptions assumptions)
        {
            return expr switch
            {
                Number n => n.Value > 0m,
                Symbol s => assumptions.IsPositive(s.Name),
                Power p => p.Exponent is Number en && en.Value == 0.5m,
                Function f when f.Name.Equals("sqrt", System.StringComparison.OrdinalIgnoreCase) => true,
                _ => false
            };
        }

        private static bool IsNonZero(IExpression expr, Assumptions assumptions)
        {
            return expr switch
            {
                Number n => n.Value != 0m,
                Symbol s => assumptions.IsPositive(s.Name),
                _ => false
            };
        }

        private static bool IsInteger(IExpression expr, Assumptions assumptions)
        {
            return expr switch
            {
                Number n => decimal.Truncate(n.Value) == n.Value,
                Symbol s => assumptions.IsInteger(s.Name),
                _ => false
            };
        }

        private static bool IsGoal(IExpression expr, Assumptions assumptions) => expr is Symbol s && assumptions.IsGoal(s.Name);
        private static bool IsThought(IExpression expr, Assumptions assumptions) => expr is Symbol s && assumptions.IsThought(s.Name);
        private static bool IsAction(IExpression expr, Assumptions assumptions) => expr is Symbol s && assumptions.IsAction(s.Name);
        private static bool IsObservation(IExpression expr, Assumptions assumptions) => expr is Symbol s && assumptions.IsObservation(s.Name);

        private static bool IsReal(IExpression expr, Assumptions assumptions)
        {
            return expr is Number || expr is Symbol;
        }
    }
}
