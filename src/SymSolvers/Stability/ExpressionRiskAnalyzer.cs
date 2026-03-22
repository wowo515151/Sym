using System;
using System.Collections.Generic;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers.Stability;

/// <summary>
/// Lightweight pattern heuristics to reward stable shapes and penalize risky ones.
/// </summary>
public static class ExpressionRiskAnalyzer
{
    public sealed record RiskScore(double Penalty, double Reward)
    {
        public double Net => Reward - Penalty;
    }

    public static RiskScore Score(IExpression expr)
    {
        double penalty = 0;
        double reward = 0;
        Traverse(expr, ref penalty, ref reward);
        return new RiskScore(penalty, reward);
    }

    private static void Traverse(IExpression expr, ref double penalty, ref double reward)
    {
        switch (expr)
        {
            case Function fn:
            {
                var name = fn.Name.ToLowerInvariant();
                if (name == "log")
                {
                    if (fn.Arguments.Count == 1 && fn.Arguments[0] is Add add && add.Arguments.Count == 2)
                    {
                        // log(1 + u)
                        if (add.Arguments[0] is Number n && n.Value == 1m)
                        {
                            penalty += 0.5;
                        }
                    }
                    if (fn.Arguments.Count == 1 && IsExpDifference(fn.Arguments[0]))
                    {
                        penalty += 0.6;
                    }
                    if (fn.Arguments.Count == 1 && fn.Arguments[0] is Function inner && inner.Name.Equals("expm1", StringComparison.OrdinalIgnoreCase))
                    {
                        reward += 0.6;
                    }
                }
                if (name == "logsumexp" || name == "log1p" || name == "expm1" || name == "softplus")
                {
                    reward += 1.0;
                }
                if (name == "exp" && fn.Arguments.Count == 1)
                {
                    penalty += 0.2;
                }
                break;
            }
            case Divide div:
            {
                if (div.Denominator is Add add && add.Arguments.Count == 2)
                {
                    // a/b with potential cancellation
                    penalty += 0.2;
                }
                break;
            }
        }

        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                Traverse(arg, ref penalty, ref reward);
            }
        }
    }

    private static bool IsExpDifference(IExpression expr)
    {
        if (expr is not Add add || add.Arguments.Count != 2) return false;

        bool IsExp(IExpression e) => e is Function fn && fn.Name.Equals("exp", StringComparison.OrdinalIgnoreCase);

        var left = add.Arguments[0];
        var right = add.Arguments[1];

        if (IsExp(left) && right is Multiply mul && mul.Arguments.Count == 2)
        {
            if (mul.Arguments[0] is Number n && n.Value == -1m && IsExp(mul.Arguments[1]))
            {
                return true;
            }
        }

        if (IsExp(right) && left is Multiply mulLeft && mulLeft.Arguments.Count == 2)
        {
            if (mulLeft.Arguments[0] is Number n && n.Value == -1m && IsExp(mulLeft.Arguments[1]))
            {
                return true;
            }
        }

        return false;
    }
}
