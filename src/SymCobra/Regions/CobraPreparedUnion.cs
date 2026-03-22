// Copyright Warren Harding 2026
namespace SymCobra.Regions;

public sealed record CobraPreparedUnion(
    int LeftId,
    int RightId,
    ulong PairKey);
