// Copyright Warren Harding 2026
using System.Collections.Generic;

namespace Sym.Core.EGraph;

public sealed class EGraphUnionBatchResult
{
    public EGraphUnionBatchResult(
        bool changed,
        IReadOnlyList<int> updatedClassIds,
        IReadOnlyList<int> updatedParentIds,
        IReadOnlyList<int> metricClassIds,
        IReadOnlyList<int> metricNodeCounts,
        IReadOnlyList<int> metricGenerations)
    {
        Changed = changed;
        UpdatedClassIds = updatedClassIds;
        UpdatedParentIds = updatedParentIds;
        MetricClassIds = metricClassIds;
        MetricNodeCounts = metricNodeCounts;
        MetricGenerations = metricGenerations;
    }

    public bool Changed { get; }

    public IReadOnlyList<int> UpdatedClassIds { get; }

    public IReadOnlyList<int> UpdatedParentIds { get; }

    public IReadOnlyList<int> MetricClassIds { get; }

    public IReadOnlyList<int> MetricNodeCounts { get; }

    public IReadOnlyList<int> MetricGenerations { get; }
}
