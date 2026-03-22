// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

public static class ExpressionClassification
{
    public static bool IsLogicOperatorName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower is "and" or "or" or "not" or "implies" or "iff";
    }

    public static bool IsLogicExpression(IExpression expr)
    {
        if (expr is Symbol s)
        {
            return s.Name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   s.Name.Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        return expr is Function fn && IsLogicOperatorName(fn.Name);
    }

    public static IReadOnlyList<IExpression> CollectLogicAtoms(IExpression expr)
    {
        var found = new HashSet<IExpression>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
        var ordered = new List<IExpression>();

        void AddAtom(IExpression atom)
        {
            if (found.Add(atom))
            {
                ordered.Add(atom);
            }
        }

        void Visit(IExpression current)
        {
            if (current is Symbol sym &&
                (sym.Name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                 sym.Name.Equals("false", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (current is Function fn && IsLogicOperatorName(fn.Name))
            {
                foreach (var arg in fn.Arguments)
                {
                    Visit(arg);
                }
                return;
            }

            if (current is Number)
            {
                return;
            }

            AddAtom(current);
        }

        Visit(expr);
        return ExpressionHelpers.SortArguments(ordered.ToImmutableList());
    }

    public static bool IsSolvable(IExpression expr)
    {
        if (expr is Derivative or Integral or DefiniteIntegral or Grad or Div or Curl or Limit or SeriesExpansion)
        {
            return true;
        }

        if (expr is Function fn && !IsStableFunction(fn.Name))
        {
            return true;
        }

        if (expr is Operation op)
        {
            return op.Arguments.Any(IsSolvable);
        }

        return false;
    }

    public static bool IsNonLinear(IExpression expr, Symbol? target)
    {
        bool foundNonlinear = false;
        void Visit(IExpression e)
        {
            if (foundNonlinear) return;
            if (e is Power p && (target == null || p.Base.ContainsSymbol(target)))
            {
                if (p.Exponent is not Number n || n.Value != 1m) { foundNonlinear = true; return; }
            }
            if (e is Function f && (target == null || f.Arguments.Any(a => a.ContainsSymbol(target))))
            {
                if (!IsLinearFunction(f.Name))
                {
                    foundNonlinear = true;
                    return;
                }
            }
            if (e is Multiply m)
            {
                int varCount = 0;
                foreach (var arg in m.Arguments)
                {
                    if (arg is not Number && !ExpressionHelpers.IsMathConstant((arg as Symbol)?.Name ?? ""))
                    {
                        if (target == null || arg.ContainsSymbol(target)) varCount++;
                    }
                }
                if (varCount > 1) { foundNonlinear = true; return; }
            }
            if (e is Operation op)
            {
                foreach (var arg in op.Arguments) Visit(arg);
            }
        }
        Visit(expr);
        return foundNonlinear;
    }

    private static bool IsLinearFunction(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower is "add" or "subtract" or "negate" or "equality" or "and" or "vector";
    }

    public static bool IsStableFunction(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower switch
        {
            "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "sinh" or "cosh" or "tanh" => true,
            "exp" or "log" or "ln" or "log10" or "sqrt" or "abs" or "ceil" or "floor" or "round" => true,
            "min" or "max" or "sign" or "sgn" => true,
            "vector" or "matrix" or "list" or "index" => true,
            "equality" or "equalityrelaxed" => true,
            "lt" or "le" or "gt" or "ge" or "ne" or "and" or "or" or "not" or "implies" or "iff" => true,
            "interval" => true,
            _ => false
        };
    }

    public static bool IsInequalityExpression(IExpression expr)
        => expr is Function fn && IsInequalityName(fn.Name);

    public static bool IsInequalityName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower == "lt" || lower == "le" || lower == "gt" || lower == "ge" || lower == "ne" ||
               lower == "less" || lower == "lessequal" || lower == "greater" || lower == "greaterequal" || lower == "notequal" ||
               lower == "abs";
    }

    public static bool IsConjunction(IExpression expr)
        => expr is Function fn && fn.Name.Equals("and", StringComparison.OrdinalIgnoreCase);

    public static bool ContainsInequality(IExpression expr)
    {
        if (IsInequalityExpression(expr) || IsConjunction(expr)) return true;
        if (expr is Operation op)
        {
            return op.Arguments.Any(ContainsInequality);
        }
        return false;
    }

    public static bool ContainsTrig(IExpression expr)
    {
        if (expr is null) return false;
        if (expr is Function fn)
        {
            var name = fn.Name.ToLowerInvariant();
            if (name is "sin" or "cos" or "tan" or "csc" or "sec" or "cot" or "sind" or "cosd" or "tand" or "cscd" or "secd" or "cotd") return true;
        }

        if (expr is Operation op)
        {
            return op.Arguments.Any(ContainsTrig);
        }

        return false;
    }

    public static IReadOnlyList<IExpression> FlattenConjunction(IExpression expr)
    {
        if (expr is Function fn && fn.Name.Equals("and", StringComparison.OrdinalIgnoreCase))
        {
            var list = new List<IExpression>();
            foreach (var arg in fn.Arguments)
            {
                list.AddRange(FlattenConjunction(arg));
            }
            return list;
        }

        return ImmutableList.Create(expr);
    }

    public static IEnumerable<Equality> ExtractEqualities(IExpression? expr)
    {
        if (expr is null) yield break;

        if (expr is Equality eq)
        {
            if (eq.RightOperand is Vector v)
            {
                foreach (var inner in ExtractEqualities(v)) yield return inner;
            }
            else if (eq.RightOperand is Add a && a.Arguments.Any(arg => arg is Equality || (arg is Function f && f.Name.Equals("Equality", StringComparison.OrdinalIgnoreCase))))
            {
                foreach (var inner in ExtractEqualities(a)) yield return inner;
            }
            else
            {
                yield return eq;
            }
            yield break;
        }

        if (expr is Function fn &&
            fn.Name.Equals("Equality", StringComparison.OrdinalIgnoreCase) &&
            fn.Arguments.Count == 2)
        {
            yield return new Equality(fn.Arguments[0], fn.Arguments[1]).Canonicalize() as Equality ?? new Equality(fn.Arguments[0], fn.Arguments[1]);
            yield break;
        }

        if (expr is Add add)
        {
            foreach (var arg in add.Arguments)
            {
                foreach (var inner in ExtractEqualities(arg)) yield return inner;
            }
        }
        else if (expr is Vector vec)
        {
            foreach (var arg in vec.Arguments)
            {
                foreach (var inner in ExtractEqualities(arg)) yield return inner;
            }
        }
    }
}
