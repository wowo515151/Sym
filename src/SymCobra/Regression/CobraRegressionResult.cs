// Copyright Warren Harding 2026
using Sym.Core;

namespace SymCobra.Regression;

public sealed record CobraRegressionResult(
    IExpression BestExpression,
    double BestScore,
    int CandidateCount,
    string Summary);
