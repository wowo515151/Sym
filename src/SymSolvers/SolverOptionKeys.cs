namespace SymSolvers;

public static class SolverOptionKeys
{
    public const string KeepNegativePowers = nameof(KeepNegativePowers);
    public const string CancelCommonFactors = nameof(CancelCommonFactors);
    public const string RulesFolder = nameof(RulesFolder);
    public const string GeneratedRulesFolder = nameof(GeneratedRulesFolder);
    public const string Strategies = nameof(Strategies);
    public const string CustomStrategies = nameof(CustomStrategies);
    public const string DisableStrategies = nameof(DisableStrategies);
    public const string SearchMode = nameof(SearchMode);
    public const string BeamWidth = nameof(BeamWidth);
    public const string BeamMaxSteps = nameof(BeamMaxSteps);
    public const string BeamOperators = nameof(BeamOperators);
    public const string BeamTargetPenalty = nameof(BeamTargetPenalty);
    public const string BeamValidateGoal = nameof(BeamValidateGoal);
    public const string BeamValidationSamples = nameof(BeamValidationSamples);
    public const string BeamValidationTolerance = nameof(BeamValidationTolerance);
    public const string BeamResidualWeight = nameof(BeamResidualWeight);
    public const string BeamSizeWeight = nameof(BeamSizeWeight);
    public const string BeamDepthWeight = nameof(BeamDepthWeight);
    public const string BeamOpPenaltyWeight = nameof(BeamOpPenaltyWeight);
    public const string BeamMarkerWeight = nameof(BeamMarkerWeight);
    public const string BeamDegreeWeight = nameof(BeamDegreeWeight);
    public const string BeamMaxSeconds = nameof(BeamMaxSeconds);
    public const string TargetVariables = nameof(TargetVariables);
    public const string Substitutions = nameof(Substitutions);
    public const string RootSelectionMode = nameof(RootSelectionMode);
    public const string StopOnFirstChange = nameof(StopOnFirstChange);
    public const string StopOnFirstSuccess = nameof(StopOnFirstSuccess);
    public const string StopOnFailure = nameof(StopOnFailure);
    public const string HybridIntervalIsolation = nameof(HybridIntervalIsolation);
    public const string HybridResidualLimit = nameof(HybridResidualLimit);
    public const string HybridStallLimit = nameof(HybridStallLimit);
    public const string EnableLinearFactorization = nameof(EnableLinearFactorization);
    public const string CostModel = nameof(CostModel);
    public const string EGraphBackend = nameof(EGraphBackend);
    public const string SaturationTimeoutSeconds = nameof(SaturationTimeoutSeconds);
    public const string ExtractionTimeoutSeconds = nameof(ExtractionTimeoutSeconds);
    public const string CobraSkipCompatibilityForDirectHandledRules = nameof(CobraSkipCompatibilityForDirectHandledRules);
    public const string ArithmeticBenchmarkRuleProfile = nameof(ArithmeticBenchmarkRuleProfile);
    public const string CSharpMathBugAnalysis = nameof(CSharpMathBugAnalysis);
    public const string CSharpMathEnableStability = nameof(CSharpMathEnableStability);
    public const string CSharpMathMaxFindings = nameof(CSharpMathMaxFindings);
    public const string CSharpMathConfidenceThreshold = nameof(CSharpMathConfidenceThreshold);
    public const string CSharpSecurityFlowMode = nameof(CSharpSecurityFlowMode);
    public const string CSharpSecurityMaxTraceSteps = nameof(CSharpSecurityMaxTraceSteps);
    // Keep legacy and new names so existing clients keep working during option migration.
    public const string CSharpSecurityPrioritizeUserSources = nameof(CSharpSecurityPrioritizeUserSources);
    public const string CSharpSecurityPrioritizeExternalSources = nameof(CSharpSecurityPrioritizeExternalSources);
    // Keep this key aligned with SymCLI --repo-scan-info and CSharpMathBugAnalyzerOptions.FromAdditionalData.
    public const string CSharpSecurityRepoScanInfoPath = nameof(CSharpSecurityRepoScanInfoPath);

    // Guard prover knobs for C# security flow analyzers.
    public const string CSharpSecurityEnableGuardProver = nameof(CSharpSecurityEnableGuardProver);
    public const string CSharpSecurityGuardMaxFacts = nameof(CSharpSecurityGuardMaxFacts);
    public const string CSharpSecurityGuardMaxIterations = nameof(CSharpSecurityGuardMaxIterations);
    public const string CSharpSecurityGuardTimeoutSeconds = nameof(CSharpSecurityGuardTimeoutSeconds);
}
