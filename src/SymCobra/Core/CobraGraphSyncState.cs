using System;
using System.Collections.Generic;

namespace SymCobra.Core;

public class CobraGraphSyncState
{
    public int Epoch { get; set; }
    public int Generation { get; set; }
    
    public HashSet<int> DirtyRebuildClasses { get; set; } = new HashSet<int>();
    public HashSet<int> DirtyRepairNodes { get; set; } = new HashSet<int>();
    public HashSet<int> DirtyLegacyClassIds { get; set; } = new HashSet<int>();
    
    // Tracking when full synchronization to/from CPU EGraph is needed or occurred
    public int LastFullSyncEpoch { get; set; }
    public int LastLegacySyncedNodeCount { get; set; }
    public int LastLegacySyncedClassCount { get; set; }
}
