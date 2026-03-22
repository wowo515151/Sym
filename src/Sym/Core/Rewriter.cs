// Copyright Warren Harding 2026
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System;
using SymCore;
// Avoid direct dependency on SymRules; reference only Sym.Core types and interfaces



namespace Sym.Core.Rewriters
{
    public static class Rewriter
    {
        public delegate decimal FunctionEvaluator(string name, decimal[] args);
        private static FunctionEvaluator? _evaluator;

        public static void RegisterEvaluator(FunctionEvaluator evaluator) => _evaluator = evaluator;

        private struct CacheKey : IEquatable<CacheKey>
        {
            public IExpression Expression;
            public object Rules; // ImmutableList<Rule> or RuleIndex
            public Assumptions? Assumptions;

            public CacheKey(IExpression expression, object rules, Assumptions? assumptions)
            {
                Expression = expression;
                Rules = rules;
                Assumptions = assumptions;
            }

            public bool Equals(CacheKey other)
            {
                if (!Expression.InternalEquals(other.Expression)) return false;
                if (!Equals(Assumptions, other.Assumptions)) return false;
                
                if (Rules is RuleIndex idx1 && other.Rules is RuleIndex idx2)
                {
                    return idx1.Equals(idx2);
                }
                
                if (Rules is ImmutableList<Rule> list1 && other.Rules is ImmutableList<Rule> list2)
                {
                    if (list1.Count != list2.Count) return false;
                    for (int i = 0; i < list1.Count; i++)
                    {
                        if (!list1[i].Equals(list2[i])) return false;
                    }
                    return true;
                }
                
                return Equals(Rules, other.Rules);
            }

            public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                HashCode hash = new HashCode();
                hash.Add(Expression.InternalGetHashCode());
                hash.Add(Assumptions);
                if (Rules is ImmutableList<Rule> list)
                {
                    foreach (var rule in list) hash.Add(rule);
                }
                else
                {
                    hash.Add(Rules);
                }
                return hash.ToHashCode();
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<CacheKey, RewriterResult> _cache = new();

        public static RewriterResult RewriteSinglePass(IExpression expression, ImmutableList<Rule> rules, Assumptions? assumptions = null, CancellationToken ct = default)
        {
            var key = new CacheKey(expression, rules, assumptions);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            (IExpression newExpression, bool changed) = ApplyRulesRecursively(expression, rules, assumptions, ct);
            var result = new RewriterResult(newExpression, changed);
            
            if (_cache.Count < 10000) _cache.TryAdd(key, result);
            return result;
        }

        public static RewriterResult RewriteSinglePass(IExpression expression, RuleIndex index, Assumptions? assumptions = null, CancellationToken ct = default)
        {
            var key = new CacheKey(expression, index, assumptions);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            (IExpression newExpression, bool changed) = ApplyRulesRecursively(expression, index, assumptions, ct);
            var result = new RewriterResult(newExpression, changed);

            if (_cache.Count < 10000) _cache.TryAdd(key, result);
            return result;
        }


        public static RewriterResult Rewrite(IExpression expression, ImmutableList<Rule> rules, Assumptions? assumptions = null, CancellationToken ct = default)
        {
            return RewriteSinglePass(expression, rules, assumptions, ct);
        }

        public static RewriterResult RewriteFully(IExpression expression, ImmutableList<Rule> rules, int maxInternalIterations = 100, Assumptions? assumptions = null, CancellationToken ct = default)
        {
            IExpression currentExpression = expression;
            bool overallChanged = false;
            bool changedInLastIteration;
            int iteration = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var seen = new HashSet<IExpression>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
            seen.Add(currentExpression);

            do
            {
                ct.ThrowIfCancellationRequested();
                changedInLastIteration = false;
                if (sw.ElapsedMilliseconds > 5000) break;
                
                RewriterResult singlePassResult = RewriteSinglePass(currentExpression, rules, assumptions, ct);

                if (singlePassResult.Changed)
                {
                    if (!seen.Add(singlePassResult.RewrittenExpression))
                    {
                        break; // Cycle detected
                    }
                    changedInLastIteration = true;
                    overallChanged = true;
                }
                currentExpression = singlePassResult.RewrittenExpression;
                iteration++;
            } while (changedInLastIteration && iteration < maxInternalIterations);

            return new RewriterResult(currentExpression, overallChanged);
        }

        public static RewriterResult RewriteFully(IExpression expression, RuleIndex index, int maxInternalIterations = 100, Assumptions? assumptions = null, CancellationToken ct = default)
        {
            IExpression currentExpression = expression;
            bool overallChanged = false;
            bool changedInLastIteration;
            int iteration = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var seen = new HashSet<IExpression>(ExpressionHelpers.ExpressionEqualityComparer.Instance);
            seen.Add(currentExpression);

            do
            {
                ct.ThrowIfCancellationRequested();
                changedInLastIteration = false;
                if (sw.ElapsedMilliseconds > 5000) break;
                
                RewriterResult singlePassResult = RewriteSinglePass(currentExpression, index, assumptions, ct);

                if (singlePassResult.Changed)
                {
                    if (!seen.Add(singlePassResult.RewrittenExpression))
                    {
                        break; // Cycle detected
                    }
                    changedInLastIteration = true;
                    overallChanged = true;
                }
                currentExpression = singlePassResult.RewrittenExpression;
                iteration++;
            } while (changedInLastIteration && iteration < maxInternalIterations);

            return new RewriterResult(currentExpression, overallChanged);
        }


        private static (IExpression result, bool changed) ApplyRulesRecursively(IExpression expression, ImmutableList<Rule> rules, Assumptions? assumptions, CancellationToken ct)
        {
            foreach (Rule rule in rules)
            {
                ct.ThrowIfCancellationRequested();
                var substituted = rule.Apply(expression, assumptions, ct);
                if (!ReferenceEquals(substituted, expression) && !substituted.InternalEquals(expression))
                {
                    if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_REWRITE") == "1")
                        Console.WriteLine($"DEBUG: Rule matched: {rule.Name ?? "Unnamed"} Pat={rule.Pattern.ToDisplayString()} Replacement={rule.Replacement.ToDisplayString()} (Expr: {expression.ToDisplayString()})");

                    return (substituted, true);
                }
            }

            if (expression is Operation operation)
            {
                IExpression[] newArgumentsArray = new IExpression[operation.Arguments.Count];
                bool anyArgumentsChanged = false;

                for (int i = 0; i < operation.Arguments.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    IExpression originalArg = operation.Arguments[i];
                    (IExpression newArg, bool argChanged) = ApplyRulesRecursively(originalArg, rules, assumptions, ct);
                    newArgumentsArray[i] = newArg;
                    if (argChanged)
                    {
                        anyArgumentsChanged = true;
                    }
                }

                IExpression currentExpr = expression;
                if (anyArgumentsChanged)
                {
                    ImmutableList<IExpression> updatedArgs = newArgumentsArray.ToImmutableList();
                    currentExpr = ((Operation)operation).WithArguments(updatedArgs).Canonicalize();
                }

                // High-Value Feature: Deep Constant Folding for Custom Functions
                if (_evaluator != null && currentExpr is Function fn && fn.Arguments.All(a => a is Number))
                {
                    try
                    {
                        var argValues = fn.Arguments.Select(a => ((Number)a).Value).ToArray();
                        var resultValue = _evaluator(fn.Name, argValues);
                        return (new Number(resultValue), true);
                    }
                    catch
                    {
                        // Evaluation failed (e.g. unsupported function), ignore and return current
                    }
                }

                return (currentExpr, anyArgumentsChanged);
            }
            return (expression, false);
        }

        private static (IExpression result, bool changed) ApplyRulesRecursively(IExpression expression, RuleIndex index, Assumptions? assumptions, CancellationToken ct)
        {
            foreach (Rule rule in index.GetCandidateRules(expression))
            {
                ct.ThrowIfCancellationRequested();
                var substituted = rule.Apply(expression, assumptions, ct);
                if (!ReferenceEquals(substituted, expression) && !substituted.InternalEquals(expression))
                {
                    if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_REWRITE") == "1")
                        Console.WriteLine($"DEBUG: Rule matched: {rule.Name ?? "Unnamed"} Pat={rule.Pattern.ToDisplayString()} Replacement={rule.Replacement.ToDisplayString()} (Expr: {expression.ToDisplayString()})");

                    return (substituted, true);
                }
            }

            if (expression is Operation operation)
            {
                IExpression[] newArgumentsArray = new IExpression[operation.Arguments.Count];
                bool anyArgumentsChanged = false;

                for (int i = 0; i < operation.Arguments.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    IExpression originalArg = operation.Arguments[i];
                    (IExpression newArg, bool argChanged) = ApplyRulesRecursively(originalArg, index, assumptions, ct);
                    newArgumentsArray[i] = newArg;
                    if (argChanged)
                    {
                        anyArgumentsChanged = true;
                    }
                }

                if (anyArgumentsChanged)
                {
                    ImmutableList<IExpression> updatedArgs = newArgumentsArray.ToImmutableList();
                    return (((Operation)operation).WithArguments(updatedArgs).Canonicalize(), true);
                }
            }
            return (expression, false);
        }


        public static MatchResult TryMatch(IExpression expression, IExpression pattern)
        {
            ImmutableDictionary<string, IExpression>.Builder bindings = ImmutableDictionary.CreateBuilder<string, IExpression>();
            bool success = TryMatchRecursive(expression, pattern, bindings);
            return success
                ? new MatchResult(true, bindings.ToImmutable())
                : new MatchResult(false, ImmutableDictionary<string, IExpression>.Empty);
        }

        private static bool TryMatchRecursive(IExpression expression, IExpression pattern, ImmutableDictionary<string, IExpression>.Builder bindings)
        {
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_MATCH") == "1")
                Console.WriteLine($"DEBUG: MatchRecursive Expr: {expression.ToDisplayString()} ({expression.GetType().Name}) vs Pat: {pattern.ToDisplayString()} ({pattern.GetType().Name})");

            if (pattern is Wild wildPattern)
            {
                if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_MATCH") == "1")
                    Console.WriteLine($"DEBUG: Matching against Wild: {wildPattern.Name}");
                if (bindings.ContainsKey(wildPattern.Name))
                {
                    return bindings[wildPattern.Name].InternalEquals(expression);
                }
                else
                {
                    if (wildPattern.Constraint == WildConstraint.Scalar && !expression.Shape.IsScalar)
                    {
                        return false;
                    }
                    if (wildPattern.Constraint == WildConstraint.Constant && expression is not Number)
                    {
                        return false;
                    }

                    bindings.Add(wildPattern.Name, expression);
                    return true;
                }
            }

            if (expression.GetType() != pattern.GetType())
            {
                if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_MATCH") == "1")
                    Console.WriteLine($"DEBUG: Match FAILED Type mismatch: Expr={expression.GetType().Name} Pat={pattern.GetType().Name}");
                return false;
            }

            if (expression is Atom atomExpression && pattern is Atom atomPattern)
            {
                if (atomExpression is Symbol atomSym && atomPattern is Symbol patSym)
                {
                    if (IsBooleanSymbolName(atomSym.Name) && IsBooleanSymbolName(patSym.Name))
                    {
                        return string.Equals(atomSym.Name, patSym.Name, StringComparison.OrdinalIgnoreCase)
                            && (patSym.Shape.IsScalar || atomSym.Shape.Equals(patSym.Shape));
                    }
                    return string.Equals(atomSym.Name, patSym.Name, StringComparison.Ordinal)
                        && (patSym.Shape.IsScalar || atomSym.Shape.Equals(patSym.Shape));
                }

                return atomExpression.InternalEquals(atomPattern);
            }
            else if (expression is Operation opExpression && pattern is Operation opPattern)
            {
                if (opExpression is Function funcExp && opPattern is Function funcPat)
                {
                    var lowerThis = funcExp.Name.ToLowerInvariant();
                    var lowerPat = funcPat.Name.ToLowerInvariant();
                    bool nameEquals;
                    if (IsCaseInsensitiveFunctionName(lowerPat))
                    {
                        nameEquals = string.Equals(lowerThis, lowerPat, StringComparison.Ordinal);
                    }
                    else
                    {
                        nameEquals = string.Equals(funcExp.Name, funcPat.Name, StringComparison.Ordinal);
                    }
                    if (!nameEquals) return false;
                }

                if (opExpression.Arguments.Count != opPattern.Arguments.Count)
                {
                    return false;
                }
                for (int i = 0; i < opExpression.Arguments.Count; i++)
                {
                    if (!TryMatchRecursive(opExpression.Arguments[i], opPattern.Arguments[i], bindings))
                    {
                        return false;
                    }
                }
                return true;
            }

            return false;
        }

        private static bool IsBooleanSymbolName(string name)
        {
            return name.Equals("true", StringComparison.OrdinalIgnoreCase)
                || name.Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCaseInsensitiveFunctionName(string lowerName)
        {
            return lowerName == "sin" || lowerName == "cos" || lowerName == "tan" ||
                lowerName == "log" || lowerName == "exp" || lowerName == "sqrt" || lowerName == "abs" ||
                lowerName == "and" || lowerName == "or" || lowerName == "not" ||
                lowerName == "implies" || lowerName == "iff";
        }

        public static IExpression Substitute(IExpression replacementExpression, ImmutableDictionary<string, IExpression> bindings, CancellationToken ct = default)
        {
            if (replacementExpression is Wild wild)
            {
                if (bindings.TryGetValue(wild.Name, out IExpression? boundExpression))
                {
                    return boundExpression;
                }
                return wild;
            }

            if (replacementExpression is Atom)
            {
                return replacementExpression;
            }

            if (replacementExpression is Operation operation)
            {
                ImmutableList<IExpression>.Builder newArgsBuilder = ImmutableList.CreateBuilder<IExpression>();
                foreach (IExpression arg in operation.Arguments)
                {
                    ct.ThrowIfCancellationRequested();
                    newArgsBuilder.Add(Substitute(arg, bindings, ct));
                }
                ImmutableList<IExpression> newArgs = newArgsBuilder.ToImmutable();
                return operation.WithArguments(newArgs).Canonicalize();
            }
            return replacementExpression.Canonicalize();
        }
    }
}