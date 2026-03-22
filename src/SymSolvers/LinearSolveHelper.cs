using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

/// <summary>
/// Shared Gaussian elimination helper for decimal and Rational systems.
/// </summary>
internal static class LinearSolveHelper
{
    public static decimal[]? Solve(decimal[,] matrix, decimal[] vector, out string? failureReason, CancellationToken ct = default)
    {
        failureReason = null;
        const decimal epsilon = 1e-18m;
        return SolveCore(
            matrix,
            vector,
            0m,
            v => Math.Abs(v) < epsilon,
            (a, b) => a + b,
            (a, b) => a - b,
            (a, b) => a * b,
            (a, b) => a / b,
            v => Math.Abs((double)v),
            out failureReason,
            ct);
    }

    public static Rational[]? Solve(Rational[,] matrix, Rational[] vector, out string? failureReason, CancellationToken ct = default)
    {
        failureReason = null;
        return SolveCore(
            matrix,
            vector,
            Rational.Zero,
            v => v.IsZero,
            (a, b) => a + b,
            (a, b) => a - b,
            (a, b) => a * b,
            (a, b) => a / b,
            _ => (double?)null,
            out failureReason,
            ct);
    }

    private static T[]? SolveCore<T>(
        T[,] matrix,
        T[] vector,
        T zero,
        Func<T, bool> isZero,
        Func<T, T, T> add,
        Func<T, T, T> subtract,
        Func<T, T, T> multiply,
        Func<T, T, T> divide,
        Func<T, double?> magnitude,
        out string? failureReason,
        CancellationToken ct = default)
    {
        failureReason = null;
        var rowCount = vector.Length;
        var colCount = matrix.GetLength(1);

        if (matrix.GetLength(0) != rowCount)
        {
            failureReason = "Matrix row count must match the RHS dimension.";
            return null;
        }

        var augmented = new T[rowCount, colCount + 1];
        for (int i = 0; i < rowCount; i++)
        {
            for (int j = 0; j < colCount; j++)
            {
                augmented[i, j] = matrix[i, j];
            }
            augmented[i, colCount] = vector[i];
        }

        int pivotRow = 0;
        int pivotCol = 0;

        while (pivotRow < rowCount && pivotCol < colCount)
        {
            ct.ThrowIfCancellationRequested();
            int bestRow = -1;
            double bestMagnitude = -1;

            for (int r = pivotRow; r < rowCount; r++)
            {
                var val = augmented[r, pivotCol];
                if (isZero(val)) continue;

                var mag = magnitude(val) ?? 1d;
                if (mag > bestMagnitude)
                {
                    bestMagnitude = mag;
                    bestRow = r;
                }
            }

            if (bestRow == -1)
            {
                pivotCol++;
                continue;
            }

            // Swap rows
            if (bestRow != pivotRow)
            {
                for (int j = pivotCol; j <= colCount; j++)
                {
                    (augmented[pivotRow, j], augmented[bestRow, j]) = (augmented[bestRow, j], augmented[pivotRow, j]);
                }
            }

            // Normalize pivot row
            var pivotVal = augmented[pivotRow, pivotCol];
            for (int j = pivotCol; j <= colCount; j++)
            {
                augmented[pivotRow, j] = divide(augmented[pivotRow, j], pivotVal);
            }

            // Eliminate other rows
            for (int r = 0; r < rowCount; r++)
            {
                if (r == pivotRow) continue;
                var factor = augmented[r, pivotCol];
                if (isZero(factor)) continue;

                for (int j = pivotCol; j <= colCount; j++)
                {
                    var scaled = multiply(factor, augmented[pivotRow, j]);
                    augmented[r, j] = subtract(augmented[r, j], scaled);
                }
            }

            pivotRow++;
            pivotCol++;
        }

        // Check for consistency
        for (int r = pivotRow; r < rowCount; r++)
        {
            ct.ThrowIfCancellationRequested();
            var residual = augmented[r, colCount];
            // Use a slightly larger epsilon for the final consistency check
            if (!isZero(residual) && magnitude(residual) > 1e-12)
            {
                failureReason = "System is inconsistent.";
                return null;
            }
        }

        // If we have free variables (pivotCol < colCount), we don't have a unique solution.
        // For our purposes, we only want a unique solution.
        if (pivotRow < colCount)
        {
            failureReason = "System has infinitely many solutions (underdetermined).";
            return null;
        }

        var solution = new T[colCount];
        for (int i = 0; i < colCount; i++)
        {
            solution[i] = augmented[i, colCount];
        }
        return solution;
    }

    public static Matrix? TryExtractMatrix(Vector vector, Vector variables, CancellationToken ct = default)
    {
        int rowCount = vector.Arguments.Count;
        int colCount = variables.Arguments.Count;
        
        var matrixComponents = ImmutableList.CreateBuilder<IExpression>();
        
        for (int i = 0; i < rowCount; i++)
        {
            var component = vector.Arguments[i];
            for (int j = 0; j < colCount; j++)
            {
                ct.ThrowIfCancellationRequested();
                var variable = variables.Arguments[j] as Symbol;
                if (variable == null) return null;
                
                // Extract coefficient of variable in component
                if (!TryGetLinearCoefficient(component, variable, out var coeff))
                {
                    return null;
                }
                matrixComponents.Add(coeff);
            }
        }
        
        return new Matrix(ImmutableArray.Create(rowCount, colCount), matrixComponents.ToImmutable());
    }

    private static bool TryGetLinearCoefficient(IExpression expr, Symbol variable, out IExpression coefficient)
    {
        coefficient = new Number(0);
        
        if (expr is Symbol s)
        {
            if (s.Name == variable.Name)
            {
                coefficient = new Number(1);
                return true;
            }
            coefficient = new Number(0);
            return true;
        }
        
        if (expr is Number)
        {
            coefficient = new Number(0);
            return true;
        }
        
        if (expr is Add add)
        {
            var coeffs = new List<IExpression>();
            foreach (var arg in add.Arguments)
            {
                if (!TryGetLinearCoefficient(arg, variable, out var c)) return false;
                coeffs.Add(c);
            }
            coefficient = new Add(coeffs.ToImmutableList()).Canonicalize();
            return true;
        }
        
        if (expr is Multiply mul)
        {
            // Term should be constant * variable or just constant
            IExpression? varTerm = null;
            var constantTerms = new List<IExpression>();
            
            foreach (var arg in mul.Arguments)
            {
                if (arg.ContainsSymbol(s => s.Name == variable.Name))
                {
                    if (varTerm != null) return false; // Non-linear
                    varTerm = arg;
                }
                else
                {
                    constantTerms.Add(arg);
                }
            }
            
            if (varTerm == null)
            {
                coefficient = new Number(0);
                return true;
            }
            
            if (varTerm is Symbol s2 && s2.Name == variable.Name)
            {
                coefficient = constantTerms.Count == 1 ? constantTerms[0] : new Multiply(constantTerms.ToImmutableList()).Canonicalize();
                return true;
            }
            
            return false;
        }

        if (expr is Subtract sub)
        {
            if (TryGetLinearCoefficient(sub.LeftOperand, variable, out var c1) &&
                TryGetLinearCoefficient(sub.RightOperand, variable, out var c2))
            {
                coefficient = new Subtract(c1, c2).Canonicalize();
                return true;
            }
            return false;
        }
        
        if (!expr.ContainsSymbol(s => s.Name == variable.Name))
        {
            coefficient = new Number(0);
            return true;
        }
        
        return false;
    }
}
