using System;
using System.Collections.Generic;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymCore;

namespace SymSolvers.Numerics;

/// <summary>
/// Evaluates Sym expressions using a provided floating point model that rounds after each primitive op.
/// </summary>
public static class PrecisionExpressionEvaluator
{
    public static bool TryEvaluate(IExpression expr, IReadOnlyDictionary<string, double> assignments, IFloatingPointModel model, out double value, out string? error)
    {
        try
        {
            value = Evaluate(expr, assignments, model);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            Logging.LogError("PrecisionExpressionEvaluatorTryEvaluate", ex.Message, $"Expression: {expr.ToDisplayString()}\nStack Trace: {ex.StackTrace}");
            value = double.NaN;
            error = ex.Message;
            return false;
        }
    }

    public static double Evaluate(IExpression expr, IReadOnlyDictionary<string, double> assignments, IFloatingPointModel model)
    {
        switch (expr)
        {
            case Number n:
                return model.Round((double)n.Value);
            case Symbol s:
                if (ExpressionHelpers.IsMathConstant(s.Name))
                {
                    if (s.Name.Equals("pi", StringComparison.OrdinalIgnoreCase)) return model.Round(Math.PI);
                    if (s.Name.Equals("e", StringComparison.OrdinalIgnoreCase)) return model.Round(Math.E);
                }
                if (assignments.TryGetValue(s.Name, out var bound))
                {
                    return model.Round(bound);
                }
                throw new InvalidOperationException($"Missing assignment for symbol '{s.Name}'.");
            case Add add:
            {
                double acc = 0;
                foreach (var arg in add.Arguments)
                {
                    acc = model.Add(acc, Evaluate(arg, assignments, model));
                }
                return acc;
            }
            case Subtract sub:
                return model.Subtract(Evaluate(sub.LeftOperand, assignments, model), Evaluate(sub.RightOperand, assignments, model));
            case Multiply mul:
            {
                double acc = 1;
                foreach (var arg in mul.Arguments)
                {
                    acc = model.Multiply(acc, Evaluate(arg, assignments, model));
                }
                return acc;
            }
            case Divide div:
            {
                var denom = Evaluate(div.Denominator, assignments, model);
                if (denom == 0) throw new DivideByZeroException("Division by zero.");
                return model.Divide(Evaluate(div.Numerator, assignments, model), denom);
            }
            case Power pow:
            {
                return model.Power(Evaluate(pow.Base, assignments, model), Evaluate(pow.Exponent, assignments, model));
            }
            case Function fn:
                return EvaluateFunction(fn, assignments, model);
            default:
                throw new InvalidOperationException($"Unsupported expression type '{expr.GetType().Name}' for precision evaluation.");
        }
    }

    private static double EvaluateFunction(Function fn, IReadOnlyDictionary<string, double> assignments, IFloatingPointModel model)
    {
        var name = fn.Name.ToLowerInvariant();
        double Arg(int i) => Evaluate(fn.Arguments[i], assignments, model);

        switch (name)
        {
            case "sin":
                return model.Round(Math.Sin(Arg(0)));
            case "cos":
                return model.Round(Math.Cos(Arg(0)));
            case "tan":
                return model.Round(Math.Tan(Arg(0)));
            case "exp":
                return model.Exp(Arg(0));
            case "log":
                if (fn.Arguments.Count == 2)
                {
                    return model.Log(Arg(1), Arg(0));
                }
                return model.Log(Arg(0));
            case "sqrt":
                return model.Sqrt(Arg(0));
            case "abs":
                return model.Round(Math.Abs(Arg(0)));
            case "log1p":
                return model.Log1p(Arg(0));
            case "expm1":
                return model.Expm1(Arg(0));
            case "softplus":
            {
                var x = Arg(0);
                // max(x,0)+log1p(exp(-abs(x))) is a common stable softplus; here we approximate.
                var max = x > 0 ? x : 0;
                var term = model.Log1p(Math.Exp(-Math.Abs(x)));
                return model.Add(max, term);
            }
            case "logsumexp":
            {
                Span<double> vals = fn.Arguments.Count <= 8 ? stackalloc double[fn.Arguments.Count] : new double[fn.Arguments.Count];
                for (int i = 0; i < fn.Arguments.Count; i++)
                {
                    vals[i] = Arg(i);
                }
                return model.LogSumExp(vals);
            }
            default:
                throw new InvalidOperationException($"Unsupported function '{fn.Name}' for precision evaluation.");
        }
    }
}
