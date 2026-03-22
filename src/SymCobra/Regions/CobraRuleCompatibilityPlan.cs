using System.Collections.Generic;
using Sym.Core;

namespace SymCobra.Regions;

public sealed record CobraRuleCompatibilityPlan(
    IReadOnlyDictionary<int, IReadOnlyList<Rule>> RulesByClassId,
    CobraRuleCompatibilitySource Source);
