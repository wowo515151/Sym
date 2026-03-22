// Copyright Warren Harding 2026
using System;
using Sym.Core;

namespace SymSolvers;

internal static class EGraphBackendSelector
{
    public static EGraphBackendMode GetMode(SolveContext context)
    {
        string raw = context.GetString(SolverOptionKeys.EGraphBackend, "Cpu");
        return Parse(raw);
    }

    public static EGraphBackendMode Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return EGraphBackendMode.Cpu;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "cobra" => EGraphBackendMode.Cobra,
            "auto" => EGraphBackendMode.Auto,
            _ => EGraphBackendMode.Cpu
        };
    }

    public static ISolverStrategy CreateSolveStrategy(SolveContext context)
    {
        return GetMode(context) switch
        {
#if ENABLE_COBRA
            EGraphBackendMode.Cobra => new CobraSolverStrategy(),
            EGraphBackendMode.Auto => new CobraSolverStrategy(),
#endif
            _ => new EGraphSolverStrategy()
        };
    }
}
