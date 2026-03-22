// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SymRules;

internal sealed class EmbeddedRuleTextFile
{
    public EmbeddedRuleTextFile(string fileName, string content)
    {
        FileName = fileName;
        Content = content.Replace("\r\n", "\n").Trim();
    }

    public string FileName { get; }
    public string Content { get; }
}

internal sealed class EmbeddedRulePackDescriptor
{
    public EmbeddedRulePackDescriptor(string folderName, string name, string description, bool enabledByDefault, int priority, IReadOnlyList<EmbeddedRuleTextFile> files)
    {
        FolderName = folderName;
        Name = name;
        Description = description;
        EnabledByDefault = enabledByDefault;
        Priority = priority;
        Files = files;
    }

    public string FolderName { get; }
    public string Name { get; }
    public string Description { get; }
    public bool EnabledByDefault { get; }
    public int Priority { get; }
    public IReadOnlyList<EmbeddedRuleTextFile> Files { get; }

    public RulePackInfo ToPackInfo()
    {
        var displayPath = Path.Combine("SymRules", FolderName);
        return new RulePackInfo(Name, Description, displayPath, displayPath, EnabledByDefault, Priority);
    }
}

internal static class EmbeddedRulePacks
{
    private static readonly IReadOnlyDictionary<string, EmbeddedRulePackDescriptor> Packs =
        new Dictionary<string, EmbeddedRulePackDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["Algebraic"] = new(
                "Algebraic",
                "AlgebraicStrategy",
                "Core algebraic simplification and transformation rules.",
                true,
                10,
                new[]
                {
                    File("add_zero.rule", """
AddZero: a + 0 -> a
"""),
                    File("algebraic.rule", """
# Combine powers with same exponent
PowMatchExponent: Pow(a, k) * Pow(b, k) -> Pow(a * b, k)

# Square root simplification (handled partially by Power.Canonicalize, but explicit rules help in complex forms)
SqrtMul: Sqrt(a) * Sqrt(b) -> Sqrt(a * b)
SqrtDiv: Sqrt(a) / Sqrt(b) -> Sqrt(a / b)

# Valuation rules
ValuationMul: valuation(p, a * b) -> valuation(p, a) + valuation(p, b)
ValuationPow: valuation(p, Pow(a, k)) -> k * valuation(p, a)
"""),
                    File("cancel-factors.rule", """
(A * B) / (A * C) => B / C
"""),
                    File("combine_like_terms.rule", """
# Combine identical symbolic terms
# Example: a + a -> 2 * a
a + a -> 2 * a
"""),
                    File("combine_powers_same_exp.rule", """
CombinePowersSameExp: Pow(a, n) * Pow(b, n) -> Pow(a * b, n)
CombinePowersSameExp3: Pow(a, n) * Pow(b, n) * Pow(c, n) -> Pow(a * b * c, n)
CombinePowersSameExpCoeff: k * Pow(a, n) * Pow(b, n) -> k * Pow(a * b, n)
CombinePowersPlus1: Pow(a, n) * Pow(b, n + 1) -> b * Pow(a * b, n)
CombinePowersPlus2: Pow(a, n) * Pow(b, n + 2) -> Pow(b, 2) * Pow(a * b, n)
CombinePowersMinus1: Pow(a, n) * Pow(b, n - 1) -> Pow(b, -1) * Pow(a * b, n)
"""),
                    File("combine-powers.rule", """
a^m * a^n => a^(m + n)
SplitPowerPlus1: Pow(a, n + 1) -> a * Pow(a, n)
SplitPowerPlus2: Pow(a, n + 2) -> Pow(a, 2) * Pow(a, n)
CombineSameExp: Pow(a, n) * Pow(b, n) -> Pow(a * b, n)
CombineSameExpCoeff: k * Pow(a, n) * Pow(b, n) -> k * Pow(a * b, n)
CombineSameExpCoeff2: k * Pow(a, n) * m * Pow(b, n) -> (k * m) * Pow(a * b, n)
"""),
                    File("commutative.rule", """
CommutativeAdd: a + b -> b + a
CommutativeMultiply: a * b -> b * a
"""),
                    File("complete_square.rule", """
# Completion of square rules for single-variable quadratics.
# These are heuristic rewrites and assume a non-zero quadratic coefficient.

CompleteSquare: Pow(x, 2) + b * x -> Pow(x + (b / 2), 2) - (Pow(b, 2) / 4)
CompleteSquareConst: c + Pow(x, 2) + b * x -> Pow(x + (b / 2), 2) + c - (Pow(b, 2) / 4)

CompleteSquareCoeff: a * Pow(x, 2) + b * x -> a * Pow(x + (b / (2 * a)), 2) - (Pow(b, 2) / (4 * a))
CompleteSquareCoeffLFirst: b * x + z * Pow(x, 2) -> z * Pow(x + (b / (2 * z)), 2) - (Pow(b, 2) / (4 * z))
CompleteSquareCoeffConst: c + a * Pow(x, 2) + b * x -> a * Pow(x + (b / (2 * a)), 2) + c - (Pow(b, 2) / (4 * a))
CompleteSquareCoeffConstLFirst: c + b * x + z * Pow(x, 2) -> z * Pow(x + (b / (2 * z)), 2) + c - (Pow(b, 2) / (4 * z))
"""),
                    File("difference_of_squares.rule", """
DiffSquares: Pow(a,2) - Pow(b,2) -> (a - b)*(a + b)
DiffSquaresAdd: Pow(a,2) + (-1*Pow(b,2)) -> (a - b)*(a + b)
"""),
                    File("expand_diff_squares.rule", """
ExpandDiffSquares: (a + b)*(a + b) - (a - b)*(a - b) -> 4 * a * b
ExpandDiffSquaresSym: (a + b)*(a + b) + -1*(a - b)*(a - b) -> 4 * a * b
ExpandDiffSquaresProd: (a + b + (a - b)) * (a + b - (a - b)) -> 4 * a * b
ExpandDiffSquaresMixed: ((a + b) - (a - b)) * ((a + b) + (a - b)) -> 4 * a * b
"""),
                    File("factor_out_constant.rule", """
# Factor out simple numeric constants (example rule; real implementation should be conservative)
2 * a + 3 * a -> (2 + 3) * a
"""),
                    File("factorization.rule", """
# Factorization Identities
DiffSquares: ?a^2 - ?b^2 -> (?a - ?b) * (?a + ?b)
SumSquares: ?a^2 + 2*?a*?b + ?b^2 -> (?a + ?b)^2
SubSquares: ?a^2 - 2*?a*?b + ?b^2 -> (?a - ?b)^2

# Constant cases for difference of squares
DiffSquaresConst: ?x^2 - ?c:Constant -> (?x - sqrt(?c)) * (?x + sqrt(?c))
"""),
                    File("function_simplification.rule", """
# Injective function equalities
EqSqrt: Equality(sqrt(?a), sqrt(?b)) -> Equality(?a, ?b)
EqExp: Equality(exp(?a), exp(?b)) -> Equality(?a, ?b)
EqLog: Equality(log(?a), log(?b)) -> Equality(?a, ?b)
EqLn: Equality(ln(?a), ln(?b)) -> Equality(?a, ?b)

# Exponential / Log identities for solving
ExpMulSolve: Equality(?a:Constant * exp(?b), exp(?c)) -> Equality(log(?a) + ?b, ?c)
ExpMulSolve2: Equality(exp(?a) * exp(?b), exp(?c)) -> Equality(?a + ?b, ?c)

# Factorial identities
FactDiv: factorial(?n + 1) / factorial(?n) -> ?n + 1
FactDivDown: factorial(?n) / factorial(?n + 1) -> 1 / (?n + 1)
FactExpand: factorial(?n + 1) -> (?n + 1) * factorial(?n)
FactContract: (?n + 1) * factorial(?n) -> factorial(?n + 1)

# Piecewise evaluation (Basic)
PiecewiseTrue: Piecewise(?a, true, ?b) -> ?a
PiecewiseFalse: Piecewise(?a, false, ?b) -> ?b

# Common function identities
AbsConst: abs(?n:Constant) -> abs(?n)
CeilConst: ceil(?n:Constant) -> ceil(?n)
FloorConst: floor(?n:Constant) -> floor(?n)
SignConst: sign(?n:Constant) -> sign(?n)
GcdConst: gcd(?a:Constant, ?b:Constant) -> gcd(?a, ?b)
LcmConst: lcm(?a:Constant, ?b:Constant) -> lcm(?a, ?b)
ModConst: mod(?a:Constant, ?b:Constant) -> mod(?a, ?b)
"""),
                    File("imaginary.rule", """
# Imaginary unit rules
i^2 -> -1
Pow(i, 2) -> -1
i * i -> -1
"""),
                    File("log.rule", """
// Logarithmic identities
// Convention: log(base, value) - this seems to be what the LLM produces despite instructions.
// We override NumericEvaluator's (value, base) by providing a symbolic expansion rule.

// LogExpand
log(b, x) -> log(x) / log(b)
// LogInverse
1 / log(b, x) -> log(x, b)
// LogPower
log(b, Pow(x, k)) -> k * log(b, x)
// LogProduct
log(b, x * y) -> log(b, x) + log(b, y)
// LogQuotient
log(b, x / y) -> log(b, x) - log(b, y)
// LogOfBase
log(b, b) -> 1
// LogOfOne
log(b, 1) -> 0
"""),
                    File("move-negative-powers.rule", """
a^-n => 1 / a^n
"""),
                    File("mul_one.rule", """
mul_one: a * 1 -> a
"""),
                    File("multiplication.rule", """
MultiplyZero: x * 0 -> 0
"""),
                    File("multiply.rule", """
MultiplyOne: x * 1 -> x
"""),
                    File("polynomial.rule", """
# Polynomial Division Rules
# Polynomial Remainder Theorem: P(x) % (x - a) = P(a)
PolyRemainder: mod(?p, ?x - ?a:Constant) -> substitute(?p, ?x, ?a)
PolyRemainderAdd: mod(?p, ?x + ?a:Constant) -> substitute(?p, ?x, -?a)
"""),
                    File("radical_simplification.rule", """
SqrtMultiply: Pow(a, 0.5) * Pow(b, 0.5) -> Pow(a * b, 0.5)
"""),
                    File("subtract_same.rule", """
a - a -> 0
"""),
                    File("sum_of_squares.rule", """
SumSquares: Pow(a,2) + Pow(b,2) -> (Pow((a + b),2) - (2*a*b))
"""),
                    File("zero_mul.rule", """
zero_mul: 0 * a -> 0
""")
                }),
            ["Calculus"] = new(
                "Calculus",
                "DifferentiationStrategy",
                "Differentiation and ODE rewrite rules.",
                true,
                20,
                new[]
                {
                    File("differentiation.rule", """
# Basic Operations
AddRule: Derivative(?f + ?g, ?x) -> Derivative(?f, ?x) + Derivative(?g, ?x)
SubRule: Derivative(?f - ?g, ?x) -> Derivative(?f, ?x) - Derivative(?g, ?x)
MulRule: Derivative(?f * ?g, ?x) -> Derivative(?f, ?x) * ?g + ?f * Derivative(?g, ?x)
DivRule: Derivative(?f / ?g, ?x) -> (Derivative(?f, ?x) * ?g - ?f * Derivative(?g, ?x)) / (?g^2)
PowRule: Derivative(?f ^ ?g, ?x) -> (?f ^ ?g) * (Derivative(?g, ?x) * log(?f) + ?g * Derivative(?f, ?x) / ?f)

# Constants and Variables
DerivConst: Derivative(?c:Constant, ?x) -> 0
DerivVar: Derivative(?x, ?x) -> 1

# Trigonometric Functions
SinRule: Derivative(sin(?u), ?x) -> cos(?u) * Derivative(?u, ?x)
CosRule: Derivative(cos(?u), ?x) -> -1 * sin(?u) * Derivative(?u, ?x)
TanRule: Derivative(tan(?u), ?x) -> Derivative(?u, ?x) / (cos(?u)^2)

# Inverse Trig
AsinRule: Derivative(asin(?u), ?x) -> Derivative(?u, ?x) / sqrt(1 - ?u^2)
AcosRule: Derivative(acos(?u), ?x) -> -1 * Derivative(?u, ?x) / sqrt(1 - ?u^2)
AtanRule: Derivative(atan(?u), ?x) -> Derivative(?u, ?x) / (1 + ?u^2)

# Exponential and Log
ExpRule: Derivative(exp(?u), ?x) -> exp(?u) * Derivative(?u, ?x)
LogRule: Derivative(log(?u), ?x) -> Derivative(?u, ?x) / ?u

# Power special cases (optimization)
PowConstExp: Derivative(?x ^ ?n:Constant, ?x) -> ?n * ?x ^ (?n - 1)
"""),
                    File("ode.rule", """
# ODE Classification and Patterns
# First-order linear: y' + P(x)y = Q(x)
ODEFirstOrderLinear: Equality(Derivative(?y, ?x) + ?p * ?y, ?q) -> solve_linear_ode(?y, ?x, ?p, ?q)

# First-order separable: y' = f(x)g(y)
ODESeparable: Equality(Derivative(?y, ?x), ?fx * ?gy) -> solve_separable_ode(?y, ?x, ?fx, ?gy)

# Second-order constant coefficients: ay'' + by' + cy = 0
ODESecondOrderConst: Equality(?a:Constant * Derivative(Derivative(?y, ?x), ?x) + ?b:Constant * Derivative(?y, ?x) + ?c:Constant * ?y, 0) -> solve_second_order_const_ode(?y, ?x, ?a, ?b, ?c)
""")
                }),
            ["EquationSolving"] = new(
                "EquationSolving",
                "EquationSolving",
                "Equation isolation and structural solving rules.",
                true,
                30,
                new[]
                {
                    File("equation_solving.rule", """
# Small, conservative rule set that enables combining like terms and basic distribution needed
# by equation isolation.
# Format: name: pattern -> replacement
CombineLikeTerms: add(mul(n1, x), mul(n2, x)) -> mul(add(n1, n2), x)
DistributeNegation: mul(-1, add(a, b)) -> add(mul(-1, a), mul(-1, b))

# Addition Isolation
IsolateAddLeft: Equality(a + b, c) -> Equality(a, c - b)
IsolateAddRight: Equality(a + b, c) -> Equality(b, c - a)

# Subtraction Isolation
IsolateSubLeft: Equality(a - b, c) -> Equality(a, c + b)
IsolateSubRight: Equality(a - b, c) -> Equality(b, a - c)

# Multiplication Isolation
IsolateMulLeft: Equality(a * b, c) -> Equality(a, c / b)
IsolateMulRight: Equality(a * b, c) -> Equality(b, c / a)

# Division Isolation
IsolateDivLeft: Equality(a / b, c) -> Equality(a, c * b)
IsolateDivRight: Equality(a / b, c) -> Equality(b, a / c)

# Negation Isolation
IsolateNeg: Equality(-a, b) -> Equality(a, -b)

# Power Isolation (Square root)
IsolateSquare: Equality(Pow(a, 2), b) -> Equality(a, Sqrt(b))
IsolateSqrt: Equality(Sqrt(a), b) -> Equality(a, Pow(b, 2))
"""),
                    File("isolation.rule", """
# Isolation Rules for Equation Solving
# Format: name: pattern -> replacement

# Addition Isolation
IsolateAddLeft: Equality(a + b, c) -> Equality(a, c - b)
IsolateAddRight: Equality(a + b, c) -> Equality(b, c - a)

# Subtraction Isolation
IsolateSubLeft: Equality(a - b, c) -> Equality(a, c + b)
IsolateSubRight: Equality(a - b, c) -> Equality(b, a - c)

# Multiplication Isolation
IsolateMulLeft: Equality(a * b, c) -> Equality(a, c / b)
IsolateMulRight: Equality(a * b, c) -> Equality(b, c / a)

# Division Isolation
IsolateDivLeft: Equality(a / b, c) -> Equality(a, c * b)
IsolateDivRight: Equality(a / b, c) -> Equality(b, a / c)

# MatMul Isolation
IsolateMatMulLeft: Equality(MatMul(?a, ?b), ?c) -> Equality(?b, MatMul(inverse(?a), ?c))
IsolateMatMulRight: Equality(MatMul(?a, ?b), ?c) -> Equality(?a, MatMul(?c, inverse(?b)))

# Negation Isolation
IsolateNeg: Equality(-a, b) -> Equality(a, -b)

# Power Isolation (Square root)
IsolateSquare: Equality(Pow(a, 2), b) -> Equality(a, Sqrt(b))
IsolateSqrt: Equality(Sqrt(a), b) -> Equality(a, Pow(b, 2))
"""),
                    File("structural_match.rule", """
# Generic Structural Match Rules
# These rules help match structures by stripping away common functions.

EqSin: Equality(sin(?a), sin(?b)) -> Equality(?a, ?b)
EqCos: Equality(cos(?a), cos(?b)) -> Equality(?a, ?b)
EqTan: Equality(tan(?a), tan(?b)) -> Equality(?a, ?b)
EqSqrt: Equality(sqrt(?a), sqrt(?b)) -> Equality(?a, ?b)
EqExp: Equality(exp(?a), exp(?b)) -> Equality(?a, ?b)
EqLog: Equality(log(?a), log(?b)) -> Equality(?a, ?b)

# Simple addition/multiplication matching
EqAdd: Equality(?a + ?b, ?c + ?d) -> Vector(Equality(?a, ?c), Equality(?b, ?d))
EqMul: Equality(?a * ?b, ?c * ?d) -> Vector(Equality(?a, ?c), Equality(?b, ?d))
""")
                }),
            ["Generating"] = new(
                "Generating",
                "Generating",
                "Generation-time cleanup rules.",
                true,
                40,
                new[]
                {
                    File("cleanup.rule", """
# Generation-only cleanup rules (deduplicate trivial expressions)
AddZeroRight: a + 0 -> a
AddZeroLeft: 0 + a -> a
""")
                }),
            ["Graph"] = new(
                "Graph",
                "Graph",
                "Built-in graph rewriting rules.",
                true,
                45,
                Array.Empty<EmbeddedRuleTextFile>()),
            ["Inequality"] = new(
                "Inequality",
                "Inequality",
                "Basic inequality normalization rules.",
                true,
                50,
                new[]
                {
                    File("inequality.rule", """
# Basic inequality normalizations that are always valid.
# Format: name: pattern -> replacement
ReflexiveFalse: x < x -> false
ReflexiveFalseGt: x > x -> false
ReflexiveTrueLe: x <= x -> true
ReflexiveTrueGe: x >= x -> true
AndConflict: and(lt(a, b), gt(a, b)) -> false
NestedGuard: and(and(p, q), r) -> and(p, q, r)
""")
                }),
            ["Integration"] = new(
                "Integration",
                "IntegrationStrategy",
                "Basic symbolic integration rules.",
                true,
                60,
                new[]
                {
                    File("integration.rule", """
# Basic Integration Rules
IntConst: Integral(?c:Constant, ?x) -> ?c * ?x
IntVar: Integral(?x, ?x) -> ?x^2 / 2
IntPow: Integral(?x^?n:Constant, ?x) -> ?x^(?n + 1) / (?n + 1)
IntReciprocal: Integral(1/?x, ?x) -> log(?x)

# Trig Integrals
IntSin: Integral(sin(?x), ?x) -> -cos(?x)
IntCos: Integral(cos(?x), ?x) -> sin(?x)

# Exponential
IntExp: Integral(exp(?x), ?x) -> exp(?x)

# Linearity (Handled by engine mostly, but can be explicit)
IntAdd: Integral(?f + ?g, ?x) -> Integral(?f, ?x) + Integral(?g, ?x)
IntMulConst: Integral(?c:Constant * ?f, ?x) -> ?c * Integral(?f, ?x)
""")
                }),
            ["Limit"] = new(
                "Limit",
                "LimitStrategy",
                "Basic limit rewrite rules.",
                true,
                70,
                new[]
                {
                    File("limit.rule", """
# Basic Limit Rules
LimitConst: Limit(?c:Constant, ?x, ?a) -> ?c
LimitVar: Limit(?x, ?x, ?a) -> ?a

# Common Limits
SinOverX: Limit(sin(?x)/?x, ?x, 0) -> 1
ExpMinusOneOverX: Limit((exp(?x) - 1)/?x, ?x, 0) -> 1

# Polynomial Dominance (Infinity)
LimitPolyRatioInf: Limit((?a:Constant * ?x^?n:Constant + ?r1) / (?b:Constant * ?x^?m:Constant + ?r2), ?x, infinity) -> (?a/?b) * Limit(?x^(?n-?m), ?x, infinity)
""")
                }),
            ["Logic"] = new(
                "Logic",
                "Logic",
                "Logic normalization and expansion rules.",
                true,
                80,
                new[]
                {
                    File("logic_simplify.rule", """
# Basic logic simplifications (binary forms)
AndTrueLeft: and(true, a) -> a
AndTrueRight: and(a, true) -> a
AndFalseLeft: and(false, a) -> false
AndFalseRight: and(a, false) -> false
OrTrueLeft: or(true, a) -> true
OrTrueRight: or(a, true) -> true
OrFalseLeft: or(false, a) -> a
OrFalseRight: or(a, false) -> a
NotTrue: not(true) -> false
NotFalse: not(false) -> true
DoubleNeg: not(not(a)) -> a
AndIdempotent: and(a, a) -> a
OrIdempotent: or(a, a) -> a
AndNotLeft: and(a, not(a)) -> false
AndNotRight: and(not(a), a) -> false
OrNotLeft: or(a, not(a)) -> true
OrNotRight: or(not(a), a) -> true
DeMorganAnd: not(and(a, b)) -> or(not(a), not(b))
DeMorganOr: not(or(a, b)) -> and(not(a), not(b))
"""),
                    File("logic.rule", """
# Propositional logic expansions for core operators.
# Format: name: pattern -> replacement
Implies: implies(a, b) -> or(not(a), b)
Iff: iff(a, b) -> and(implies(a, b), implies(b, a))
""")
                }),
            ["Matrix"] = new(
                "Matrix",
                "MatrixStrategy",
                "Matrix algebra and inverse rules.",
                true,
                90,
                new[]
                {
                    File("identity.rule", """
# Matrix identity rules for left/right identity and vector products.
IdentityLeft: MatrixMultiply(Identity, a) -> a
IdentityRight: MatrixMultiply(a, Identity) -> a
IdentityMul: Identity * a -> a
IdentityVector: MatrixMultiply(Identity, v) -> v
"""),
                    File("inverse.rule", """
# Matrix inverse rules for 2x2 matrices
Inverse2x2: inverse(Matrix(Vector(a, b), Vector(c, d))) -> Matrix(Vector(d/(a*d - b*c), -b/(a*d - b*c)), Vector(-c/(a*d - b*c), a/(a*d - b*c)))
"""),
                    File("matrix.rule", """
# Matrix Identities
Det2x2: determinant(Matrix(Vector(?a, ?b), Vector(?c, ?d))) -> ?a * ?d - ?b * ?c
Inv2x2: inverse(Matrix(Vector(?a, ?b), Vector(?c, ?d))) -> (1 / (?a * ?d - ?b * ?c)) * Matrix(Vector(?d, -?b), Vector(-?c, ?a))

# Matrix Equality component-wise
MatEq2x2: Equality(Matrix(Vector(?a, ?b), Vector(?c, ?d)), Matrix(Vector(?e, ?f), Vector(?g, ?h))) -> Vector(Equality(?a, ?e), Equality(?b, ?f), Equality(?c, ?g), Equality(?d, ?h))
""")
                }),
            ["NumberTheory"] = new(
                "NumberTheory",
                "NumberTheoryStrategy",
                "Number theory rewrite rules.",
                true,
                100,
                new[]
                {
                    File("number_theory.rule", """
# Modular Arithmetic
ModPow: mod(pow(?a:Constant, ?n:Constant), ?m:Constant) -> mod_pow(?a, ?n, ?m)
ModInverse: mod_inverse(?a:Constant, ?m:Constant) -> modular_inverse(?a, ?m)

# Congruence extraction
EqMod: Equality(mod(?x, ?m:Constant), ?r:Constant) -> Equality(mod(?x, ?m), ?r)
""")
                }),
            ["Recurrence"] = new(
                "Recurrence",
                "RecurrenceStrategy",
                "Recurrence detection and simplification rules.",
                true,
                110,
                new[]
                {
                    File("recurrence.rule", """
# Linear Recurrence Rules
# a(n) = c * a(n-1) -> a(n) = a(0) * c^n
RecurrenceLinear1: Equality(Function(?a, ?n), ?c:Constant * Function(?a, ?n - 1)) -> Equality(Function(?a, ?n), Function(?a, 0) * ?c^?n)

# a(n) = a(n-1) + d -> a(n) = a(0) + n * d
RecurrenceArithmetic: Equality(Function(?a, ?n), Function(?a, ?n - 1) + ?d:Constant) -> Equality(Function(?a, ?n), Function(?a, 0) + ?n * ?d)
""")
                }),
            ["Rules"] = new(
                "Rules",
                "Rules",
                "Example rule pack retained for round-trip parsing coverage.",
                true,
                120,
                new[]
                {
                    File("AddZero.rule", """
# Simple rule file for AddZero
AddZero: a + 0 -> a
""")
                }),
            ["SpecialFunctions"] = new(
                "SpecialFunctions",
                "SpecialFunctions",
                "Special function recurrence and piecewise rules.",
                true,
                130,
                new[]
                {
                    File("factorial.rule", """
# Symbolic factorial recurrence.
FactorialShift: factorial(n + 1) -> (n + 1) * factorial(n)
"""),
                    File("gamma.rule", """
# Gamma function recurrence anchored on shift form.
GammaShift: gamma(x + 1) -> x * gamma(x)
"""),
                    File("piecewise.rule", """
# Piecewise evaluation rules
PiecewiseEvalTrue: Piecewise(a, true, b) -> a
PiecewiseEvalFalse: Piecewise(a, false, b) -> b

# Multi-argument Piecewise (heuristic for common forms)
PiecewiseEvalTrue3: Piecewise(a, true, c1, b, c2, c) -> a
PiecewiseEvalFalse3: Piecewise(a, false, c1, b, c2, c) -> Piecewise(c1, b, c2, c)
""")
                }),
            ["Tensor"] = new(
                "Tensor",
                "Tensor",
                "Tensor, matrix, and fusion-oriented optimization rules.",
                true,
                140,
                new[]
                {
                    File("Tensor.rule", """
# 1. Algebraic Rules

# Transpose(Transpose(A)) -> A
TransposeDouble: Transpose(Transpose(?a)) -> ?a

# Distributive Property: MatMul(A, B + C) -> A*B + A*C
# Useful for expanding to find fusion opportunities or factoring to reduce ops.
# We might need both directions or control them via strategies. 
# For now, let's allow expansion to explore.
DistributeMatMulLeft: MatMul(?a, TensorAdd(?b, ?c)) -> TensorAdd(MatMul(?a, ?b), MatMul(?a, ?c))
DistributeMatMulRight: MatMul(TensorAdd(?a, ?b), ?c) -> TensorAdd(MatMul(?a, ?c), MatMul(?b, ?c))

# Factoring Rule: A*B + A*C -> A*(B + C)
# Reduces FLOPs by sharing the multiplication.
FactorMatMulLeft: TensorAdd(MatMul(?a, ?b), MatMul(?a, ?c)) -> MatMul(?a, TensorAdd(?b, ?c))
FactorMatMulRight: TensorAdd(MatMul(?a, ?c), MatMul(?b, ?c)) -> MatMul(TensorAdd(?a, ?b), ?c)

# Transpose of Product: (AB)^T -> B^T A^T
TransposeProduct: Transpose(MatMul(?a, ?b)) -> MatMul(Transpose(?b), Transpose(?a))

# Transpose of Sum: (A + B)^T -> A^T + B^T
TransposeSum: Transpose(TensorAdd(?a, ?b)) -> TensorAdd(Transpose(?a), Transpose(?b))

# Transpose of Elementwise Product: (A * B)^T -> A^T * B^T
TransposeElemMul: Transpose(TensorMul(?a, ?b)) -> TensorMul(Transpose(?a), Transpose(?b))

# Associativity of MatMul
# Allows the solver to reorder multiplications to minimize FLOPs/Memory.
AssocMatMul1: MatMul(MatMul(?a, ?b), ?c) -> MatMul(?a, MatMul(?b, ?c))
AssocMatMul2: MatMul(?a, MatMul(?b, ?c)) -> MatMul(MatMul(?a, ?b), ?c)

# Zero Rules
# Simplify tensor expressions interacting with scalar zero.
TensorAddZeroRight: TensorAdd(?a, 0) -> ?a
TensorAddZeroLeft: TensorAdd(0, ?a) -> ?a
TensorMulZeroRight: TensorMul(?a, 0) -> 0
TensorMulZeroLeft: TensorMul(0, ?a) -> 0

# Identity Rules
# Scalar 1 acts as identity for MatMul (via broadcasting/scalar mult).
MatMulIdentityRight: MatMul(?a, 1) -> ?a
MatMulIdentityLeft: MatMul(1, ?a) -> ?a
TensorMulIdentityRight: TensorMul(?a, 1) -> ?a
TensorMulIdentityLeft: TensorMul(1, ?a) -> ?a

# TensorAdd Properties
TensorAddComm: TensorAdd(?a, ?b) -> TensorAdd(?b, ?a)
TensorMulComm: TensorMul(?a, ?b) -> TensorMul(?b, ?a)
TensorAddAssoc1: TensorAdd(TensorAdd(?a, ?b), ?c) -> TensorAdd(?a, TensorAdd(?b, ?c))
TensorAddAssoc2: TensorAdd(?a, TensorAdd(?b, ?c)) -> TensorAdd(TensorAdd(?a, ?b), ?c)
TensorAddSelf: TensorAdd(?a, ?a) -> TensorMul(?a, 2)

# 2. Fusion Rules (Optimization Targets)

# Fused MatMul+Add
# A * B + C -> FusedMatMulAdd(A, B, C)
FuseMatMulAdd: TensorAdd(MatMul(?a, ?b), ?c) -> FusedMatMulAdd(?a, ?b, ?c)

# Fused MatMul+Add+Relu
# Relu(A * B + C) -> Relu(FusedMatMulAdd(A, B, C)) -> FusedMatMulAddRelu(A, B, C)
FuseMatMulAddRelu1: Relu(FusedMatMulAdd(?a, ?b, ?c)) -> FusedMatMulAddRelu(?a, ?b, ?c)

# Direct path if the intermediate fusion didn't happen yet (though EGraph should find it)
FuseMatMulAddRelu2: Relu(TensorAdd(MatMul(?a, ?b), ?c)) -> FusedMatMulAddRelu(?a, ?b, ?c)

# Conv2D Fusion
# Relu(Conv2D(Input, Filter, Stride, Padding)) -> FusedConv2DRelu(...)
Relu(Conv2D(?input, ?filter, ?stride, ?padding)) -> FusedConv2DRelu(?input, ?filter, ?stride, ?padding)

# 3. Optimization Rules (Hoisting/Ordering)

# Hoisting Transpose through MatMul if both args are transposed
# A^T * B^T -> (B * A)^T
MatMul(Transpose(?a), Transpose(?b)) -> Transpose(MatMul(?b, ?a))

# Relu Commutation with Transpose
# Allows Relu to move inside Transpose to enable fusion with generating ops (e.g. Conv2D, MatMul).
Relu(Transpose(?a)) -> Transpose(Relu(?a))
Transpose(Relu(?a)) -> Relu(Transpose(?a))

# Relu Idempotence
# Relu(Relu(x)) -> Relu(x)
Relu(Relu(?a)) -> Relu(?a)

# Relu Zero
# Relu(0) -> 0
Relu(0) -> 0

# 4. Matrix Inverse Identities

# Inverse of Inverse: (A^-1)^-1 -> A
inverse(inverse(?a)) -> ?a

# Inverse of Product: (AB)^-1 -> B^-1 A^-1
inverse(MatMul(?a, ?b)) -> MatMul(inverse(?b), inverse(?a))

# Inverse of Transpose: (A^T)^-1 -> (A^-1)^T
inverse(Transpose(?a)) -> Transpose(inverse(?a))
Transpose(inverse(?a)) -> inverse(Transpose(?a))

# 5. Equation Isolation Rules
# Allows solving for variables in tensor equations

IsolateTranspose: Equality(Transpose(?a), ?b) -> Equality(?a, Transpose(?b))
IsolateTensorAddLeft: Equality(TensorAdd(?a, ?b), ?c) -> Equality(?a, TensorAdd(?c, Multiply(-1, ?b)))
IsolateTensorAddRight: Equality(TensorAdd(?a, ?b), ?c) -> Equality(?b, TensorAdd(?c, Multiply(-1, ?a)))

# 6. Weight Sliding and Scalar Commutation
# Allows scalar weights (g) to move inside/outside MatMul to enable factoring.
# We prefer sliding to the left (towards activation) to avoid materializing large weighted weights.
MatMulWeightSlideLeft: MatMul(TensorMul(?a, ?g), ?b) -> TensorMul(MatMul(?a, ?b), ?g)
TensorMulMatMulSlideLeft: TensorMul(MatMul(?a, ?b), ?g) -> MatMul(TensorMul(?a, ?g), ?b)
MatMulWeightSlideRight: MatMul(?a, TensorMul(?b, ?g)) -> TensorMul(MatMul(?a, ?b), ?g)
TensorMulMatMulSlideRight: TensorMul(MatMul(?a, ?b), ?g) -> MatMul(?a, TensorMul(?b, ?g))

# Slide both simultaneously
MatMulWeightSlideBoth: MatMul(TensorMul(?a, ?g1), Transpose(TensorMul(?b, ?g2))) -> TensorMul(MatMul(?a, Transpose(?b)), TensorMul(?g1, Transpose(?g2)))

# TensorMul Identities
TensorMulAssoc: TensorMul(TensorMul(?a, ?b), ?c) -> TensorMul(?a, TensorMul(?b, ?c))
TensorMulComm: TensorMul(?a, ?b) -> TensorMul(?b, ?a)

# Multiply Compatibility Rules (for Scale Folding)
MatMulWeightSlideLeft_Mul: MatMul(Multiply(?a, ?g), ?b) -> Multiply(MatMul(?a, ?b), ?g)
MultiplyMatMulSlideLeft: Multiply(MatMul(?a, ?b), ?g) -> MatMul(Multiply(?a, ?g), ?b)
MatMulWeightSlideRight_Mul: MatMul(?a, Multiply(?b, ?g)) -> Multiply(MatMul(?a, ?b), ?g)
MultiplyMatMulSlideRight: Multiply(MatMul(?a, ?b), ?g) -> MatMul(?a, Multiply(?b, ?g))
TransposeElemMul_Mul: Transpose(Multiply(?a, ?b)) -> Multiply(Transpose(?a), Transpose(?b))

# Transpose Identities
TransposeTranspose: Transpose(Transpose(?a)) -> ?a
TransposeMatMul: Transpose(MatMul(?a, ?b)) -> MatMul(Transpose(?b), Transpose(?a))
TransposeElemMul: Transpose(TensorMul(?a, ?b)) -> TensorMul(Transpose(?a), Transpose(?b))

# 8. Advanced Factoring Identities
# Direct shortcuts for dual-end factoring, bypassing local fusion minima.
# Dual-end factoring: (A*W)*g1 + (B*W)*g2 -> (A*g1 + B*g2) * W
FactorDualRight: TensorAdd(MatMul(?a, ?W), MatMul(?b, ?W)) -> MatMul(TensorAdd(?a, ?b), ?W)
FactorDualLeft: TensorAdd(MatMul(?W, ?a), MatMul(?W, ?b)) -> MatMul(?W, TensorAdd(?a, ?b))
FactorWeightedDualRight: TensorAdd(TensorMul(MatMul(?a, ?W), ?g1), TensorMul(MatMul(?b, ?W), ?g2)) -> MatMul(TensorAdd(TensorMul(?a, ?g1), TensorMul(?b, ?g2)), ?W)
FactorWeightedDualLeft: TensorAdd(TensorMul(MatMul(?W, ?a), ?g1), TensorMul(MatMul(?W, ?b), ?g2)) -> MatMul(?W, TensorAdd(TensorMul(?a, ?g1), TensorMul(?b, ?g2)))

# Mixed Weighted/Unweighted Factoring
FactorWeightedMatMulRight: TensorAdd(MatMul(?a, ?c), TensorMul(MatMul(?b, ?c), ?g)) -> MatMul(TensorAdd(?a, TensorMul(?b, ?g)), ?c)
FactorWeightedMatMulLeft: TensorAdd(MatMul(?c, ?a), TensorMul(MatMul(?c, ?b), ?g)) -> MatMul(?c, TensorAdd(?a, TensorMul(?b, ?g)))
FactorWeightedMatMulRight2: TensorAdd(TensorMul(MatMul(?a, ?c), ?g), MatMul(?b, ?c)) -> MatMul(TensorAdd(TensorMul(?a, ?g), ?b), ?c)
FactorWeightedMatMulLeft2: TensorAdd(TensorMul(MatMul(?c, ?a), ?g), MatMul(?c, ?b)) -> MatMul(?c, TensorAdd(TensorMul(?a, ?g), ?b))

# Factoring across Fused operations
FactorFusedRight: TensorAdd(FusedMatMulAdd(?a, ?c, ?d), FusedMatMulAdd(?b, ?c, ?e)) -> FusedMatMulAdd(TensorAdd(?a, ?b), ?c, TensorAdd(?d, ?e))
FactorFusedLeft: TensorAdd(FusedMatMulAdd(?a, ?b, ?d), FusedMatMulAdd(?a, ?c, ?e)) -> FusedMatMulAdd(?a, TensorAdd(?b, ?c), TensorAdd(?d, ?e))

# 9. Stacking and Concatenation
# Transforms multiple weighted operations into a single stacked operation.
# axis=0 for vertical stacking
StackWeightedSum: TensorAdd(TensorMul(?r1, ?w1), TensorMul(?r2, ?w2)) -> MatMul(Stack(0, ?w1, ?w2), Stack(0, ?r1, ?r2))
StackWeightedSum3: TensorAdd(TensorAdd(TensorMul(?r1, ?w1), TensorMul(?r2, ?w2)), TensorMul(?r3, ?w3)) -> MatMul(Stack(0, ?w1, ?w2, ?w3), Stack(0, ?r1, ?r2, ?r3))

# Fusion rules
FuseMatMulAdd: TensorAdd(MatMul(?a, ?b), ?c) -> FusedMatMulAdd(?a, ?b, ?c)
FuseMatMulAddComm: TensorAdd(?c, MatMul(?a, ?b)) -> FusedMatMulAdd(?a, ?b, ?c)

# 11. Summation and Reduction
SumDistribute: Sum(TensorAdd(?a, ?b)) -> TensorAdd(Sum(?a), Sum(?b))
SumFactor: Sum(TensorMul(?a, ?g)) -> TensorMul(Sum(?a), ?g)

# Squared Difference Expansion (L2 Distance optimization)
ExpandL2Distance: Sum(Pow(TensorAdd(?a, TensorMul(?b, -1)), 2)) -> TensorAdd(TensorAdd(Sum(Pow(?a, 2)), TensorMul(Sum(TensorMul(?a, ?b)), -2)), Sum(Pow(?b, 2)))
ExpandSquaredDiff: Pow(TensorAdd(?a, TensorMul(?b, -1)), 2) -> TensorAdd(TensorAdd(Pow(?a, 2), TensorMul(TensorMul(?a, ?b), -2)), Pow(?b, 2))
# Allows the EGraph to see through fused operations for global optimization.
UnfuseMatMulAdd: FusedMatMulAdd(?a, ?b, ?c) -> TensorAdd(MatMul(?a, ?b), ?c)
FactorFusedMatMulRight: FusedMatMulAdd(?a, ?b, MatMul(?c, ?b)) -> MatMul(TensorAdd(?a, ?c), ?b)
FactorFusedWeightedMatMulRight: FusedMatMulAdd(?a, ?b, TensorMul(MatMul(?c, ?b), ?g)) -> MatMul(TensorAdd(?a, TensorMul(?c, ?g)), ?b)
""")
                }),
            ["Trigonometry"] = new(
                "Trigonometry",
                "Trigonometry",
                "Trigonometric identities and special-value rules.",
                true,
                150,
                new[]
                {
                    File("cos.rule", """
# Cosine special values
CosZero: cos(0) -> 1
CosHalfPi: cos(pi/2) -> 0
"""),
                    File("degree_identities.rule", """
# SinToCosDeg: sin(a) -> cos(90 - a)
# CosToSinDeg: cos(a) -> sin(90 - a)
TanToCotDeg: tan(a) -> cot(90 - a)
Sin180Minus: sin(180 - a) -> sin(a)
Cos180Minus: cos(180 - a) -> -cos(a)
"""),
                    File("double_angle.rule", """
# Double angle identities
SinDouble: sin(2*x) -> 2*sin(x)*cos(x)
CosDouble: cos(2*x) -> cos(x)^2 - sin(x)^2
TanDouble: tan(2*x) -> 2*tan(x) / (1 - tan(x)^2)
"""),
                    File("parity.rule", """
# Trigonometric parity identities
SinOdd: sin(-a) -> -sin(a)
CosEven: cos(-a) -> cos(a)
TanOdd: tan(-a) -> -tan(a)
"""),
                    File("product_to_sum.rule", """
SinACosB: sin(a) * cos(b) -> 0.5 * (sin(a + b) + sin(a - b))
CosASinB: cos(a) * sin(b) -> 0.5 * (sin(a + b) - sin(a - b))
CosACosB: cos(a) * cos(b) -> 0.5 * (cos(a + b) + cos(a - b))
SinASinB: sin(a) * sin(b) -> 0.5 * (cos(a - b) - cos(a + b))
"""),
                    File("pythagorean.rule", """
Pythagorean: Pow(sin(x),2) + Pow(cos(x),2) -> 1
PythagoreanPlus: a + Pow(sin(x),2) + Pow(cos(x),2) -> a + 1
"""),
                    File("sin.rule", """
SinZero: sin(0) -> 0
SinHalfPi: sin(pi/2) -> 1
"""),
                    File("sum_of_angles.rule", """
CosCosMinusSinSin: cos(a) * cos(b) - sin(a) * sin(b) -> cos(a + b)
CosCosPlusSinSin: cos(a) * cos(b) + sin(a) * sin(b) -> cos(a - b)
SinCosPlusCosSin: sin(a) * cos(b) + cos(a) * sin(b) -> sin(a + b)
SinCosMinusCosSin: sin(a) * cos(b) - cos(a) * sin(b) -> sin(a - b)
"""),
                    File("tan_cot.rule", """
# Tangent and Cotangent identities
TanDef: tan(x) -> sin(x) / cos(x)
CotDef: cot(x) -> cos(x) / sin(x)
TanZero: tan(0) -> 0
CotHalfPi: cot(pi/2) -> 0
TanHalfPi: tan(pi/2) -> Infinity
CotZero: cot(0) -> Infinity
TanParity: tan(-x) -> -tan(x)
CotParity: cot(-x) -> -cot(x)
TanPeriodic: tan(x + pi) -> tan(x)
CotPeriodic: cot(x + pi) -> cot(x)
""")
                }),
            ["Vector"] = new(
                "Vector",
                "Vector",
                "Vector and dot-product rewrite rules.",
                true,
                160,
                new[]
                {
                    File("dot_bilinear.rule", """
# Bilinear expansion for dot products to enable distribution on both operands.
DotBilinear: DotProduct(a + b, c + d) -> DotProduct(a, c) + DotProduct(a, d) + DotProduct(b, c) + DotProduct(b, d)
DotNegateLeft: DotProduct(-a, b) -> -DotProduct(a, b)
DotNegateRight: DotProduct(a, -b) -> -DotProduct(a, b)
"""),
                    File("dot_distribute.rule", """
DotDistribute: DotProduct(a + b, c) -> DotProduct(a, c) + DotProduct(b, c)
DotDistributeRight: DotProduct(a, b + c) -> DotProduct(a, b) + DotProduct(a, c)
"""),
                    File("dot.rule", """
DotProductZeroRight: DotProduct(a, 0) -> 0
DotProductZeroLeft: DotProduct(0, a) -> 0
""")
                })
        };

    public static IReadOnlyList<RulePackInfo> GetPackInfos()
    {
        return Packs.Values.Select(static pack => pack.ToPackInfo()).ToList();
    }

    public static bool TryGetPack(string nameOrPath, out EmbeddedRulePackDescriptor? pack)
    {
        pack = null;
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return false;
        }

        var normalized = nameOrPath.Replace('\\', '/').TrimEnd('/');
        var lastSegment = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(lastSegment))
        {
            lastSegment = normalized;
        }

        if (Packs.TryGetValue(lastSegment, out pack))
        {
            return true;
        }

        pack = Packs.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Name, lastSegment, StringComparison.OrdinalIgnoreCase));

        return pack is not null;
    }

    public static IReadOnlyList<RuleDefinition> LoadRules(string nameOrPath)
    {
        if (!TryGetPack(nameOrPath, out var pack) || pack is null)
        {
            return Array.Empty<RuleDefinition>();
        }

        var rules = new List<RuleDefinition>();
        foreach (var file in pack.Files)
        {
            rules.AddRange(RuleLoader.LoadRulesFromText(file.Content, Path.GetFileNameWithoutExtension(file.FileName)));
        }

        return rules;
    }

    private static EmbeddedRuleTextFile File(string fileName, string content)
    {
        return new EmbeddedRuleTextFile(fileName, content);
    }
}
