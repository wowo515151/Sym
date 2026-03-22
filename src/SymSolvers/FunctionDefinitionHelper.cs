// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace SymSolvers;

internal static class FunctionDefinitionHelper
{
    private static readonly ImmutableHashSet<string> ReservedFunctionNames =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "vector", "matrix", "piecewise", "sum", "product", "integrate", "derive", "limit",
            "mod", "gcd", "lcm", "pow", "sqrt", "abs",
            "sin", "cos", "tan", "arcsin", "arccos", "arctan",
            "exp", "ln", "log",
            "lt", "le", "gt", "ge", "and", "or", "not",
            "min", "max", "floor", "ceil", "ceiling", "round",
            "dist", "length", "len");

    private static readonly ImmutableHashSet<string> BannedDefinitionNames =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "int", "decimal", "double", "float", "long", "short", "bool", "boolean",
            "integer", "real", "natural");

    internal sealed record FunctionDefinition(string Name, ImmutableArray<Symbol> Parameters, IExpression Body);

    public static bool IsReservedFunctionName(string name) => ReservedFunctionNames.Contains(name);

    public static bool IsBannedDefinitionName(string name) => BannedDefinitionNames.Contains(name);

    public static bool ContainsFunctionCall(IExpression expr, string functionName)
    {
        if (expr is Function fn && fn.Name.Equals(functionName, StringComparison.Ordinal))
        {
            return true;
        }

        if (expr is Operation op)
        {
            foreach (var arg in op.Arguments)
            {
                if (ContainsFunctionCall(arg, functionName)) return true;
            }
        }

        return false;
    }

    public static bool TryExtractDefinition(
        Equality eq,
        Func<string, bool> isNameAllowed,
        bool allowRightSide,
        bool requireBodyUsesParameter,
        out FunctionDefinition definition)
    {
        definition = default!;

        if (TryExtractDefinitionSide(eq.LeftOperand, out var fn, out var parameters))
        {
            if (IsAllowedDefinition(fn, parameters, eq.RightOperand, isNameAllowed, requireBodyUsesParameter))
            {
                definition = new FunctionDefinition(fn.Name, parameters, eq.RightOperand);
                return true;
            }
        }

        if (allowRightSide && TryExtractDefinitionSide(eq.RightOperand, out var fnR, out var parametersR))
        {
            if (IsAllowedDefinition(fnR, parametersR, eq.LeftOperand, isNameAllowed, requireBodyUsesParameter))
            {
                definition = new FunctionDefinition(fnR.Name, parametersR, eq.LeftOperand);
                return true;
            }
        }

        return false;
    }

    public static Dictionary<string, FunctionDefinition> ExtractDefinitions(
        IEnumerable<IExpression> expressions,
        Func<string, bool> isNameAllowed,
        bool allowRightSide,
        bool requireBodyUsesParameter,
        StringComparer comparer,
        HashSet<string>? forAllSymbols,
        HashSet<IExpression>? definitionExpressions,
        System.Threading.CancellationToken ct)
    {
        var defs = new Dictionary<string, FunctionDefinition>(comparer);

        foreach (var expr in expressions)
        {
            ct.ThrowIfCancellationRequested();
            if (expr is not Equality eq) continue;

            if (TryExtractDefinition(eq, isNameAllowed, allowRightSide, requireBodyUsesParameter, out var def))
            {
                if (!defs.ContainsKey(def.Name))
                {
                    defs[def.Name] = def;
                    definitionExpressions?.Add(eq);
                    if (forAllSymbols != null)
                    {
                        foreach (var p in def.Parameters)
                        {
                            forAllSymbols.Add(p.Name);
                        }
                    }
                }
            }
        }

        return defs;
    }

    public static Dictionary<string, FunctionDefinition> ExtractDefinitions(
        IExpression expression,
        Func<string, bool> isNameAllowed,
        bool allowRightSide,
        bool requireBodyUsesParameter,
        StringComparer comparer,
        HashSet<string>? forAllSymbols,
        HashSet<IExpression>? definitionExpressions,
        System.Threading.CancellationToken ct)
    {
        var equalities = ExpressionClassification.ExtractEqualities(expression).Cast<IExpression>();
        return ExtractDefinitions(equalities, isNameAllowed, allowRightSide, requireBodyUsesParameter, comparer, forAllSymbols, definitionExpressions, ct);
    }

    public static void MergeContextDefinitions(
        IDictionary<string, FunctionDefinition> definitions,
        SolveContext context,
        Func<string, bool> isNameAllowed,
        bool allowRightSide,
        bool requireBodyUsesParameter,
        bool requireKeyMatch)
    {
        if (context.AdditionalData is null) return;
        if (!context.AdditionalData.TryGetValue("FunctionDefinitions", out var defsObj) ||
            defsObj is not ImmutableDictionary<string, IExpression> contextDefs)
        {
            return;
        }

        foreach (var kvp in contextDefs)
        {
            if (requireKeyMatch && definitions.ContainsKey(kvp.Key)) continue;
            if (kvp.Value is not Equality eq) continue;

            if (TryExtractDefinition(eq, isNameAllowed, allowRightSide, requireBodyUsesParameter, out var def))
            {
                if (requireKeyMatch && !string.Equals(kvp.Key, def.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!definitions.ContainsKey(def.Name))
                {
                    definitions[def.Name] = def;
                }
            }
        }
    }

    public static IExpression InlineFunctions(
        IExpression expr,
        IReadOnlyDictionary<string, FunctionDefinition> definitions,
        int maxDepth,
        System.Threading.CancellationToken ct,
        bool countTraversalDepth = false)
    {
        if (definitions.Count == 0) return expr;
        return Inline(expr, 0);

        IExpression Inline(IExpression current, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > maxDepth) return current;

            if (current is Function fn && definitions.TryGetValue(fn.Name, out var def) && fn.Arguments.Count == def.Parameters.Length)
            {
                var map = new Dictionary<string, IExpression>(StringComparer.Ordinal);
                for (int i = 0; i < def.Parameters.Length; i++)
                {
                    map[def.Parameters[i].Name] = Inline(fn.Arguments[i], depth + 1);
                }

                var substituted = SubstituteSymbols(def.Body, map).Canonicalize();
                return Inline(substituted, depth + 1).Canonicalize();
            }

            if (current is Operation op)
            {
                var nextDepth = countTraversalDepth ? depth + 1 : depth;
                var newArgs = op.Arguments.Select(a => Inline(a, nextDepth)).ToImmutableList();
                return op.WithArguments(newArgs).Canonicalize();
            }

            return current;
        }
    }

    public static List<IExpression> InlineDefinitionsInConstraints(
        List<IExpression> constraints,
        Func<string, bool> isNameAllowed,
        bool allowRightSide,
        bool requireBodyUsesParameter,
        StringComparer comparer,
        HashSet<string> forAllSymbols,
        System.Threading.CancellationToken ct)
    {
        var definitionExpressions = new HashSet<IExpression>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
        var defs = ExtractDefinitions(constraints, isNameAllowed, allowRightSide, requireBodyUsesParameter, comparer, forAllSymbols, definitionExpressions, ct);
        if (defs.Count == 0) return constraints;

        var rewritten = new List<IExpression>(constraints.Count);
        foreach (var c in constraints)
        {
            ct.ThrowIfCancellationRequested();
            if (definitionExpressions.Contains(c)) continue;
            rewritten.Add(InlineFunctions(c, defs, 8, ct).Canonicalize());
        }

        return rewritten;
    }

    private static bool TryExtractDefinitionSide(IExpression side, out Function fn, out ImmutableArray<Symbol> parameters)
    {
        fn = null!;
        parameters = ImmutableArray<Symbol>.Empty;
        if (side is not Function f) return false;
        if (f.Arguments.Count == 0) return false;

        var paramList = new List<Symbol>();
        foreach (var arg in f.Arguments)
        {
            if (arg is not Symbol s) return false;
            if (string.IsNullOrWhiteSpace(s.Name)) return false;
            paramList.Add(s);
        }

        fn = f;
        parameters = paramList.ToImmutableArray();
        return true;
    }

    private static bool IsAllowedDefinition(Function fn, ImmutableArray<Symbol> parameters, IExpression body, Func<string, bool> isNameAllowed, bool requireBodyUsesParameter)
    {
        if (!isNameAllowed(fn.Name)) return false;
        if (parameters.Length == 0) return false;
        // Allow constant/aliasing definitions like f(x) == c or f(x) == otherSymbol.
        // These are safe to inline and are common in script problems for staged evaluation.
        if (requireBodyUsesParameter && !parameters.Any(p => body.ContainsSymbol(p)))
        {
            if (body is not Symbol && body is not Number) return false;
        }
        if (ContainsFunctionCall(body, fn.Name)) return false;
        return true;
    }

    private static IExpression SubstituteSymbols(IExpression expr, IReadOnlyDictionary<string, IExpression> subs)
    {
        if (expr is Symbol s && subs.TryGetValue(s.Name, out var val))
        {
            return val;
        }

        if (expr is Operation op)
        {
            var newArgs = op.Arguments.Select(a => SubstituteSymbols(a, subs)).ToImmutableList();
            return op.WithArguments(newArgs).Canonicalize();
        }

        return expr;
    }
}
