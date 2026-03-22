// Copyright Warren Harding 2026
using System;

namespace SymSolvers.Numerics;

/// <summary>
/// Models a floating-point environment where intermediate results are rounded after each primitive operation.
/// Implementations should be deterministic to make stability scoring reproducible.
/// </summary>
public interface IFloatingPointModel
{
    string Name { get; }

    double Round(double value);

    double Add(double a, double b);
    double Subtract(double a, double b);
    double Multiply(double a, double b);
    double Divide(double a, double b);
    double Power(double a, double b);

    double Exp(double x);
    double Log(double x);
    double Log(double x, double @base);
    double Log1p(double x);
    double Expm1(double x);
    double Sqrt(double x);

    /// <summary>
    /// Stabilized log-sum-exp with max-shift; implementations should round the final result.
    /// </summary>
    double LogSumExp(ReadOnlySpan<double> values);
}
