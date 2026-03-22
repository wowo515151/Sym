using System;
using System.Collections.Immutable;

namespace SymCore;

/// <summary>
/// Centralized assumption store (positivity, real, complex, integer) for symbols.
/// </summary>
public sealed class Assumptions
{
    private readonly ImmutableHashSet<string> _positive;
    private readonly ImmutableHashSet<string> _real;
    private readonly ImmutableHashSet<string> _complex;
    private readonly ImmutableHashSet<string> _integer;
    private readonly ImmutableHashSet<string> _goals;
    private readonly ImmutableHashSet<string> _thoughts;
    private readonly ImmutableHashSet<string> _actions;
    private readonly ImmutableHashSet<string> _observations;
    private readonly string _domain;
    private int? _cachedHashCode;

    public bool HasAny => _positive.Count + _real.Count + _complex.Count + _integer.Count + _goals.Count + _thoughts.Count + _actions.Count + _observations.Count > 0;

    public Assumptions(
        IEnumerable<string>? positive = null,
        IEnumerable<string>? real = null,
        IEnumerable<string>? complex = null,
        IEnumerable<string>? integer = null,
        IEnumerable<string>? goals = null,
        IEnumerable<string>? thoughts = null,
        IEnumerable<string>? actions = null,
        IEnumerable<string>? observations = null,
        string domain = "Real")
    {
        _positive = (positive ?? Array.Empty<string>()).Select(Normalize).ToImmutableHashSet(StringComparer.Ordinal);
        _real = (real ?? Array.Empty<string>()).Select(Normalize).ToImmutableHashSet(StringComparer.Ordinal);
        _complex = (complex ?? Array.Empty<string>()).Select(Normalize).ToImmutableHashSet(StringComparer.Ordinal);
        _integer = (integer ?? Array.Empty<string>()).Select(Normalize).ToImmutableHashSet(StringComparer.Ordinal);
        _goals = (goals ?? Array.Empty<string>()).Select(Normalize).ToImmutableHashSet(StringComparer.Ordinal);
        _thoughts = (thoughts ?? Array.Empty<string>()).Select(Normalize).ToImmutableHashSet(StringComparer.Ordinal);
        _actions = (actions ?? Array.Empty<string>()).Select(Normalize).ToImmutableHashSet(StringComparer.Ordinal);
        _observations = (observations ?? Array.Empty<string>()).Select(Normalize).ToImmutableHashSet(StringComparer.Ordinal);
        _domain = string.IsNullOrWhiteSpace(domain) ? "Real" : domain.Trim();
    }

    public bool IsPositive(string symbolName) => _positive.Contains(Normalize(symbolName));
    public bool IsInteger(string symbolName) => _integer.Contains(Normalize(symbolName));
    public bool IsGoal(string symbolName) => _goals.Contains(Normalize(symbolName));
    public bool IsThought(string symbolName) => _thoughts.Contains(Normalize(symbolName));
    public bool IsAction(string symbolName) => _actions.Contains(Normalize(symbolName));
    public bool IsObservation(string symbolName) => _observations.Contains(Normalize(symbolName));
    public bool IsReal(string symbolName)
    {
        var name = Normalize(symbolName);
        if (_real.Contains(name) || _positive.Contains(name) || _integer.Contains(name)) return true;
        if (string.Equals(_domain, "Real", StringComparison.OrdinalIgnoreCase)) return !_complex.Contains(name);
        return false;
    }
    public bool IsComplex(string symbolName)
    {
        var name = Normalize(symbolName);
        if (_complex.Contains(name)) return true;
        if (string.Equals(_domain, "Complex", StringComparison.OrdinalIgnoreCase)) return true;
        return IsReal(symbolName);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Assumptions other) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(_domain, other._domain, StringComparison.Ordinal) &&
               _positive.SetEquals(other._positive) &&
               _real.SetEquals(other._real) &&
               _complex.SetEquals(other._complex) &&
               _integer.SetEquals(other._integer) &&
               _goals.SetEquals(other._goals) &&
               _thoughts.SetEquals(other._thoughts) &&
               _actions.SetEquals(other._actions) &&
               _observations.SetEquals(other._observations);
    }

    public override int GetHashCode()
    {
        if (_cachedHashCode.HasValue) return _cachedHashCode.Value;

        HashCode hash = new HashCode();
        hash.Add(_domain);
        foreach (var s in _positive.OrderBy(x => x)) hash.Add(s);
        foreach (var s in _real.OrderBy(x => x)) hash.Add(s);
        foreach (var s in _complex.OrderBy(x => x)) hash.Add(s);
        foreach (var s in _integer.OrderBy(x => x)) hash.Add(s);
        foreach (var s in _goals.OrderBy(x => x)) hash.Add(s);
        foreach (var s in _thoughts.OrderBy(x => x)) hash.Add(s);
        foreach (var s in _actions.OrderBy(x => x)) hash.Add(s);
        foreach (var s in _observations.OrderBy(x => x)) hash.Add(s);
        
        _cachedHashCode = hash.ToHashCode();
        return _cachedHashCode.Value;
    }

    public static Assumptions FromAdditionalData(IDictionary<string, object>? additional)
    {
        if (additional is null || additional.Count == 0) return new Assumptions(domain: "Real");

        var domain = additional.TryGetValue("Domain", out var rawDomain) && rawDomain is string domainString
            ? domainString
            : "Real";

        IEnumerable<string> Extract(string key)
        {
            if (!additional.TryGetValue(key, out var raw) || raw is null) return Array.Empty<string>();
            return raw switch
            {
                string s => new[] { s },
                IEnumerable<string> list => list,
                IEnumerable<object> objs => objs.Select(o => o?.ToString() ?? string.Empty),
                _ => Array.Empty<string>()
            };
        }

        return new Assumptions(
            positive: Extract("AssumePositive"),
            real: Extract("AssumeReal"),
            complex: Extract("AssumeComplex"),
            integer: Extract("AssumeInteger"),
            goals: Extract("AssumeGoal"),
            thoughts: Extract("AssumeThought"),
            actions: Extract("AssumeAction"),
            observations: Extract("AssumeObservation"),
            domain: domain);
    }

    private static string Normalize(string name) => name?.Trim() ?? string.Empty;
}
