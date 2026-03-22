namespace SymCobra.Regions;

public sealed record CobraPreparedUnion(
    int LeftId,
    int RightId,
    ulong PairKey);
