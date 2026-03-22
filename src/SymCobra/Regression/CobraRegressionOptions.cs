// Copyright Warren Harding 2026
using System.Collections.Generic;

namespace SymCobra.Regression;

public sealed record CobraRegressionOptions(
    string DatasetPath,
    string TargetColumn,
    IReadOnlyList<string>? FeatureColumns,
    double ComplexityPenalty,
    int MaxCandidates,
    string LossFunction);
