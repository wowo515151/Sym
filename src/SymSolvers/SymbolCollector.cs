using System;
using System.Collections.Generic;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

internal static class SymbolCollector
{
    public static HashSet<string> CollectSymbolNames(IExpression expr, Func<string, bool>? shouldIgnore = null, StringComparer? comparer = null)
    {
        var names = new HashSet<string>(comparer ?? StringComparer.Ordinal);

        void Walk(IExpression e)
        {
            if (e is Symbol s)
            {
                if (shouldIgnore != null && shouldIgnore(s.Name)) return;
                names.Add(s.Name);
                return;
            }

            if (e is Operation op)
            {
                foreach (var arg in op.Arguments)
                {
                    Walk(arg);
                }
            }
        }

        Walk(expr);
        return names;
    }

    public static List<Symbol> CollectSymbolsList(IExpression expr, Func<string, bool>? shouldIgnore = null, StringComparer? comparer = null)
    {
        var seen = new HashSet<string>(comparer ?? StringComparer.Ordinal);
        var list = new List<Symbol>();

        void Walk(IExpression e)
        {
            if (e is Symbol s)
            {
                if (shouldIgnore != null && shouldIgnore(s.Name)) return;
                if (seen.Add(s.Name)) list.Add(s);
                return;
            }

            if (e is Operation op)
            {
                foreach (var arg in op.Arguments)
                {
                    Walk(arg);
                }
            }
        }

        Walk(expr);
        return list;
    }

    public static HashSet<Symbol> CollectSymbolsSet(IExpression expr, Func<string, bool>? shouldIgnore = null, IEqualityComparer<Symbol>? comparer = null)
    {
        var set = new HashSet<Symbol>(comparer ?? SymbolNameComparer.Instance);

        void Walk(IExpression e)
        {
            if (e is Symbol s)
            {
                if (shouldIgnore != null && shouldIgnore(s.Name)) return;
                set.Add(s);
                return;
            }

            if (e is Operation op)
            {
                foreach (var arg in op.Arguments)
                {
                    Walk(arg);
                }
            }
        }

        Walk(expr);
        return set;
    }

    public static bool IsMathConstantName(string name) => ExpressionHelpers.IsMathConstant(name);
}
