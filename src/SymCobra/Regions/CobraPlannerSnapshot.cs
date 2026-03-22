using Sym.Core.EGraph;
using SymCobra.Core;

namespace SymCobra.Regions;

public sealed class CobraPlannerSnapshot
{
    private CobraPlannerSnapshot(
        int[] rootIds,
        int[] nodeCounts,
        int[] generations,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks)
    {
        RootIds = rootIds;
        NodeCounts = nodeCounts;
        Generations = generations;
        ClassConstraintMasks = classConstraintMasks;
        ClassHeadBucketMasks = classHeadBucketMasks;
        ClassExactHeadMasks = classExactHeadMasks;
        ClassChildEqualityMasks = classChildEqualityMasks;
        ClassChildAtomBucketMasks = classChildAtomBucketMasks;
        ClassChildConstraintMasks = classChildConstraintMasks;
        ClassChildReferenceBloomMasks = classChildReferenceBloomMasks;
    }

    public int[] RootIds { get; }

    public int[] NodeCounts { get; }

    public int[] Generations { get; }

    public int[] ClassConstraintMasks { get; }

    public int[] ClassHeadBucketMasks { get; }

    public int[] ClassExactHeadMasks { get; }

    public int[] ClassChildEqualityMasks { get; }

    public int[] ClassChildAtomBucketMasks { get; }

    public int[] ClassChildConstraintMasks { get; }

    public int[] ClassChildReferenceBloomMasks { get; }

    public static CobraPlannerSnapshot Create(EGraph graph)
    {
        int[] rootIds = new int[graph.GetRootIds().Count];
        var graphRootIds = graph.GetRootIds();
        for (int i = 0; i < graphRootIds.Count; i++)
        {
            rootIds[i] = graphRootIds[i];
        }

        int[] nodeCounts = new int[graph.ClassCount];
        int[] generations = new int[graph.ClassCount];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            var eClass = graph.GetClass(classId);
            nodeCounts[classId] = eClass.Nodes.Count;
            generations[classId] = eClass.Generation;
        }

        return new CobraPlannerSnapshot(
            rootIds,
            nodeCounts,
            generations,
            CobraNodeMatchEncoding.BuildClassConstraintMasks(graph),
            CobraNodeMatchEncoding.BuildClassHeadBucketMasks(graph),
            CobraNodeMatchEncoding.BuildClassExactHeadMasks(graph),
            CobraNodeMatchEncoding.BuildClassChildEqualityMasks(graph),
            CobraNodeMatchEncoding.BuildClassChildAtomBucketMasks(graph),
            CobraNodeMatchEncoding.BuildClassChildConstraintMasks(graph),
            CobraNodeMatchEncoding.BuildClassChildReferenceBloomMasks(graph));
    }

    public static CobraPlannerSnapshot Create(CobraGraphState graphState)
    {
        int[] rootIds = BuildRootIds(graphState);
        int[] nodeCounts = new int[graphState.ClassCount];
        int[] generations = new int[graphState.ClassCount];

        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            var cobraClass = graphState.GetClass(classId);
            nodeCounts[classId] = cobraClass.NodeIds.Count;
            generations[classId] = cobraClass.Generation;
        }

        return new CobraPlannerSnapshot(
            rootIds,
            nodeCounts,
            generations,
            CobraNodeMatchEncoding.BuildClassConstraintMasks(graphState),
            CobraNodeMatchEncoding.BuildClassHeadBucketMasks(graphState),
            CobraNodeMatchEncoding.BuildClassExactHeadMasks(graphState),
            CobraNodeMatchEncoding.BuildClassChildEqualityMasks(graphState),
            CobraNodeMatchEncoding.BuildClassChildAtomBucketMasks(graphState),
            CobraNodeMatchEncoding.BuildClassChildConstraintMasks(graphState),
            CobraNodeMatchEncoding.BuildClassChildReferenceBloomMasks(graphState));
    }

    private static int[] BuildRootIds(CobraGraphState graphState)
    {
        var rootIds = new List<int>();
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            if (graphState.Find(classId) == classId && graphState.HasMaterializedNodes(classId))
            {
                rootIds.Add(classId);
            }
        }

        return rootIds.ToArray();
    }
}
