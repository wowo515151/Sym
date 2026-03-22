using System;
using System.Collections.Generic;
using Sym.Core;

namespace SymSolvers.CSharpAnalysis;

public enum CSharpMathBugSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public enum CSharpMathBugConfidence
{
    Low = 0,
    Medium = 1,
    High = 2,
    Confirmed = 3
}

public enum CSharpSecurityRisk
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum CSharpSecurityFlowMode
{
    SinkOnly = 0,
    IntraProcedural = 1,
    Disabled = 2,
    InterproceduralIfds = 3,
    InterproceduralIde = 4
}

public sealed record CSharpMathBugSourceSpan(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn)
{
    public override string ToString()
    {
        var path = string.IsNullOrWhiteSpace(FilePath) ? "<in-memory>" : FilePath;
        return $"{path}:{StartLine}:{StartColumn}-{EndLine}:{EndColumn}";
    }
}

public sealed record CSharpMathBugAnalyzerOptions(
    bool EnableStabilityAnalysis = false,
    int MaxFindings = 25,
    double ConfidenceThreshold = 0.35,
    int MaxSaturationIterations = 12,
    int MaxSamples = 48,
    double SaturationTimeoutSeconds = 8.0,
    double AnalysisTimeoutSeconds = 3600.0,
    int MaxDegreeOfParallelism = 0,
    CSharpSecurityFlowMode SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
    int SecurityMaxTraceSteps = 8,
    // Legacy option name kept for compatibility; now covers both UserSource and AiSource.
    bool PrioritizeUserSources = false,
    // Repo-specific source/sink/sanitizer rules file (RepoScanInfo.txt).
    string? RepoScanInfoPath = null,
    bool EnableGuardProver = true,
    int GuardMaxFacts = 32,
    int GuardMaxIterations = 8,
    double GuardTimeoutSeconds = 2.0)
{
    public static CSharpMathBugAnalyzerOptions Default { get; } = new();

    public CSharpMathBugAnalyzerOptions Normalize()
    {
        var normalizedMaxFindings = Math.Clamp(MaxFindings, 1, 200);
        var normalizedThreshold = Math.Clamp(ConfidenceThreshold, 0.0, 1.0);
        var normalizedIterations = Math.Clamp(MaxSaturationIterations, 1, 100);
        var normalizedSamples = Math.Clamp(MaxSamples, 8, 256);
        var normalizedSaturationTimeout = Math.Clamp(SaturationTimeoutSeconds, 0.1, 120.0);
        var normalizedAnalysisTimeout = Math.Clamp(AnalysisTimeoutSeconds, 1.0, 86400.0);
        var normalizedParallelism = MaxDegreeOfParallelism <= 0
            ? Math.Max(1, Environment.ProcessorCount / 2)
            : Math.Clamp(MaxDegreeOfParallelism, 1, 128);
        var normalizedTraceSteps = Math.Clamp(SecurityMaxTraceSteps, 2, 64);
        var normalizedFlowMode = Enum.IsDefined(typeof(CSharpSecurityFlowMode), SecurityFlowMode)
            ? SecurityFlowMode
            : CSharpSecurityFlowMode.IntraProcedural;
        var normalizedRepoScanInfoPath = string.IsNullOrWhiteSpace(RepoScanInfoPath) ? null : RepoScanInfoPath.Trim();
        var normalizedGuardMaxFacts = Math.Clamp(GuardMaxFacts, 1, 1024);
        var normalizedGuardMaxIterations = Math.Clamp(GuardMaxIterations, 1, 32);
        var normalizedGuardTimeout = Math.Clamp(GuardTimeoutSeconds, 0.0, 60.0);

        return this with
        {
            MaxFindings = normalizedMaxFindings,
            ConfidenceThreshold = normalizedThreshold,
            MaxSaturationIterations = normalizedIterations,
            MaxSamples = normalizedSamples,
            SaturationTimeoutSeconds = normalizedSaturationTimeout,
            AnalysisTimeoutSeconds = normalizedAnalysisTimeout,
            MaxDegreeOfParallelism = normalizedParallelism,
            SecurityFlowMode = normalizedFlowMode,
            SecurityMaxTraceSteps = normalizedTraceSteps,
            PrioritizeUserSources = PrioritizeUserSources,
            RepoScanInfoPath = normalizedRepoScanInfoPath,
            EnableGuardProver = EnableGuardProver,
            GuardMaxFacts = normalizedGuardMaxFacts,
            GuardMaxIterations = normalizedGuardMaxIterations,
            GuardTimeoutSeconds = normalizedGuardTimeout
        };
    }

    public static CSharpMathBugAnalyzerOptions FromAdditionalData(IReadOnlyDictionary<string, object>? additionalData)
    {
        if (additionalData is null || additionalData.Count == 0)
        {
            return Default;
        }

        static bool ReadBool(IReadOnlyDictionary<string, object> data, string key, bool fallback)
        {
            if (!data.TryGetValue(key, out var raw) || raw is null) return fallback;
            if (raw is bool b) return b;
            if (raw is string s && bool.TryParse(s, out var parsed)) return parsed;
            return fallback;
        }

        static int ReadInt(IReadOnlyDictionary<string, object> data, string key, int fallback)
        {
            if (!data.TryGetValue(key, out var raw) || raw is null) return fallback;
            if (raw is int i) return i;
            if (raw is long l && l >= int.MinValue && l <= int.MaxValue) return (int)l;
            if (raw is double d && d >= int.MinValue && d <= int.MaxValue) return (int)d;
            if (raw is decimal dec && dec >= int.MinValue && dec <= int.MaxValue) return (int)dec;
            if (raw is string s && int.TryParse(s, out var parsed)) return parsed;
            return fallback;
        }

        static double ReadDouble(IReadOnlyDictionary<string, object> data, string key, double fallback)
        {
            if (!data.TryGetValue(key, out var raw) || raw is null) return fallback;
            if (raw is double d) return d;
            if (raw is decimal dec) return (double)dec;
            if (raw is int i) return i;
            if (raw is string s && double.TryParse(s, out var parsed)) return parsed;
            return fallback;
        }

        static string? ReadString(IReadOnlyDictionary<string, object> data, string key, string? fallback)
        {
            if (!data.TryGetValue(key, out var raw) || raw is null)
            {
                return fallback;
            }

            if (raw is string text)
            {
                return text;
            }

            return fallback;
        }

        static CSharpSecurityFlowMode ReadFlowMode(
            IReadOnlyDictionary<string, object> data,
            string key,
            CSharpSecurityFlowMode fallback)
        {
            if (!data.TryGetValue(key, out var raw) || raw is null)
            {
                return fallback;
            }

            if (raw is CSharpSecurityFlowMode mode)
            {
                return mode;
            }

            if (raw is int i && Enum.IsDefined(typeof(CSharpSecurityFlowMode), i))
            {
                return (CSharpSecurityFlowMode)i;
            }

            if (raw is string s && Enum.TryParse<CSharpSecurityFlowMode>(s, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        static bool ReadPrioritizeExternalSources(
            IReadOnlyDictionary<string, object> data,
            bool fallback)
        {
            // Keep supporting the legacy "UserSources" key for backward compatibility.
            var legacy = ReadBool(data, SolverOptionKeys.CSharpSecurityPrioritizeUserSources, fallback);
            return ReadBool(data, SolverOptionKeys.CSharpSecurityPrioritizeExternalSources, legacy);
        }

        var options = new CSharpMathBugAnalyzerOptions(
            EnableStabilityAnalysis: ReadBool(additionalData, SolverOptionKeys.CSharpMathEnableStability, Default.EnableStabilityAnalysis),
            MaxFindings: ReadInt(additionalData, SolverOptionKeys.CSharpMathMaxFindings, Default.MaxFindings),
            ConfidenceThreshold: ReadDouble(additionalData, SolverOptionKeys.CSharpMathConfidenceThreshold, Default.ConfidenceThreshold),
            MaxSaturationIterations: ReadInt(additionalData, "CSharpMathMaxSaturationIterations", Default.MaxSaturationIterations),
            MaxSamples: ReadInt(additionalData, "CSharpMathSampleBudget", Default.MaxSamples),
            SaturationTimeoutSeconds: ReadDouble(additionalData, "CSharpMathSaturationTimeoutSeconds", Default.SaturationTimeoutSeconds),
            AnalysisTimeoutSeconds: ReadDouble(additionalData, "CSharpMathAnalysisTimeoutSeconds", Default.AnalysisTimeoutSeconds),
            SecurityFlowMode: ReadFlowMode(additionalData, SolverOptionKeys.CSharpSecurityFlowMode, Default.SecurityFlowMode),
            SecurityMaxTraceSteps: ReadInt(additionalData, SolverOptionKeys.CSharpSecurityMaxTraceSteps, Default.SecurityMaxTraceSteps),
            PrioritizeUserSources: ReadPrioritizeExternalSources(additionalData, Default.PrioritizeUserSources),
            RepoScanInfoPath: ReadString(additionalData, SolverOptionKeys.CSharpSecurityRepoScanInfoPath, Default.RepoScanInfoPath),
            EnableGuardProver: ReadBool(additionalData, SolverOptionKeys.CSharpSecurityEnableGuardProver, Default.EnableGuardProver),
            GuardMaxFacts: ReadInt(additionalData, SolverOptionKeys.CSharpSecurityGuardMaxFacts, Default.GuardMaxFacts),
            GuardMaxIterations: ReadInt(additionalData, SolverOptionKeys.CSharpSecurityGuardMaxIterations, Default.GuardMaxIterations),
            GuardTimeoutSeconds: ReadDouble(additionalData, SolverOptionKeys.CSharpSecurityGuardTimeoutSeconds, Default.GuardTimeoutSeconds));

        return options.Normalize();
    }

    public static CSharpMathBugAnalyzerOptions FromSolveContext(SolveContext? context)
    {
        if (context?.AdditionalData is null)
        {
            return Default;
        }

        return FromAdditionalData(context.AdditionalData);
    }
}

public sealed record CSharpMathBugFinding(
    string BugId,
    string Title,
    CSharpMathBugSeverity Severity,
    CSharpSecurityRisk SecurityRisk,
    CSharpMathBugConfidence Confidence,
    double ConfidenceScore,
    string Message,
    string Suggestion,
    string Expression,
    CSharpMathBugSourceSpan? SourceSpan,
    IReadOnlyList<string> Evidence,
    IReadOnlyDictionary<string, double>? WitnessAssignments);

public sealed record CSharpMathBugAnalysisResult(
    IReadOnlyList<CSharpMathBugFinding> Findings,
    IReadOnlyList<string> Diagnostics,
    int CandidateCount,
    int LoweredExpressionCount,
    bool IsComplete = true);
