// Copyright Warren Harding 2026
using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraUnionBatchGroup(
    int AnchorId,
    IReadOnlyList<int> MemberIds);
