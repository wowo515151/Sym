// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using SymSolvers.Numerics;

namespace SymSolvers.StableForms;

public sealed class StableLossSynthesisConfig
{
    public IReadOnlyList<IFloatingPointModel> Models { get; init; } = new IFloatingPointModel[]
    {
        new Float16Model(),
        new BFloat16Model(),
        new Float32Model(),
        new Float64Model()
    };

    public int CandidateBudget { get; init; } = 8;
    public int SampleBudget { get; init; } = 24;
    public double EquivalenceTolerance { get; init; } = 1e-6;
    public bool RequireProof { get; init; } = false;
    public bool AdversarialSearch { get; init; } = false;
    public int ReturnTopK { get; init; } = 1;

    public static StableLossSynthesisConfig FromAdditionalData(ImmutableDictionary<string, object>? additional)
    {
        if (additional is null) return new StableLossSynthesisConfig();

        int IntOpt(string key, int def) => additional.TryGetValue(key, out var raw) && raw is int i ? i : def;
        double DoubleOpt(string key, double def)
        {
            if (additional.TryGetValue(key, out var raw))
            {
                if (raw is double d) return d;
                if (raw is decimal dec) return (double)dec;
                if (raw is int i) return i;
            }
            return def;
        }
        bool BoolOpt(string key, bool def) => additional.TryGetValue(key, out var raw) && raw is bool b ? b : def;

        return new StableLossSynthesisConfig
        {
            CandidateBudget = Math.Max(2, IntOpt("StableLossCandidateBudget", 8)),
            SampleBudget = Math.Max(8, IntOpt("StableLossSampleBudget", 24)),
            EquivalenceTolerance = DoubleOpt("StableLossTolerance", 1e-6),
            RequireProof = BoolOpt("StableLossRequireProof", false),
            AdversarialSearch = BoolOpt("StableLossAdversarial", false),
            ReturnTopK = Math.Max(1, IntOpt("StableLossReturnTopK", 1))
        };
    }
}
