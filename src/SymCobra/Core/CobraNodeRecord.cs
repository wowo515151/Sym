// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;

namespace SymCobra.Core;

/// <summary>
/// Represents a node within the COBRA authoritative graph state.
/// </summary>
public struct CobraNodeRecord
{
    public string Head { get; set; }
    public int HeadCode { get; set; }
    public int[] CanonicalChildIds { get; set; }
    public int ClassId { get; set; }
    public string? Literal { get; set; }

    public int Arity => CanonicalChildIds?.Length ?? 0;

    public CobraNodeRecord(string head, int headCode, int[] canonicalChildIds, int classId, string? literal = null)
    {
        Head = head;
        HeadCode = headCode;
        CanonicalChildIds = canonicalChildIds;
        ClassId = classId;
        Literal = literal;
    }
}
