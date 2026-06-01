namespace SymSolvers;

public sealed record BlazorDemoExample(string Name, string Script, string Description, string? ExpectedSubstring = null);

public static class BlazorDemoCatalog
{
    public static IReadOnlyList<BlazorDemoExample> DerivativeExamples { get; } =
    [
        new(
            "Exponential Derivative",
            """
            <Options>
              RulePacks: Calculus, AlgebraicStrategy
            </Options>

            Derivative(exp(x), x)
            """,
            "A stable calculus baseline showing exact differentiation of an exponential expression.",
            "exp"
        ),
        new(
            "Cosine Derivative",
            """
            <Options>
              RulePacks: Calculus, AlgebraicStrategy
            </Options>

            Derivative(cos(x), x)
            """,
            "A stable trigonometric derivative example that exercises an exact calculus rewrite.",
            "sin"
        ),
        new(
            "Product Rule Mix",
            """
            <Options>
              RulePacks: Calculus, AlgebraicStrategy
            </Options>

            Derivative(sin(x) * exp(x), x)
            """,
            "A mixed product example that typically expands into multiple symbolic terms before simplification."
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> RewriteExamples { get; } =
    [
        new(
            "Trig Identity",
            """
            <Options>
              RulePacks: Trigonometry, AlgebraicStrategy
              MaxIterations: 50
            </Options>

            sin(x)^2 + cos(x)^2
            """,
            "A classic trigonometric identity that should collapse to a constant result.",
            "1"
        ),
        new(
            "Zero Cancellation",
            """
            <Options>
              RulePacks: AlgebraicStrategy
            </Options>

            x - x
            """,
            "A minimal normalization example showing cancellation under an algebraic strategy.",
            "0"
        ),
        new(
            "Inline Rule With Target",
            """
            <Options>
              RulePacks: EquationSolving, AlgebraicStrategy
              Target: y
            </Options>

            <Rules>
              Rule(f(Wild("x")), Pow(Wild("x"), 2));
            </Rules>

            f(5) = y
            """,
            "A custom-rule example that rewrites a function call before the equation solver isolates the target.",
            "25"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> TensorExamples { get; } =
    [
        new(
            "Simple Fusion",
            """
            <Options>
              RulePacks: Tensor
              CostModel: Tensor
              MaxIterations: 100
            </Options>

            Relu(TensorAdd(MatMul(A, B), C))
            """,
            "A fusion baseline that should prefer a tensor-oriented operator form for matmul plus bias plus activation.",
            "FusedMatMulAddRelu"
        ),
        new(
            "Double Transpose Cleanup",
            """
            <Options>
              RulePacks: Tensor
              CostModel: Tensor
              MaxIterations: 100
            </Options>

            Transpose(Transpose(MatMul(A, B)))
            """,
            "A structural tensor simplification example that removes redundant transpose wrappers."
        ),
        new(
            "Advanced Tensor Saturation",
            """
            <Options>
              RulePacks: Tensor
              CostModel: Tensor
              MaxIterations: 100
            </Options>

            Transpose(Transpose(Relu(TensorAdd(MatMul(A, B), C))))
            """,
            "A tensor example that combines redundant-structure cleanup with fused extraction, but stays light enough for an interactive demo.",
            "FusedMatMulAddRelu"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> IntegrationExamples { get; } =
    [
        new(
            "Polynomial Antiderivative",
            """
            <Options>
              EnableTracing: true
            </Options>

            Integral(x^3 + 2*x, x)
            """,
            "A simple exact antiderivative that shows the integration strategy handling polynomial structure directly.",
            "Pow(x, 4)"
        ),
        new(
            "By Parts With Exponential",
            """
            <Options>
              EnableTracing: true
            </Options>

            Integral(x * exp(x), x)
            """,
            "A stable engine-backed example of integration by parts where the symbolic answer remains inspectable.",
            "exp"
        ),
        new(
            "Arctangent Form",
            """
            <Options>
              EnableTracing: true
            </Options>

            Integral(1 / (x^2 + 4), x)
            """,
            "A standard exact integral that lands in an inverse-trigonometric form instead of a numeric approximation.",
            "atan"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> LimitExamples { get; } =
    [
        new(
            "Sinc Limit",
            """
            <Options>
              EnableTracing: true
            </Options>

            Limit(sin(x) / x, x, 0)
            """,
            "A classic exact limit showing Sym's limit workflow on a foundational calculus expression.",
            "1"
        ),
        new(
            "Exponential Series Limit",
            """
            <Options>
              EnableTracing: true
            </Options>

            Limit((exp(x) - 1 - x) / x^2, x, 0)
            """,
            "A limit that exercises the strategy's series and L'Hopital-style reasoning paths.",
            "0.5"
        ),
        new(
            "Cosine Cancellation Limit",
            """
            <Options>
              EnableTracing: true
            </Options>

            Limit((1 - cos(x)) / x^2, x, 0)
            """,
            "A cancellation-heavy limit that is useful for checking exact rather than purely numeric handling.",
            "0.5"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> LinearAlgebraExamples { get; } =
    [
        new(
            "Determinant Of A 2x2 Matrix",
            """
            determinant(Matrix(Vector(4, 7), Vector(2, 6)))
            """,
            "A minimal linear-algebra example that uses the exact matrix strategy rather than a separate numeric widget.",
            "10"
        ),
        new(
            "Matrix Equality Expansion",
            """
            Matrix(Vector(a, b), Vector(c, d)) = Matrix(Vector(1, 2), Vector(3, 4))
            """,
            "A component-wise matrix equality example that turns one matrix statement into exact scalar equalities.",
            "a = 1"
        ),
        new(
            "Solve A Small Linear System",
            """
            MatrixMultiply(Matrix(Vector(2, 1), Vector(1, -1)), Vector(x, y)) = Vector(5, 1)
            """,
            "A small exact linear system solved from matrix form into symbolic assignments for the target vector entries.",
            "x = 2"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> OdeExamples { get; } =
    [
        new(
            "Exponential Growth ODE",
            """
            Derivative(y(x), x) = y(x)
            """,
            "A first-order differential equation that should resolve to an exponential-family symbolic solution.",
            "exp"
        ),
        new(
            "Polynomial Right-Hand Side ODE",
            """
            Derivative(y(x), x) = x
            """,
            "A simple first-order equation with a direct polynomial right-hand side, useful for checking exact closed-form output.",
            "C1"
        ),
        new(
            "Second-Order Constant-Coefficient ODE",
            """
            Derivative(Derivative(y(x), x), x) - y(x) = 0
            """,
            "A simple higher-order constant-coefficient equation that exercises the closed-form ODE path.",
            "C1"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> RecurrenceExamples { get; } =
    [
        new(
            "Doubling Recurrence",
            """
            a(n) = 2 * a(n - 1)
            a(0) = 3
            a(5)
            """,
            "A recurrence demo that exposes the discrete solver's target-term path, even when coefficient recovery stays in symbolic matrix form.",
            "32 *"
        ),
        new(
            "Second-Order Linear Recurrence",
            """
            a(n) = 3 * a(n - 1) - 2 * a(n - 2)
            a(0) = 1
            a(1) = 2
            a(5)
            """,
            "A higher-order constant-coefficient recurrence with enough initial data to build an exact closed form.",
            "32"
        ),
        new(
            "Fibonacci-Style Target Query",
            """
            a(n) = a(n - 1) + a(n - 2)
            a(0) = 0
            a(1) = 1
            a(7)
            """,
            "A familiar recurrence that demonstrates how the app can target one requested term instead of printing an entire derivation.",
            "13"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> SeriesExamples { get; } =
    [
        new(
            "Sine Maclaurin Series",
            """
            SeriesExpansion(sin(x), x, 0, 5)
            """,
            "A compact exact series-expansion example that keeps the symbolic terms visible instead of hiding them behind numerics.",
            "Pow(x, 5)"
        ),
        new(
            "Log One Plus X Series",
            """
            SeriesExpansion(log(1 + x), x, 0, 4)
            """,
            "A standard series that is useful for AI/math workflows where local approximation structure matters.",
            "Pow(x, 2)"
        ),
        new(
            "Sinc Reconstruction Series",
            """
            SeriesExpansion(sin(x) / x, x, 0, 5)
            """,
            "A good bridge between exact series reasoning and the corresponding limit workflow.",
            "Pow(x, 4)"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> InequalityExamples { get; } =
    [
        new(
            "Linear Inequality Isolation",
            """
            le(3*x + 5, 11)
            """,
            "A direct inequality example that exposes the existing isolation strategy through the browser.",
            "x"
        ),
        new(
            "Interval From A Conjunction",
            """
            and(le(x, 5), ge(x, 1))
            """,
            "A compact domain-style example that turns two inequalities into a single interval result.",
            "[1, 5]"
        ),
        new(
            "Guarded Piecewise Inequality",
            """
            and(le(x, 5), ge(y, 2))
            """,
            "A mixed symbolic condition showing that inequality solving can preserve guards instead of flattening everything away.",
            "piecewise"
        )
    ];

    public static IReadOnlyList<BlazorDemoExample> AllExamples { get; } =
        DerivativeExamples
            .Concat(RewriteExamples)
            .Concat(TensorExamples)
            .Concat(IntegrationExamples)
            .Concat(LimitExamples)
            .Concat(LinearAlgebraExamples)
            .Concat(OdeExamples)
            .Concat(RecurrenceExamples)
            .Concat(SeriesExamples)
            .Concat(InequalityExamples)
            .ToArray();
}
