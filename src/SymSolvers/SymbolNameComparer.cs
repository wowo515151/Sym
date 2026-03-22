// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using Sym.Atoms;

namespace SymSolvers;

internal sealed class SymbolNameComparer : IEqualityComparer<Symbol>
{
    public static readonly SymbolNameComparer Instance = new();

    public bool Equals(Symbol? x, Symbol? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return string.Equals(x.Name, y.Name, StringComparison.Ordinal);
    }

    public int GetHashCode(Symbol obj)
    {
        return obj?.Name is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Name);
    }
}
