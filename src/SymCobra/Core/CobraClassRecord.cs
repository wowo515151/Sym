using System;
using System.Collections.Generic;

namespace SymCobra.Core;

public class CobraClassRecord
{
    public int ClassId { get; set; }
    public int RepresentativeNodeId { get; set; }
    public List<int> NodeIds { get; set; } = new List<int>();
    public List<int> ParentNodeIds { get; set; } = new List<int>();
    public int Generation { get; set; }
    public int NodeCount => NodeIds.Count;

    // Metadata / properties for shapes or specific domain data
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

    public CobraClassRecord(int classId)
    {
        ClassId = classId;
    }
}
