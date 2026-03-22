// Copyright Warren Harding 2026
using Sym.Core;
using Sym.Core.EGraph;

namespace SymCobra.Regions;

public sealed record CobraDirectMatchPair(int ClassId, Rule Rule, ENode Node);
