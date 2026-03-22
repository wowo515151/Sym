// Copyright Warren Harding 2026
using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraDirectRuleApplication(
    int RootClassId,
    Sym.Core.Rule Rule,
    IReadOnlyDictionary<string, int> Bindings,
    Sym.Core.IExpression? TransformedExpression = null);
