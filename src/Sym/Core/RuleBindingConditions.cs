// Copyright Warren Harding 2026
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Sym.Atoms;

namespace Sym.Core;

public static class RuleBindingConditions
{
    private static readonly ConditionalWeakTable<Func<ImmutableDictionary<string, IExpression>, bool>, BindingConditionSpec> Specs = new();

    public static Func<ImmutableDictionary<string, IExpression>, bool> SymbolPrefix(string bindingName, string prefix)
    {
        var spec = new BindingConditionSpec(BindingConditionKind.SymbolPrefix, bindingName, ImmutableArray.Create(prefix));
        Func<ImmutableDictionary<string, IExpression>, bool> condition = bindings =>
        {
            if (!TryGetBindingSymbol(bindings, bindingName, out string symbolName))
            {
                return false;
            }

            return symbolName.StartsWith(prefix, StringComparison.Ordinal);
        };

        Specs.Add(condition, spec);
        return condition;
    }

    public static Func<ImmutableDictionary<string, IExpression>, bool> StringLiteral(string bindingName) =>
        SymbolPrefix(bindingName, "str:");

    public static Func<ImmutableDictionary<string, IExpression>, bool> SymbolContainsAnyIgnoreCase(string bindingName, params string[] fragments)
    {
        var spec = new BindingConditionSpec(BindingConditionKind.SymbolContainsAnyIgnoreCase, bindingName, fragments.ToImmutableArray());
        Func<ImmutableDictionary<string, IExpression>, bool> condition = bindings =>
        {
            if (!TryGetBindingSymbol(bindings, bindingName, out string symbolName))
            {
                return false;
            }

            return spec.Arguments.Any(fragment => symbolName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        };

        Specs.Add(condition, spec);
        return condition;
    }

    public static bool TryEvaluate(
        Func<ImmutableDictionary<string, IExpression>, bool>? condition,
        Func<string, IExpression?> bindingResolver,
        out bool handled,
        out bool result)
    {
        handled = false;
        result = false;
        if (condition is null || !Specs.TryGetValue(condition, out var spec))
        {
            return false;
        }

        handled = true;
        if (!TryGetLiteralSymbol(bindingResolver(spec.BindingName), out string symbolName))
        {
            return true;
        }

        result = spec.Kind switch
        {
            BindingConditionKind.SymbolPrefix => symbolName.StartsWith(spec.Arguments[0], StringComparison.Ordinal),
            BindingConditionKind.SymbolContainsAnyIgnoreCase => spec.Arguments.Any(fragment => symbolName.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
        return true;
    }

    private static bool TryGetBindingSymbol(
        ImmutableDictionary<string, IExpression> bindings,
        string bindingName,
        out string symbolName)
    {
        symbolName = string.Empty;
        return bindings.TryGetValue(bindingName, out IExpression? expression) &&
               TryGetLiteralSymbol(expression, out symbolName);
    }

    private static bool TryGetLiteralSymbol(IExpression? expression, out string symbolName)
    {
        if (expression is Symbol symbol)
        {
            symbolName = symbol.Name;
            return true;
        }

        symbolName = string.Empty;
        return false;
    }

    private enum BindingConditionKind
    {
        SymbolPrefix,
        SymbolContainsAnyIgnoreCase
    }

    private sealed record BindingConditionSpec(
        BindingConditionKind Kind,
        string BindingName,
        ImmutableArray<string> Arguments);
}
