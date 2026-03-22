// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;
using System.Collections.Generic; // Required for ImmutableDictionary<string, IExpression> bindings

namespace Sym.Calculus
{
    /// <summary>
    /// Provides a static collection of calculus rules (differentiation, integration, and vector calculus)
    /// for the Sym symbolic mathematics system.
    /// </summary>
    public static class CalculusRules
    {
        // Wildcard declarations
        private static readonly Wild _wildX = new Wild("x");
        private static readonly Wild _wildY = new Wild("y");
        private static readonly Wild _wildF = new Wild("f");
        private static readonly Wild _wildN = new Wild("n");
        private static readonly Wild _wildC = new Wild("c"); // For general constant values

        // New wildcards for general expressions as vector components, and for variables
        private static readonly Wild _wildExp1 = new Wild("exp1");
        private static readonly Wild _wildExp2 = new Wild("exp2");
        private static readonly Wild _wildExp3 = new Wild("exp3");
        private static readonly Wild _wildVar1 = new Wild("var1");
        private static readonly Wild _wildVar2 = new Wild("var2");
        private static readonly Wild _wildVar3 = new Wild("var3");

        /// <summary>
        /// Gets the immutable list of differentiation rules.
        /// </summary>
        public static ImmutableList<Rule> DifferentiationRules { get; }

        public static RuleIndex DifferentiationIndex { get; }

        /// <summary>
        /// Gets the immutable list of integration rules.
        /// </summary>
        public static ImmutableList<Rule> IntegrationRules { get; }

        public static RuleIndex IntegrationIndex { get; }

        /// <summary>
        /// Gets the immutable list of vector calculus rules.
        /// </summary>
        public static ImmutableList<Rule> VectorCalculusRules { get; }

        public static RuleIndex VectorCalculusIndex { get; }

        /// <summary>
        /// Initializes the <see cref="CalculusRules"/> class, populating the calculus rules.
        /// </summary>
        static CalculusRules()
        {
            DifferentiationRules = ImmutableList.Create<Rule>(
                // Rule: d/d(x) (f) = 0 if f does not contain x. This handles symbols (e.g., d/dx(y)=0) and numbers correctly.
                new Rule(
                    new Derivative(_wildF, _wildX),
                    new Number(0m),
                    (ImmutableDictionary<string, IExpression> bindings) =>
                    {
                        if (bindings.TryGetValue("f", out IExpression? matchedF) &&
                            bindings.TryGetValue("x", out IExpression? matchedX) && matchedX is Symbol targetVar)
                        {
                            // Assuming IExpression has a ContainsSymbol method.
                            return !matchedF.ContainsSymbol(targetVar);
                        }
                        return false;
                    }
                ),
                // Rule: d/dx (x) = 1
                new Rule(
                    new Derivative(_wildX, _wildX),
                    new Number(1m)
                ),
                // Sum rule: d/dx (f + g) = d/dx(f) + d/dx(g)
                new Rule(
                    new Derivative(new Add(_wildF, _wildY), _wildX),
                    new Add(new Derivative(_wildF, _wildX), new Derivative(_wildY, _wildX))
                ),
                // Product rule: d/dx (f * g) = g*d/dx(f) + f*d/dx(g)
                new Rule(
                    new Derivative(new Multiply(_wildF, _wildY), _wildX),
                    new Add(
                        new Multiply(_wildY, new Derivative(_wildF, _wildX)),
                        new Multiply(_wildF, new Derivative(_wildY, _wildX))
                    )
                ),
                // Power rule (with chain rule): d/dx(f^n) = n * f^(n-1) * d/dx(f), for n that is constant w.r.t x.
                new Rule(
                    new Derivative(new Power(_wildF, _wildN), _wildX),
                    new Multiply(
                        new Multiply(_wildN, new Power(_wildF, new Add(_wildN, new Number(-1m)))),
                        new Derivative(_wildF, _wildX)
                    ),
                    (ImmutableDictionary<string, IExpression> bindings) =>
                    {
                        if (bindings.TryGetValue("n", out var n) &&
                            bindings.TryGetValue("x", out var x) && x is Symbol var)
                        {
                            return !n.ContainsSymbol(var);
                        }
                        return false;
                    }
                ),
                // Chain rule: d/dx(sin(f)) = cos(f) * f'
                new Rule(
                    new Derivative(new Function("sin", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Multiply(
                        new Function("cos", ImmutableList.Create<IExpression>(_wildF)),
                        new Derivative(_wildF, _wildX))
                ),
                // Chain rule: d/dx(cos(f)) = -sin(f) * f'
                new Rule(
                    new Derivative(new Function("cos", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Multiply(
                        new Number(-1m),
                        new Multiply(new Function("sin", ImmutableList.Create<IExpression>(_wildF)), new Derivative(_wildF, _wildX)))
                ),
                // Chain rule: d/dx(tan(f)) = f' / cos(f)^2
                new Rule(
                    new Derivative(new Function("tan", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Divide(
                        new Derivative(_wildF, _wildX),
                        new Power(new Function("cos", ImmutableList.Create<IExpression>(_wildF)), new Number(2m)))
                ),
                // Quotient rule: d/dx (f/g) = (g*f' - f*g') / g^2
                new Rule(
                    new Derivative(new Divide(_wildF, _wildY), _wildX),
                    new Divide(
                        new Add(
                            new Multiply(_wildY, new Derivative(_wildF, _wildX)),
                            new Multiply(new Number(-1m), new Multiply(_wildF, new Derivative(_wildY, _wildX)))),
                        new Power(_wildY, new Number(2m)))
                ),
                // Chain rule: d/dx(asin(f)) = f' / sqrt(1 - f^2)
                new Rule(
                    new Derivative(new Function("asin", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Divide(
                        new Derivative(_wildF, _wildX),
                        new Function("sqrt", ImmutableList.Create<IExpression>(
                            new Add(new Number(1m), new Multiply(new Number(-1m), new Power(_wildF, new Number(2m)))))))
                ),
                // Chain rule: d/dx(acos(f)) = -f' / sqrt(1 - f^2)
                new Rule(
                    new Derivative(new Function("acos", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Multiply(
                        new Number(-1m),
                        new Divide(
                            new Derivative(_wildF, _wildX),
                            new Function("sqrt", ImmutableList.Create<IExpression>(
                                new Add(new Number(1m), new Multiply(new Number(-1m), new Power(_wildF, new Number(2m))))))))
                ),
                // Chain rule: d/dx(atan(f)) = f' / (1 + f^2)
                new Rule(
                    new Derivative(new Function("atan", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Divide(
                        new Derivative(_wildF, _wildX),
                        new Add(new Number(1m), new Power(_wildF, new Number(2m))))
                ),
                // Chain rule: d/dx(exp(f)) = exp(f) * f'
                new Rule(
                    new Derivative(new Function("exp", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Multiply(new Function("exp", ImmutableList.Create<IExpression>(_wildF)), new Derivative(_wildF, _wildX))
                ),
                // Chain rule: d/dx(log(f)) = f' / f
                new Rule(
                    new Derivative(new Function("log", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Divide(new Derivative(_wildF, _wildX), _wildF)
                ),
                // Chain rule: d/dx(sinh(f)) = cosh(f) * f'
                new Rule(
                    new Derivative(new Function("sinh", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Multiply(new Function("cosh", ImmutableList.Create<IExpression>(_wildF)), new Derivative(_wildF, _wildX))
                ),
                // Chain rule: d/dx(cosh(f)) = sinh(f) * f'
                new Rule(
                    new Derivative(new Function("cosh", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Multiply(new Function("sinh", ImmutableList.Create<IExpression>(_wildF)), new Derivative(_wildF, _wildX))
                ),
                // Chain rule: d/dx(tanh(f)) = f' / cosh(f)^2
                new Rule(
                    new Derivative(new Function("tanh", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Divide(new Derivative(_wildF, _wildX), new Power(new Function("cosh", ImmutableList.Create<IExpression>(_wildF)), new Number(2m)))
                ),
                // Chain rule: d/dx(asinh(f)) = f' / sqrt(f^2 + 1)
                new Rule(
                    new Derivative(new Function("asinh", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Divide(
                        new Derivative(_wildF, _wildX),
                        new Function("sqrt", ImmutableList.Create<IExpression>(new Add(new Power(_wildF, new Number(2m)), new Number(1m)))))
                ),
                // Chain rule: d/dx(acosh(f)) = f' / sqrt(f^2 - 1)
                new Rule(
                    new Derivative(new Function("acosh", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Divide(
                        new Derivative(_wildF, _wildX),
                        new Function("sqrt", ImmutableList.Create<IExpression>(new Add(new Power(_wildF, new Number(2m)), new Number(-1m)))))
                ),
                // Chain rule: d/dx(atanh(f)) = f' / (1 - f^2)
                new Rule(
                    new Derivative(new Function("atanh", ImmutableList.Create<IExpression>(_wildF)), _wildX),
                    new Divide(
                        new Derivative(_wildF, _wildX),
                        new Add(new Number(1m), new Multiply(new Number(-1m), new Power(_wildF, new Number(2m))))))
            );

            DifferentiationIndex = RuleIndex.Create(DifferentiationRules);

            IntegrationRules = ImmutableList.Create<Rule>(
                // Rule: Integral(0, x) = 0
                new Rule(
                    new Integral(new Number(0m), _wildX),
                    new Number(0m)
                ),
                // Rule: Integral(c, x) = c * x (for constant c, typically a Number)
                new Rule(
                    new Integral(_wildC, _wildX),
                    new Multiply(_wildC, _wildX),
                    (ImmutableDictionary<string, IExpression> bindings) => bindings.TryGetValue("c", out IExpression? mc) && mc is Number
                ),
                // Rule: Integral(x, x) = 0.5 * x^2
                new Rule(
                    new Integral(_wildX, _wildX),
                    new Multiply(new Number(0.5m), new Power(_wildX, new Number(2m)))
                ),
                // Rule: Integral(f + g, x) = Integral(f, x) + Integral(g, x)
                new Rule(
                    new Integral(new Add(_wildF, _wildY), _wildX),
                    new Add(new Integral(_wildF, _wildX), new Integral(_wildY, _wildX))
                ),
                // Rule: Integral(c * f, x) = c * Integral(f, x) (Constant Multiple Rule)
                new Rule(
                    new Integral(new Multiply(_wildC, _wildF), _wildX),
                    new Multiply(_wildC, new Integral(_wildF, _wildX)),
                    (ImmutableDictionary<string, IExpression> bindings) => bindings.TryGetValue("c", out IExpression? matchedC) && matchedC is Number &&
                                  bindings.TryGetValue("x", out IExpression? matchedX) && matchedX is Symbol targetVar &&
                                  !matchedC.ContainsSymbol(targetVar) // Ensure constant does not contain integration variable
                ),
                // Rule: Integral(x^n, x) = x^(n+1) / (n+1), for n != -1
                new Rule(
                    new Integral(new Power(_wildX, _wildN), _wildX),
                    new Multiply(new Power(_wildX, new Add(_wildN, new Number(1m))),
                                 new Power(new Add(_wildN, new Number(1m)), new Number(-1m))),
                    (ImmutableDictionary<string, IExpression> bindings) => bindings.TryGetValue("n", out IExpression? mn) && mn is Number numN && numN.Value != -1m
                ),
                // Rule: Integral(cos(x), x) = sin(x)
                new Rule(
                    new Integral(new Function("cos", ImmutableList.Create<IExpression>(_wildX)), _wildX),
                    new Function("sin", ImmutableList.Create<IExpression>(_wildX))
                ),
                // Rule: Integral(sin(x), x) = -cos(x)
                new Rule(
                    new Integral(new Function("sin", ImmutableList.Create<IExpression>(_wildX)), _wildX),
                    new Multiply(new Number(-1m), new Function("cos", ImmutableList.Create<IExpression>(_wildX)))
                ),
                // Rule: Integral(exp(x), x) = exp(x)
                new Rule(
                    new Integral(new Function("exp", ImmutableList.Create<IExpression>(_wildX)), _wildX),
                    new Function("exp", ImmutableList.Create<IExpression>(_wildX))
                ),
                // Rule: Integral(1/x, x) = log(x)
                new Rule(
                    new Integral(new Divide(new Number(1m), _wildX), _wildX),
                    new Function("log", ImmutableList.Create<IExpression>(_wildX)),
                    (ImmutableDictionary<string, IExpression> bindings) => bindings.TryGetValue("x", out IExpression? matchedX) && matchedX is Symbol
                ),
                // Rule: Integral(x^-1, x) = log(x)
                new Rule(
                    new Integral(new Power(_wildX, new Number(-1m)), _wildX),
                    new Function("log", ImmutableList.Create<IExpression>(_wildX)),
                    (ImmutableDictionary<string, IExpression> bindings) => bindings.TryGetValue("x", out IExpression? matchedX) && matchedX is Symbol
                ),
                // Rule: Integral(derivative(f, x), x) = f
                new Rule(
                    new Integral(new Derivative(_wildF, _wildX), _wildX),
                    _wildF,
                    (ImmutableDictionary<string, IExpression> bindings) => bindings.TryGetValue("x", out IExpression? matchedX) && matchedX is Symbol
                ),
                // Rule: Integral(1 / (1 + x^2), x) = atan(x)
                new Rule(
                    new Integral(new Divide(new Number(1m), new Add(new Number(1m), new Power(_wildX, new Number(2m)))), _wildX),
                    new Function("atan", ImmutableList.Create<IExpression>(_wildX))
                ),
                // Rule: Integral(cosh(x), x) = sinh(x)
                new Rule(
                    new Integral(new Function("cosh", ImmutableList.Create<IExpression>(_wildX)), _wildX),
                    new Function("sinh", ImmutableList.Create<IExpression>(_wildX))
                ),
                // Rule: Integral(sinh(x), x) = cosh(x)
                new Rule(
                    new Integral(new Function("sinh", ImmutableList.Create<IExpression>(_wildX)), _wildX),
                    new Function("cosh", ImmutableList.Create<IExpression>(_wildX))
                ),
                // Rule: Integral(tanh(x), x) = log(cosh(x))
                new Rule(
                    new Integral(new Function("tanh", ImmutableList.Create<IExpression>(_wildX)), _wildX),
                    new Function("log", ImmutableList.Create<IExpression>(new Function("cosh", ImmutableList.Create<IExpression>(_wildX))))
                )
            );

            IntegrationIndex = RuleIndex.Create(IntegrationRules);

            VectorCalculusRules = ImmutableList.Create<Rule>(
                // Updated: Grad(f, Vector(var1, var2, var3)) - original 3D rule, using new wildcards
                new Rule(
                    new Grad(_wildF,
                        new Vector(ImmutableList.Create<IExpression>(_wildVar1, _wildVar2, _wildVar3))
                    ),
                    new Vector(ImmutableList.Create<IExpression>(
                        new Derivative(_wildF, _wildVar1),
                        new Derivative(_wildF, _wildVar2),
                        new Derivative(_wildF, _wildVar3)
                    ))
                ),
                // NEW: Grad(f, Vector(var1, var2)) - 2D version
                new Rule(
                    new Grad(_wildF,
                        new Vector(ImmutableList.Create<IExpression>(_wildVar1, _wildVar2))
                    ),
                    new Vector(ImmutableList.Create<IExpression>(
                        new Derivative(_wildF, _wildVar1),
                        new Derivative(_wildF, _wildVar2)
                    ))
                ),
                // Updated: Div(Vector(exp1, exp2, exp3), Vector(var1, var2, var3)) - original 3D rule, using new wildcards
                new Rule(
                    new Div(
                        new Vector(ImmutableList.Create<IExpression>(_wildExp1, _wildExp2, _wildExp3)),
                        new Vector(ImmutableList.Create<IExpression>(_wildVar1, _wildVar2, _wildVar3))
                    ),
                    new Add(ImmutableList.Create<IExpression>(
                        new Derivative(_wildExp1, _wildVar1),
                        new Derivative(_wildExp2, _wildVar2),
                        new Derivative(_wildExp3, _wildVar3)
                    ))
                ),
                // NEW: Div(Vector(exp1, exp2), Vector(var1, var2)) - 2D version
                new Rule(
                    new Div(
                        new Vector(ImmutableList.Create<IExpression>(_wildExp1, _wildExp2)),
                        new Vector(ImmutableList.Create<IExpression>(_wildVar1, _wildVar2))
                    ),
                    new Add(ImmutableList.Create<IExpression>(
                        new Derivative(_wildExp1, _wildVar1),
                        new Derivative(_wildExp2, _wildVar2)
                    ))
                ),
                // Updated: Curl(Vector(exp1, exp2, exp3), Vector(var1, var2, var3)) - original 3D rule, using new wildcards
                new Rule(
                    new Curl(
                        new Vector(ImmutableList.Create<IExpression>(_wildExp1, _wildExp2, _wildExp3)),
                        new Vector(ImmutableList.Create<IExpression>(_wildVar1, _wildVar2, _wildVar3))
                    ),
                    new Vector(ImmutableList.Create<IExpression>(
                        new Add(new Derivative(_wildExp3, _wildVar2), new Multiply(new Number(-1m), new Derivative(_wildExp2, _wildVar3))),
                        new Add(new Derivative(_wildExp1, _wildVar3), new Multiply(new Number(-1m), new Derivative(_wildExp3, _wildVar1))),
                        new Add(new Derivative(_wildExp2, _wildVar1), new Multiply(new Number(-1m), new Derivative(_wildExp1, _wildVar2)))
                    ))
                )
            );

            VectorCalculusIndex = RuleIndex.Create(VectorCalculusRules);
        }
    }
}
