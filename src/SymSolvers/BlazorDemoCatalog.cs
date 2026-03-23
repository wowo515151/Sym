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

    public static IReadOnlyList<BlazorDemoExample> AllExamples { get; } =
        DerivativeExamples.Concat(RewriteExamples).Concat(TensorExamples).ToArray();
}
