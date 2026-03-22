// Copyright Warren Harding 2026
using System;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Solves small numeric linear systems of the form Matrix * Vector = Vector.
/// </summary>
public class LinearAlgebraStrategy : ISolverStrategy, INamedSolverStrategy
{
    public string Name => "LinearAlgebraStrategy";
    private readonly RulePackStrategy _rulePackStrategy;

    public LinearAlgebraStrategy()
    {
        var packs = SymRules.RulePackLibrary.GetRulePacks();
        var pack = packs.FirstOrDefault(p => p.Name == "MatrixStrategy");
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
            if (res.IsSuccess && res.ResultExpression != null && !res.ResultExpression.InternalEquals(current))
            {
                current = res.ResultExpression;
                // If we got a result that is no longer a Matrix operation, we might be done.
                if (current is Number or Vector or Matrix) 
                {
                     return SolveResult.Success(current, "LinearAlgebraStrategy applied rules.");
                }
            }
        }

        if (current is not Equality equality) return SolveResult.Failure(problem, "LinearAlgebraStrategy primarily supports matrix equalities.");

        // General matrix equality expansion: Matrix == Matrix
        if (equality.LeftOperand is Matrix mLeft && equality.RightOperand is Matrix mRight)
        {
            if (mLeft.Arguments.Count == mRight.Arguments.Count && mLeft.MatrixDimensions.SequenceEqual(mRight.MatrixDimensions))
            {
                var components = new List<IExpression>();
                for (int i = 0; i < mLeft.Arguments.Count; i++)
                {
                    components.Add(new Equality(mLeft.Arguments[i], mRight.Arguments[i]).Canonicalize());
                }
                return SolveResult.Success(new Vector(components.ToImmutableList()).Canonicalize(), "Expanded matrix equality component-wise.");
            }
        }

        if (equality.LeftOperand is not MatrixMultiply matrixMultiply ||
            matrixMultiply.LeftOperand is not Matrix matrix ||
            matrixMultiply.RightOperand is not Vector variables ||
            equality.RightOperand is not Vector rhsVector)
        {
            return SolveResult.Failure(problem, "LinearAlgebraStrategy supports equations of the form Matrix*Vector = Vector with numeric components, Matrix powers, inverses, and determinants.");
        }


        if (!matrix.Shape.IsValid || !variables.Shape.IsValid || !rhsVector.Shape.IsValid)
        {
            return SolveResult.Failure(problem, "Invalid matrix or vector shape.");
        }

        var dimensions = matrix.MatrixDimensions;
        if (dimensions.Length < 2 || dimensions[0] != dimensions[1])
        {
            return SolveResult.Failure(problem, "Only square matrices are supported.");
        }

        var n = dimensions[0];
        if (rhsVector.Arguments.Count != n)
        {
            return SolveResult.Failure(problem, "Right-hand side vector length mismatch.");
        }

        // Try exact rational solve first for better precision on integer/rational systems.
        if (TrySolveRational(matrix, rhsVector, n, out var rationalResult, out var rationalError, context.CancellationToken))
        {
            var assignments = new List<IExpression>();
            for (int i = 0; i < n; i++)
            {
                assignments.Add(new Equality(variables.Arguments[i], new Number(rationalResult![i].ToDecimal())).Canonicalize());
            }
            var vectorResult = new Vector(assignments.ToImmutableList()).Canonicalize();
            var trace = context.EnableTracing ? ImmutableList.Create(problem, vectorResult) : null;
            return SolveResult.Success(vectorResult, "Solved linear system exactly via Rational arithmetic.", trace);
        }

        var coefficients = new decimal[n, n];
        var rhs = new decimal[n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (NumericEvaluator.TryEvaluate(matrix.Arguments[i * n + j], ImmutableDictionary<string, decimal>.Empty, out decimal val, out _))
                {
                    coefficients[i, j] = val;
                }
                else
                {
                    return SolveResult.Failure(problem, "Non-numeric matrix components are not supported.");
                }
            }

            if (NumericEvaluator.TryEvaluate(rhsVector.Arguments[i], ImmutableDictionary<string, decimal>.Empty, out decimal rhsVal, out _))
            {
                rhs[i] = rhsVal;
            }
            else
            {
                return SolveResult.Failure(problem, "Non-numeric right-hand side entries are not supported.");
            }
        }

        var solution = LinearSolveHelper.Solve(coefficients, rhs, out string? failureReason, context.CancellationToken);
        if (solution is null)
        {
            var reason = string.IsNullOrWhiteSpace(failureReason) ? "No unique solution." : failureReason;
            return SolveResult.Failure(problem, reason);
        }

        var assignmentsDecimal = new List<IExpression>();
        for (int i = 0; i < n; i++)
        {
            assignmentsDecimal.Add(new Equality(variables.Arguments[i], new Number(solution[i])).Canonicalize());
        }
        var vectorResultDecimal = new Vector(assignmentsDecimal.ToImmutableList()).Canonicalize();
        var traceDecimal = context.EnableTracing ? ImmutableList.Create(problem, vectorResultDecimal) : null;
        return SolveResult.Success(vectorResultDecimal, "Solved linear system.", traceDecimal);
    }

    private IExpression? TryEvaluateMatrixAdd(Add add, CancellationToken ct)
    {
        var matrices = add.Arguments.OfType<Matrix>().ToList();
        if (matrices.Count < 1) return null;

        var dims = matrices[0].MatrixDimensions;
        if (dims.Length != 2) return null;
        if (matrices.Any(m => !m.MatrixDimensions.SequenceEqual(dims))) return null;

        int rows = dims[0];
        int cols = dims[1];
        int count = rows * cols;

        var nonMatrices = add.Arguments.Where(a => a is not Matrix).ToList();
        
        var resultComponents = ImmutableList.CreateBuilder<IExpression>();
        for (int i = 0; i < count; i++)
        {
            var terms = new List<IExpression>();
            foreach (var m in matrices)
            {
                terms.Add(m.Arguments[i]);
            }
            
            // If we have non-matrix terms (like 0), they are usually intended to be applied element-wise 
            // if it's a sum. But standard math says you can only add same-shaped matrices.
            // Some problems use Matrix + 0 or Matrix + MatrixOfZeros.
            // If it's a single scalar being added to a matrix, it might be intended as element-wise.
            // But we'll be conservative and only handle multiple matrices.
            
            var cellSum = new Add(terms.ToImmutableList()).Canonicalize();
            resultComponents.Add(SimplifyComponent(cellSum, ct));
        }

        IExpression resultMatrix = new Matrix(dims, resultComponents.ToImmutable());
        if (nonMatrices.Count > 0)
        {
            return new Add(nonMatrices.Concat(new[] { resultMatrix }).ToImmutableList()).Canonicalize();
        }
        return resultMatrix;
    }

    private IExpression? TryEvaluateMatrixScalarMul(Multiply mul, CancellationToken ct)
    {
        var matrices = mul.Arguments.OfType<Matrix>().ToList();
        if (matrices.Count != 1) return null; // We already handle Matrix-Matrix multiply via MatrixMultiply op

        var m = matrices[0];
        var dims = m.MatrixDimensions;
        var scalars = mul.Arguments.Where(a => a is not Matrix).ToList();
        if (scalars.Count == 0) return m;

        var scalar = scalars.Count == 1 ? scalars[0] : new Multiply(scalars.ToImmutableList()).Canonicalize();
        
        var resultComponents = ImmutableList.CreateBuilder<IExpression>();
        foreach (var arg in m.Arguments)
        {
            var cellMul = new Multiply(scalar, arg).Canonicalize();
            resultComponents.Add(SimplifyComponent(cellMul, ct));
        }

        return new Matrix(dims, resultComponents.ToImmutable());
    }

    private Matrix? TryEvaluateMatrixPower(Matrix m, int n, CancellationToken ct)
    {
        if (n < 0) return null; // Inverse power not supported yet
        if (n == 0) return MakeIdentity(m.MatrixDimensions[0]);
        if (n == 1) return m;

        var res = MakeIdentity(m.MatrixDimensions[0]);
        var sq = m;
        while (n > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (n % 2 == 1)
            {
                var next = TryMatrixMultiply(res, sq, ct);
                if (next is null) return null;
                res = next;
            }
            n /= 2;
            if (n > 0)
            {
                var nextSq = TryMatrixMultiply(sq, sq, ct);
                if (nextSq is null) return null;
                sq = nextSq;
            }
        }
        return res;
    }

    private Matrix MakeIdentity(int size)
    {
        var components = ImmutableList.CreateBuilder<IExpression>();
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                components.Add(new Number(i == j ? 1m : 0m));
            }
        }
        return new Matrix(ImmutableArray.Create(size, size), components.ToImmutable());
    }

    private Matrix? TryMatrixMultiply(Matrix m1, Matrix m2, CancellationToken ct)
    {
        if (m1.MatrixDimensions[1] != m2.MatrixDimensions[0]) return null;

        int rows = m1.MatrixDimensions[0];
        int common = m1.MatrixDimensions[1];
        int cols = m2.MatrixDimensions[1];

        var components = ImmutableList.CreateBuilder<IExpression>();
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                ct.ThrowIfCancellationRequested();
                var terms = new List<IExpression>();
                for (int k = 0; k < common; k++)
                {
                    var term = new Multiply(m1.Arguments[i * common + k], m2.Arguments[k * cols + j]).Canonicalize();
                    terms.Add(term);
                }
                var sum = new Add(terms.ToImmutableList()).Canonicalize();
                
                // Try to simplify the sum as much as possible
                sum = SimplifyComponent(sum, ct);
                components.Add(sum);
            }
        }

        // Attempt to convert numeric-looking components to Numbers for a clean Matrix result
        var finalComponents = components.ToImmutable();
        var converted = ImmutableList.CreateBuilder<IExpression>();
        foreach (var c in finalComponents)
        {
            // Extra aggressive simplification: try a fast canonicalize pass to reduce Adds/Subtracts/Negations
            var simple = c.Canonicalize();
            if (NumericEvaluator.TryEvaluate(simple, ImmutableDictionary<string, decimal>.Empty, out var v, out _))
            {
                converted.Add(new Number(v));
            }
            else
            {
                // If canonicalized form isn't numeric, attempt one more simplification pass via SimplifyComponent
                var better = SimplifyComponent(simple, ct);
                if (NumericEvaluator.TryEvaluate(better, ImmutableDictionary<string, decimal>.Empty, out var v2, out _))
                {
                    converted.Add(new Number(v2));
                }
                else
                {
                    converted.Add(better);
                }
            }
        }
        var resultMatrix = new Matrix(ImmutableArray.Create(rows, cols), converted.ToImmutable());
        if (!resultMatrix.Shape.IsValid)
        {
            // Emit a lightweight diagnostic to console to help debug failing tests
            Console.WriteLine($"DEBUG: Constructed Matrix invalid. Sample components types: {string.Join(", ", resultMatrix.Arguments.Take(6).Select(a => a.GetType().Name))}");
            try
            {
                for (int ii = 0; ii < Math.Min(6, resultMatrix.Arguments.Count); ii++)
                {
                    Console.WriteLine($"DEBUG: Component[{ii}] = {resultMatrix.Arguments[ii].ToDisplayString()} ({resultMatrix.Arguments[ii].GetType().Name})");
                }
            }
            catch { }
        }
        return resultMatrix;
    }

    private IExpression? TryEvaluateDeterminant(Matrix m, CancellationToken ct)
    {
        var dims = m.MatrixDimensions;
        if (dims.Length != 2 || dims[0] != dims[1]) return null;

        if (dims[0] == 2)
        {
            // ad - bc
            var a = m.Arguments[0];
            var b = m.Arguments[1];
            var c = m.Arguments[2];
            var d = m.Arguments[3];
            var res = new Subtract(new Multiply(a, d), new Multiply(b, c)).Canonicalize();
            return SimplifyComponent(res, ct);
        }

        if (dims[0] == 3)
        {
            // Sarrus rule or expansion by first row
            // a(ei - fh) - b(di - fg) + c(dh - eg)
            var a = m.Arguments[0]; var b = m.Arguments[1]; var c = m.Arguments[2];
            var d = m.Arguments[3]; var e = m.Arguments[4]; var f = m.Arguments[5];
            var g = m.Arguments[6]; var h = m.Arguments[7]; var i = m.Arguments[8];

            var term1 = new Multiply(a, new Subtract(new Multiply(e, i), new Multiply(f, h)));
            var term2 = new Multiply(b, new Subtract(new Multiply(d, i), new Multiply(f, g)));
            var term3 = new Multiply(c, new Subtract(new Multiply(d, h), new Multiply(e, g)));

            var res = new Add(term1, new Multiply(new Number(-1), term2), term3).Canonicalize();
            return SimplifyComponent(res, ct);
        }

        return null;
    }

    private Matrix? TryEvaluateInverse(Matrix m, CancellationToken ct)
    {
        var dims = m.MatrixDimensions;
        if (dims.Length != 2 || dims[0] != dims[1]) return null;

        if (dims[0] == 2)
        {
            // Inverse of [[a, b], [c, d]] is 1/(ad-bc) * [[d, -b], [-c, a]]
            var a = m.Arguments[0];
            var b = m.Arguments[1];
            var c = m.Arguments[2];
            var d = m.Arguments[3];

            var det = new Subtract(new Multiply(a, d), new Multiply(b, c)).Canonicalize();
            var detSimp = SimplifyComponent(det, ct);
            
            // Check for zero determinant
            if (detSimp is Number n && n.Value == 0m) return null;

            var invDet = new Divide(new Number(1), detSimp).Canonicalize();

            var components = ImmutableList.CreateBuilder<IExpression>();
            components.Add(SimplifyComponent(new Multiply(invDet, d), ct));
            components.Add(SimplifyComponent(new Multiply(invDet, new Multiply(new Number(-1), b)), ct));
            components.Add(SimplifyComponent(new Multiply(invDet, new Multiply(new Number(-1), c)), ct));
            components.Add(SimplifyComponent(new Multiply(invDet, a), ct));

            return new Matrix(ImmutableArray.Create(2, 2), components.ToImmutable());
        }

        return null;
    }

    private static IExpression SimplifyComponent(IExpression expr, CancellationToken ct)
    {
        var ctx = new SolveContext(null, ImmutableList<Rule>.Empty, 128, false, null, ct);
        var rules = RuleProvider.BuildRules(ctx);
        var runCtx = new SolveContext(null, rules, 128, false, null, ct);

        var current = expr.Canonicalize();
        var simplifier = new EGraphSolverStrategy();

        for (int i = 0; i < 3; i++)
        {
            var last = current;
            ct.ThrowIfCancellationRequested();
            var res = simplifier.Solve(current, runCtx);
            if (res.IsSuccess && res.ResultExpression is not null)
            {
                current = res.ResultExpression.Canonicalize();
            }
            if (current.InternalEquals(last)) break;
        }

        return current;
    }

    private bool TrySolveRational(Matrix matrix, Vector rhsVector, int n, out Rational[]? result, out string? error, CancellationToken ct = default)
    {
        result = null;
        error = null;
        var coefficients = new Rational[n, n];
        var rhs = new Rational[n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (TryEvaluateToRational(matrix.Arguments[i * n + j], out var val))
                {
                    coefficients[i, j] = val;
                }
                else return false;
            }
            if (TryEvaluateToRational(rhsVector.Arguments[i], out var rhsVal))
            {
                rhs[i] = rhsVal;
            }
            else return false;
        }

        result = LinearSolveHelper.Solve(coefficients, rhs, out error, ct);
        return result != null;
    }

    private bool TryEvaluateToRational(IExpression expr, out Rational result)
    {
        result = Rational.Zero;
        switch (expr)
        {
            case Number num:
                result = Rational.FromDecimal(num.Value);
                return true;
            case Add add:
                Rational sum = Rational.Zero;
                foreach (var arg in add.Arguments)
                {
                    if (TryEvaluateToRational(arg, out var val)) sum += val;
                    else return false;
                }
                result = sum;
                return true;
            case Subtract sub:
                if (TryEvaluateToRational(sub.LeftOperand, out var l) && TryEvaluateToRational(sub.RightOperand, out var r))
                {
                    result = l - r;
                    return true;
                }
                return false;
            case Multiply mul:
                Rational prod = Rational.One;
                foreach (var arg in mul.Arguments)
                {
                    if (TryEvaluateToRational(arg, out var val)) prod *= val;
                    else return false;
                }
                result = prod;
                return true;
            case Divide div:
                if (TryEvaluateToRational(div.Numerator, out var numRat) && TryEvaluateToRational(div.Denominator, out var denRat))
                {
                    if (denRat.IsZero) return false;
                    result = numRat / denRat;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

}
