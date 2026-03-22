// Copyright Warren Harding 2026
using System;
using System.Collections.Immutable;
using System.Threading;
using Sym.Atoms;
using Sym.Core.EGraph;
using SymCore;

namespace Sym.Core;

/// <summary>
/// Represents the context and settings for a symbolic solver session.
/// </summary>
public sealed class SolveContext
{
    public Symbol? TargetVariable { get; }

    public ImmutableList<Rule> Rules { get; }

    public int MaxIterations { get; }

    public bool EnableTracing { get; }

    public ImmutableDictionary<string, object>? AdditionalData { get; }

    public Assumptions Assumptions { get; }

    public CancellationToken CancellationToken { get; }

    public Sym.Core.EGraph.EGraph? SharedEGraph { get; }

    public int MaxConcurrency { get; }

    public SolveContext(
        Symbol? targetVariable = null,
        ImmutableList<Rule>? rules = null,
        int maxIterations = 100,
        bool enableTracing = false,
        ImmutableDictionary<string, object>? additionalData = null,
        CancellationToken cancellationToken = default,
        Sym.Core.EGraph.EGraph? sharedEGraph = null,
        int maxConcurrency = 8)
    {
        TargetVariable = targetVariable;
        Rules = rules ?? ImmutableList<Rule>.Empty;
        MaxIterations = maxIterations;
        EnableTracing = enableTracing;
        AdditionalData = additionalData; // preserve null when caller supplies null
        Assumptions = Assumptions.FromAdditionalData(additionalData);
        CancellationToken = cancellationToken;
        SharedEGraph = sharedEGraph;
        MaxConcurrency = maxConcurrency;
    }

    public static ImmutableDictionary<string, object> NormalizeAdditionalData(IDictionary<string, object>? additionalData)
    {
        return additionalData is null
            ? ImmutableDictionary<string, object>.Empty
            : ImmutableDictionary.CreateRange(additionalData);
    }

    public SolveContext WithAdditionalData(IDictionary<string, object>? additionalData)
    {
        var baseDict = AdditionalData ?? ImmutableDictionary<string, object>.Empty;
        var merged = baseDict.SetItems(NormalizeAdditionalData(additionalData));
        return new SolveContext(TargetVariable, Rules, MaxIterations, EnableTracing, merged, CancellationToken, SharedEGraph, MaxConcurrency);
    }

    public SolveContext WithSharedEGraph(Sym.Core.EGraph.EGraph sharedEGraph)
    {
        return new SolveContext(TargetVariable, Rules, MaxIterations, EnableTracing, AdditionalData, CancellationToken, sharedEGraph, MaxConcurrency);
    }

    public void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();

    private bool TryGet<T>(string key, Func<object, bool> matcher, Func<object, T> caster, T defaultValue, out T value)
    {
        if (AdditionalData is not null && AdditionalData.TryGetValue(key, out var raw) && matcher(raw))
        {
            value = caster(raw);
            return true;
        }

        value = defaultValue;
        return false;
    }

    public bool GetBool(string key, bool defaultValue)
    {
        return TryGet(key, o => o is bool, o => (bool)o, defaultValue, out var value) ? value : defaultValue;
    }

    public int GetInt(string key, int defaultValue)
    {
        return TryGet(key, o => o is int, o => (int)o, defaultValue, out var value) ? value : defaultValue;
    }

    public string GetString(string key, string defaultValue)
    {
        return TryGet(key, o => o is string, o => (string)o, defaultValue, out var value) ? value : defaultValue;
    }

    public bool KeepNegativePowers => GetBool("KeepNegativePowers", false);

    public bool CancelCommonFactors => GetBool("CancelCommonFactors", true);

    public bool DifferentiateOnlyTopLevel => GetBool("DifferentiateOnlyTopLevel", false);

    /// <summary>
    /// Optional domain hint ("Real" | "Complex"); defaults to "Real".
    /// </summary>
    public string Domain => GetString("Domain", "Real");

    public bool AssumePositive(Symbol symbol) => Assumptions.IsPositive(symbol.Name);
}
