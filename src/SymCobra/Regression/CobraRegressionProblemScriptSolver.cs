// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SymCobra.Regression;

public sealed class CobraRegressionProblemScriptSolver
{
    public string Solve(IDictionary<string, string> options, CancellationToken ct = default)
    {
        if (!options.TryGetValue("RegressionDataset", out var datasetPath) || string.IsNullOrWhiteSpace(datasetPath))
        {
            throw new InvalidOperationException("RegressionDataset must be provided when RegressionMode is enabled.");
        }

        if (!options.TryGetValue("RegressionTarget", out var targetColumn) || string.IsNullOrWhiteSpace(targetColumn))
        {
            throw new InvalidOperationException("RegressionTarget must be provided when RegressionMode is enabled.");
        }

        var featureColumns = options.TryGetValue("RegressionFeatures", out var featuresRaw) && !string.IsNullOrWhiteSpace(featuresRaw)
            ? featuresRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        double complexityPenalty = options.TryGetValue("RegressionComplexityPenalty", out var penaltyRaw) &&
                                   double.TryParse(penaltyRaw, out var parsedPenalty)
            ? parsedPenalty
            : 0.01;

        int maxCandidates = options.TryGetValue("RegressionMaxCandidates", out var maxCandidatesRaw) &&
                            int.TryParse(maxCandidatesRaw, out var parsedCandidates)
            ? parsedCandidates
            : 64;

        var loss = options.TryGetValue("RegressionLoss", out var lossRaw) && !string.IsNullOrWhiteSpace(lossRaw)
            ? lossRaw
            : "MSE";

        var engine = new CobraRegressionEngine();
        var result = engine.SolveTabular(
            new CobraRegressionOptions(datasetPath, targetColumn, featureColumns, complexityPenalty, maxCandidates, loss),
            ct);

        return $"{result.BestExpression.ToDisplayString()}{Environment.NewLine}Score: {result.BestScore:0.######}";
    }
}
