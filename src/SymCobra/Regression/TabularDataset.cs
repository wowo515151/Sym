using System.Collections.Generic;

namespace SymCobra.Regression;

public sealed record TabularDataset(
    IReadOnlyList<string> FeatureNames,
    IReadOnlyList<double[]> Rows,
    IReadOnlyList<double> Targets);
