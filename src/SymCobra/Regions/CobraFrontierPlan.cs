using System.Collections.Generic;

namespace SymCobra.Regions;

public sealed record CobraFrontierPlan
{
    public CobraFrontierPlan(
        IReadOnlyList<int> orderedClassIds,
        CobraFrontierPrioritySource prioritySource,
        IReadOnlyList<int>? interiorQueueClassIds = null,
        IReadOnlyList<int>? boundaryQueueClassIds = null,
        IReadOnlyList<int>? residualQueueClassIds = null,
        IReadOnlyList<int>? suppressedQueueClassIds = null,
        IReadOnlyDictionary<int, string>? routingReasonsByClassId = null)
    {
        OrderedClassIds = orderedClassIds;
        PrioritySource = prioritySource;
        InteriorQueueClassIds = interiorQueueClassIds ?? [];
        BoundaryQueueClassIds = boundaryQueueClassIds ?? [];
        ResidualQueueClassIds = residualQueueClassIds ?? [];
        SuppressedQueueClassIds = suppressedQueueClassIds ?? [];
        RoutingReasonsByClassId = routingReasonsByClassId ?? new Dictionary<int, string>();
    }

    public IReadOnlyList<int> OrderedClassIds { get; }

    public CobraFrontierPrioritySource PrioritySource { get; }

    public IReadOnlyList<int> InteriorQueueClassIds { get; }

    public IReadOnlyList<int> BoundaryQueueClassIds { get; }

    public IReadOnlyList<int> ResidualQueueClassIds { get; }

    public IReadOnlyList<int> SuppressedQueueClassIds { get; }

    public IReadOnlyDictionary<int, string> RoutingReasonsByClassId { get; }
}
