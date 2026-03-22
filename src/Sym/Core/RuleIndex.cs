// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Operations;

namespace Sym.Core;

/// <summary>
/// An index of rules grouped by the head symbol or function of their patterns.
/// Allows for faster rule matching by filtering for only rules that could potentially match.
/// </summary>
public sealed class RuleIndex
{
    public ImmutableList<Rule> AllRules { get; }
    public ImmutableDictionary<string, ImmutableList<Rule>> ByHead { get; }

    public RuleIndex(ImmutableList<Rule> allRules, ImmutableDictionary<string, ImmutableList<Rule>> byHead)
    {
        AllRules = allRules;
        ByHead = byHead;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not RuleIndex other) return false;
        if (ReferenceEquals(this, other)) return true;
        
        if (AllRules.Count != other.AllRules.Count) return false;
        for (int i = 0; i < AllRules.Count; i++)
        {
            if (!AllRules[i].Equals(other.AllRules[i])) return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        foreach (var rule in AllRules)
        {
            hash.Add(rule);
        }
        return hash.ToHashCode();
    }

    public static RuleIndex Create(IEnumerable<Rule> rules)
    {
        var all = rules.ToImmutableList();
        var map = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);

        foreach (var rule in all)
        {
            var head = GetHead(rule.Pattern);
            if (!map.TryGetValue(head, out var list))
            {
                list = new List<Rule>();
                map[head] = list;
            }
            list.Add(rule);
        }

        return new RuleIndex(all, map.ToImmutableDictionary(k => k.Key, k => k.Value.ToImmutableList(), StringComparer.Ordinal));
    }

    public IEnumerable<Rule> GetCandidateRules(IExpression expression)
    {
        var head = GetHead(expression);
        var specific = ByHead.TryGetValue(head, out var list) ? list : ImmutableList<Rule>.Empty;
        var wild = ByHead.TryGetValue("*", out var wildList) ? wildList : ImmutableList<Rule>.Empty;

        if (specific.Count == 0) return wild;
        if (wild.Count == 0) return specific;
        return specific.AddRange(wild);
    }

    public static string GetHead(IExpression expr)
    {
        if (expr is Wild) return "*";
        if (expr is Function f) return f.Name.ToLowerInvariant();
        if (expr is Symbol s) return s.Name;
        if (expr is Number) return "#";
        
        return expr.GetType().Name;
    }
}
