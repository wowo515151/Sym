// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Sym.Core.EGraph;
using SymCobra.Runtime;

namespace SymCobra.Regions;

public static class CobraRegionDetector
{
    private readonly record struct CobraRegionStructureMetadata(
        int[] BoundaryClassIds,
        int NodeCount,
        int BoundaryCount,
        bool HasResidual,
        bool HasTranspose,
        bool HasMatMul,
        bool HasTensorAdd,
        bool HasFused,
        bool HasRelu,
        int MatMulLikeNodeCount,
        int RepeatedLeftChildCount,
        int RepeatedRightChildCount,
        int StructuredParentCount,
        int SharedSinkParentCount);

    private static readonly HashSet<string> SharedSinkHeads = new(StringComparer.Ordinal)
    {
        "MatMul",
        "FusedMatMulAdd",
        "FusedMatMulAddRelu"
    };

    public static IReadOnlyList<CobraRegion> Detect(EGraph graph, CancellationToken ct = default)
    {
        return Detect(graph, CobraPlannerSnapshot.Create(graph), ct);
    }

    public static IReadOnlyList<CobraRegion> Detect(EGraph graph, CobraPlannerSnapshot snapshot, CancellationToken ct = default)
    {
        // Cheap bypass: if graph is completely trivial, skip CUDA overhead.
        // Integration tests for structured regions use smaller counts, so we set this low.
        if (graph.ClassCount < 3)
        {
            return Array.Empty<CobraRegion>();
        }

        int[] familyCodes;
        bool detectedWithCuda = false;

        var headBuckets = snapshot.ClassHeadBucketMasks;
        var exactHeads = snapshot.ClassExactHeadMasks;

        if (CobraCudaNative.TryWarmRegionDetectionMasks(headBuckets, exactHeads) &&
            CobraCudaNative.TryDetectRegionsBatchCached(graph.ClassCount, out familyCodes))
        {
            detectedWithCuda = true;
        }
        else if (CobraCudaNative.TryDetectRegionsBatch(headBuckets, exactHeads, out familyCodes))
        {
            detectedWithCuda = true;
        }

        List<CobraRegionDraft> drafts;
        if (detectedWithCuda)
        {
            drafts = BuildCudaDrafts(graph, familyCodes, ct);
            if (drafts.Count == 0)
            {
                drafts = BuildCpuDrafts(graph, ct);
            }
        }
        else
        {
            drafts = BuildCpuDrafts(graph, ct);
        }

        CobraRegion[] regions;
        if (!CobraCudaRegionScorer.TryScore(drafts.ToArray(), out regions))
        {
            regions = drafts
                .Select(draft => draft.ToRegion(
                    ComputeBenefitScore(draft),
                    ComputeConflictScore(draft),
                    CobraScoreSource.CpuHeuristic))
                .ToArray();
        }

        return regions
            .OrderByDescending(r => r.BenefitScore - (r.ConflictScore * 0.5))
            .ThenBy(r => r.RegionId)
            .ToList();
    }

    private static List<CobraRegionDraft> BuildCudaDrafts(EGraph graph, int[] familyCodes, CancellationToken ct)
    {
        var drafts = new List<CobraRegionDraft>();
        int nextRegionId = 0;

        foreach (var rootId in graph.GetRootIds())
        {
            ct.ThrowIfCancellationRequested();
            if ((uint)rootId >= (uint)familyCodes.Length)
            {
                continue;
            }

            int code = familyCodes[rootId];
            var members = new HashSet<int> { rootId };
            var metadata = AnalyzeRegion(graph, members, ct);
            var expandedFamily = ClassifyFamily(metadata);
            var family = expandedFamily != CobraRegionFamily.Unknown
                ? expandedFamily
                : (CobraRegionFamily)code;
            if (family == CobraRegionFamily.Unknown)
            {
                continue;
            }
            
            // CUDA gives a fast seed family, but expanded analysis has the full structured context.
            drafts.Add(new CobraRegionDraft(
                RegionId: nextRegionId++,
                Family: family,
                MemberClassIds: members.ToArray(),
                BoundaryClassIds: metadata.BoundaryClassIds,
                NodeCount: metadata.NodeCount,
                BoundaryCount: metadata.BoundaryCount,
                HasResidualBranch: metadata.HasResidual,
                HasTransposeBoundary: metadata.HasTranspose,
                Summary: $"{family} region (CUDA detected) rooted at class {rootId} with {members.Count} member(s)"));
        }

        return drafts;
    }

    private static List<CobraRegionDraft> BuildCpuDrafts(EGraph graph, CancellationToken ct)
    {
        var drafts = new List<CobraRegionDraft>();
        int nextRegionId = 0;

        foreach (var rootId in graph.GetRootIds())
        {
            ct.ThrowIfCancellationRequested();
            var eClass = graph.GetClass(rootId);
            if (eClass.Nodes.Count == 0)
            {
                continue;
            }

            var members = new HashSet<int> { rootId };
            var metadata = AnalyzeRegion(graph, members, ct);

            CobraRegionFamily family = ClassifyFamily(metadata);

            if (family == CobraRegionFamily.Unknown)
            {
                continue;
            }

            drafts.Add(new CobraRegionDraft(
                RegionId: nextRegionId++,
                Family: family,
                MemberClassIds: members.ToArray(),
                BoundaryClassIds: metadata.BoundaryClassIds,
                NodeCount: metadata.NodeCount,
                BoundaryCount: metadata.BoundaryCount,
                HasResidualBranch: metadata.HasResidual,
                HasTransposeBoundary: metadata.HasTranspose,
                Summary: $"{family} region rooted at class {rootId} with {members.Count} member(s)"));
        }

        return drafts;
    }

    private static CobraRegionFamily ClassifyFamily(CobraRegionStructureMetadata metadata)
    {
        if (metadata.HasTranspose && (metadata.HasMatMul || metadata.HasFused))
        {
            return CobraRegionFamily.TransposeBoundaryCore;
        }

        if (metadata.SharedSinkParentCount >= 2 && metadata.HasTensorAdd)
        {
            return CobraRegionFamily.BilinearOverlap;
        }

        if (metadata.RepeatedLeftChildCount > 0 && metadata.RepeatedRightChildCount > 0)
        {
            return CobraRegionFamily.BilinearOverlap;
        }

        if ((metadata.MatMulLikeNodeCount >= 2 || metadata.SharedSinkParentCount >= 2) && (metadata.HasRelu || metadata.HasFused))
        {
            return CobraRegionFamily.SharedSink;
        }

        if (metadata.RepeatedRightChildCount > metadata.RepeatedLeftChildCount)
        {
            return CobraRegionFamily.RightFactorPack;
        }

        if (metadata.RepeatedLeftChildCount > 0 || (metadata.HasMatMul && metadata.HasTensorAdd))
        {
            return CobraRegionFamily.LeftFactorPack;
        }

        if (metadata.HasResidual && metadata.HasMatMul)
        {
            return CobraRegionFamily.ResidualCoreBundle;
        }

        if (metadata.SharedSinkParentCount >= 2)
        {
            return CobraRegionFamily.SharedSink;
        }

        if (metadata.HasMatMul && metadata.MatMulLikeNodeCount >= 2)
        {
            return CobraRegionFamily.SharedSink;
        }

        if (metadata.HasMatMul)
        {
            return CobraRegionFamily.LeftFactorPack;
        }

        return CobraRegionFamily.Unknown;
    }

    private static CobraRegionStructureMetadata AnalyzeRegion(EGraph graph, HashSet<int> members, CancellationToken ct)
    {
        var boundaryClasses = new HashSet<int>();
        bool hasResidual = false;
        bool hasTranspose = false;
        bool hasMatMul = false;
        bool hasTensorAdd = false;
        bool hasFused = false;
        bool hasRelu = false;
        int nodeCount = 0;
        int matMulLikeNodeCount = 0;
        var leftChildCounts = new Dictionary<int, int>();
        var rightChildCounts = new Dictionary<int, int>();
        var structuredParentClassIds = new HashSet<int>();
        var sharedSinkParentClassIds = new HashSet<int>();

        var queue = new Queue<int>(members);
        var visitedInExpansion = new HashSet<int>(members);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            int classId = queue.Dequeue();
            var eClass = graph.GetClass(classId);
            nodeCount += eClass.Nodes.Count;
            var parentClassIds = graph.GetParentClassIds(classId);
            var expandableParentClassIds = new List<int>();

            foreach (int parentClassId in parentClassIds)
            {
                if (!IsStructuredClass(graph, parentClassId))
                {
                    boundaryClasses.Add(parentClassId);
                    continue;
                }

                structuredParentClassIds.Add(parentClassId);
                if (ContainsSharedSinkHead(graph, parentClassId))
                {
                    sharedSinkParentClassIds.Add(parentClassId);
                }

                if (ShouldExpandToStructuredParent(graph, classId, parentClassId, parentClassIds.Count))
                {
                    expandableParentClassIds.Add(parentClassId);
                }
                else
                {
                    boundaryClasses.Add(parentClassId);
                }
            }

            foreach (int parentClassId in expandableParentClassIds)
            {
                if (visitedInExpansion.Add(parentClassId))
                {
                    members.Add(parentClassId);
                    queue.Enqueue(parentClassId);
                }
            }

            foreach (var node in eClass.Nodes)
            {
                string head = node.Head;
                hasResidual |= head is "TensorAdd" or "Add" or "Vector";
                hasTranspose |= head == "Transpose";
                hasMatMul |= SharedSinkHeads.Contains(head);
                hasTensorAdd |= head is "TensorAdd" or "Add";
                hasFused |= head.StartsWith("Fused", StringComparison.Ordinal);
                hasRelu |= head == "Relu";
                if (SharedSinkHeads.Contains(head))
                {
                    matMulLikeNodeCount++;
                    if (node.Children.Count > 0)
                    {
                        int leftChildId = graph.Find(node.Children[0]);
                        leftChildCounts[leftChildId] = leftChildCounts.TryGetValue(leftChildId, out int leftCount)
                            ? leftCount + 1
                            : 1;
                    }

                    if (node.Children.Count > 1)
                    {
                        int rightChildId = graph.Find(node.Children[1]);
                        rightChildCounts[rightChildId] = rightChildCounts.TryGetValue(rightChildId, out int rightCount)
                            ? rightCount + 1
                            : 1;
                    }
                }

                foreach (var childId in node.Children)
                {
                    int canonicalChildId = graph.Find(childId);
                    if (!visitedInExpansion.Contains(canonicalChildId))
                    {
                        if (IsStructuredClass(graph, canonicalChildId))
                        {
                            visitedInExpansion.Add(canonicalChildId);
                            members.Add(canonicalChildId);
                            queue.Enqueue(canonicalChildId);
                        }
                        else
                        {
                            boundaryClasses.Add(canonicalChildId);
                        }
                    }
                }
            }
        }

        // Clean up boundary: remove members from boundary
        boundaryClasses.ExceptWith(members);

        return new CobraRegionStructureMetadata(
            boundaryClasses.ToArray(),
            nodeCount,
            boundaryClasses.Count,
            hasResidual,
            hasTranspose,
            hasMatMul,
            hasTensorAdd,
            hasFused,
            hasRelu,
            matMulLikeNodeCount,
            leftChildCounts.Values.Count(static count => count > 1),
            rightChildCounts.Values.Count(static count => count > 1),
            structuredParentClassIds.Count,
            sharedSinkParentClassIds.Count);
    }

    private static bool IsStructuredClass(EGraph graph, int classId)
    {
        var eClass = graph.GetClass(classId);
        foreach (var node in eClass.Nodes)
        {
            string head = node.Head;
            if (SharedSinkHeads.Contains(head) || 
                head == "Transpose" || 
                head == "Relu" || 
                head == "TensorAdd" || 
                head == "Add" ||
                head.StartsWith("Fused", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsSharedSinkHead(EGraph graph, int classId)
    {
        var eClass = graph.GetClass(classId);
        foreach (var node in eClass.Nodes)
        {
            if (SharedSinkHeads.Contains(node.Head))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldExpandToStructuredParent(EGraph graph, int childClassId, int parentClassId, int totalParentCount)
    {
        if (totalParentCount >= 2)
        {
            return true;
        }

        var parentClass = graph.GetClass(parentClassId);
        return parentClass.Nodes.Any(node =>
            node.Children.Count == 1 &&
            graph.Find(node.Children[0]) == childClassId &&
            (node.Head == "Relu" || node.Head == "Transpose" || node.Head.StartsWith("Fused", StringComparison.Ordinal)));
    }

    private static (int[] BoundaryClassIds, int BoundaryCount, bool HasResidual, bool HasTranspose, bool HasMatMul, bool HasTensorAdd, bool HasFused) AnalyzeClass(EGraph graph, EClass eClass)
    {
        var boundaryClasses = new HashSet<int>();
        bool hasResidual = false;
        bool hasTranspose = false;
        bool hasMatMul = false;
        bool hasTensorAdd = false;
        bool hasFused = false;

        foreach (var node in eClass.Nodes)
        {
            string head = node.Head;
            hasResidual |= head is "TensorAdd" or "Add" or "Vector";
            hasTranspose |= head == "Transpose";
            hasMatMul |= SharedSinkHeads.Contains(head);
            hasTensorAdd |= head is "TensorAdd" or "Add";
            hasFused |= head.StartsWith("Fused", StringComparison.Ordinal);

            foreach (var childId in node.Children)
            {
                boundaryClasses.Add(graph.Find(childId));
            }
        }

        return (boundaryClasses.ToArray(), boundaryClasses.Count, hasResidual, hasTranspose, hasMatMul, hasTensorAdd, hasFused);
    }

    private static double ComputeBenefitScore(CobraRegionDraft draft)
    {
        double benefit = draft.Family switch
        {
            CobraRegionFamily.TransposeBoundaryCore => 6,
            CobraRegionFamily.SharedSink => 8,
            CobraRegionFamily.BilinearOverlap => 7,
            CobraRegionFamily.ResidualCoreBundle => 6,
            CobraRegionFamily.LeftFactorPack => 4,
            CobraRegionFamily.RightFactorPack => 4,
            _ => 0
        };

        benefit += Math.Min(4, draft.NodeCount);
        return benefit;
    }

    private static double ComputeConflictScore(CobraRegionDraft draft)
    {
        double conflict = draft.Family switch
        {
            CobraRegionFamily.TransposeBoundaryCore => 2,
            CobraRegionFamily.SharedSink => 3,
            CobraRegionFamily.BilinearOverlap => 5,
            CobraRegionFamily.ResidualCoreBundle => 4,
            CobraRegionFamily.LeftFactorPack => 2,
            CobraRegionFamily.RightFactorPack => 2,
            _ => 0
        };

        conflict += Math.Max(0, draft.BoundaryCount - 2);
        return conflict;
    }
}
