using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using SymCobra.Regions;
using SymCobra.Telemetry;

namespace SymCobra.Runtime;

public static class CobraCudaNative
{
    private const string LibraryName = "cobra_cuda.dll";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_device_count")]
    private static extern int cobra_device_count();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_batch_mse_scores")]
    private static extern int cobra_batch_mse_scores(
        double[] predictions,
        double[] targets,
        int samples,
        int candidates,
        double[] results);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_regions")]
    private static extern int cobra_score_regions(
        int[] familyCodes,
        int[] nodeCounts,
        int[] boundaryCounts,
        int regionCount,
        double[] benefitScores,
        double[] conflictScores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_matches")]
    private static extern int cobra_score_matches(
        int[] hotFlags,
        int[] boundaryFlags,
        int[] suppressedFlags,
        int matchCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_classes")]
    private static extern int cobra_score_classes(
        int[] nodeCounts,
        int[] generations,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_prepare_unions")]
    private static extern int cobra_prepare_unions(
        int[] leftIds,
        int[] rightIds,
        int pairCount,
        int[] normalizedLeft,
        int[] normalizedRight,
        ulong[] pairKeys);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_rule_compatibility")]
    private static extern int cobra_score_rule_compatibility(
        int[] classMasks,
        int[] ruleMasks,
        int classCount,
        int ruleCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_rebuild_classes")]
    private static extern int cobra_score_rebuild_classes(
        int[] nodeCounts,
        int[] generations,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_analysis_classes")]
    private static extern int cobra_score_analysis_classes(
        int[] nodeCounts,
        int[] generations,
        int[] unresolvedFlags,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_resolve_union_roots")]
    private static extern int cobra_resolve_union_roots(
        int[] parents,
        int parentCount,
        int[] leftIds,
        int[] rightIds,
        int pairCount,
        int[] resolvedLeft,
        int[] resolvedRight,
        ulong[] pairKeys);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_cache_parent_snapshot")]
    private static extern int cobra_cache_parent_snapshot(
        int[] parents,
        int parentCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_resolve_union_roots_cached")]
    private static extern int cobra_resolve_union_roots_cached(
        int[] leftIds,
        int[] rightIds,
        int pairCount,
        int[] resolvedLeft,
        int[] resolvedRight,
        ulong[] pairKeys);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_apply_parent_updates_cached")]
    private static extern int cobra_apply_parent_updates_cached(
        int[] classIds,
        int[] parentIds,
        int updateCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_mark_repair_candidates")]
    private static extern int cobra_mark_repair_candidates(
        int[] parents,
        int parentCount,
        int[] childStarts,
        int[] childCounts,
        int[] childIds,
        int nodeCount,
        int[] dirtyFlags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_cache_repair_snapshot")]
    private static extern int cobra_cache_repair_snapshot(
        int[] parents,
        int parentCount,
        int[] childStarts,
        int[] childCounts,
        int[] childIds,
        int nodeCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_mark_repair_candidates_cached")]
    private static extern int cobra_mark_repair_candidates_cached(
        int[] dirtyFlags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_union_batch_gpu_cached")]
    private static extern int cobra_union_batch_gpu_cached(
        int[] leftIds,
        int[] rightIds,
        int pairCount,
        ref int changed);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_init_gpu_hash_table")]
    private static extern int cobra_init_gpu_hash_table(int size);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_lookup_or_insert_nodes")]
    private static extern int cobra_lookup_or_insert_nodes(
        int[] headHashes,
        int[] arities,
        int[] childStarts,
        int[] childIds,
        int nodeCount,
        int totalChildCount,
        ref int nextClassId,
        int[] resultClassIds);

    public static bool TryInitGpuHashTable(int size)
    {
        if (!TryLoad()) return false;
        int status;
        try { status = cobra_init_gpu_hash_table(size); }
        catch { return false; }
        return status == 0;
    }

    public static bool TryLookupOrInsertNodes(
        int[] headHashes,
        int[] arities,
        int[] childStarts,
        int[] childIds,
        ref int nextClassId,
        out int[] resultClassIds)
    {
        resultClassIds = Array.Empty<int>();
        if (!TryLoad()) return false;
        int nodeCount = headHashes.Length;
        if (nodeCount <= 0) return true;

        var results = new int[nodeCount];
        int status;
        try { status = cobra_lookup_or_insert_nodes(headHashes, arities, childStarts, childIds, nodeCount, childIds.Length, ref nextClassId, results); }
        catch { return false; }

        if (status != 0) return false;
        resultClassIds = results;
        return true;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_get_parent_snapshot")]
    private static extern int cobra_get_parent_snapshot(
        int[] hostParents,
        int parentCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_canonicalize_and_hash_nodes_cached")]
    private static extern int cobra_canonicalize_and_hash_nodes_cached(
        int[] nodeHeadHashes,
        int[] nodeChildStarts,
        int[] nodeChildCounts,
        int[] nodeChildIds,
        int nodeCount,
        int totalChildCount,
        int[] nodeHashes);

    public static bool TryCanonicalizeAndHashNodes(
        int[] nodeHeadHashes,
        int[] nodeChildStarts,
        int[] nodeChildCounts,
        int[] nodeChildIds,
        out int[] nodeHashes)
    {
        nodeHashes = Array.Empty<int>();
        if (!TryLoad()) return false;
        int nodeCount = nodeHeadHashes.Length;
        if (nodeCount <= 0) return true;
        int totalChildCount = nodeChildIds.Length;

        var result = new int[nodeCount];
        int status;
        try { status = cobra_canonicalize_and_hash_nodes_cached(nodeHeadHashes, nodeChildStarts, nodeChildCounts, nodeChildIds, nodeCount, totalChildCount, result); }
        catch { return false; }

        if (status != 0) return false;
        nodeHashes = result;
        return true;
    }

    public static bool TryWarmParents(int[] parents)
    {
        if (!TryLoad()) return false;
        if (parents == null || parents.Length == 0) return false;
        try
        {
            return EnsureParentSnapshotCached(parents);
        }
        catch { return false; }
    }

    public static int[] GetParentsSnapshot()
    {
        if (!TryLoad()) return Array.Empty<int>();
        if (_cachedParentLength <= 0) return Array.Empty<int>();
        int[] result = new int[_cachedParentLength];
        int status;
        try { status = cobra_get_parent_snapshot(result, _cachedParentLength); }
        catch { return Array.Empty<int>(); }
        return status == 0 ? result : Array.Empty<int>();
    }

    public static int[] GetParentsSnapshot(int count)
    {
        if (!TryLoad() || count <= 0) return Array.Empty<int>();
        int[] result = new int[count];
        int status;
        try { status = cobra_get_parent_snapshot(result, count); }
        catch { return Array.Empty<int>(); }
        return status == 0 ? result : Array.Empty<int>();
    }

    public static bool TryUnionBatchGpuCached(
        int[] leftIds,
        int[] rightIds,
        out bool changed)
    {
        changed = false;
        if (!TryLoad()) return false;
        int pairCount = leftIds.Length;
        if (pairCount <= 0) return true;

        int hChanged = 0;
        int status;
        try { status = cobra_union_batch_gpu_cached(leftIds, rightIds, pairCount, ref hChanged); }
        catch { return false; }

        if (status != 0) return false;
        changed = hChanged != 0;
        return true;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_group_unions_v2")]
    private static extern int cobra_group_unions_v2(
        int[] leftIds,
        int[] rightIds,
        int pairCount,
        int maxClassId,
        int[] groupKeys);

    public static bool TryGroupUnionsV2(
        int[] leftIds,
        int[] rightIds,
        int maxClassId,
        out int[] groupKeys)
    {
        groupKeys = Array.Empty<int>();
        if (!TryLoad()) return false;
        int pairCount = leftIds.Length;
        if (pairCount <= 0) return false;

        var result = new int[pairCount];
        int status;
        try { status = cobra_group_unions_v2(leftIds, rightIds, pairCount, maxClassId, result); }
        catch { return false; }

        if (status != 0) return false;
        groupKeys = result;
        return true;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_group_unions")]
    private static extern int cobra_group_unions(
        int[] leftIds,
        int[] rightIds,
        int pairCount,
        int[] groupKeys);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_union_members")]
    private static extern int cobra_score_union_members(
        int[] memberIds,
        int memberCount,
        int[] nodeCounts,
        int[] generations,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_union_members_cached")]
    private static extern int cobra_score_union_members_cached(
        int[] memberIds,
        int memberCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_pairs")]
    private static extern int cobra_score_direct_pairs(
        int[] classIds,
        int[] nodeArities,
        int[] generations,
        int[] nodeCounts,
        int pairCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_classes")]
    private static extern int cobra_score_direct_classes(
        int[] pairCounts,
        int[] generations,
        int[] nodeCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_region_selection")]
    private static extern int cobra_score_region_selection(
        int[] benefitScores,
        int[] conflictScores,
        int[] residualFlags,
        int[] transposeFlags,
        int[] boundaryCounts,
        int regionCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_rule_order")]
    private static extern int cobra_score_rule_order(
        int[] compatibilityCounts,
        int[] arities,
        int[] wildcardFlags,
        int ruleCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_candidate_rules")]
    private static extern int cobra_score_candidate_rules(
        int[] allowedCounts,
        int[] arities,
        int[] directFlags,
        int ruleCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_rules")]
    private static extern int cobra_score_direct_rules(
        int[] pairCounts,
        int[] arities,
        int[] nestedFlags,
        int ruleCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_prepared_unions")]
    private static extern int cobra_score_prepared_unions(
        int[] leftGenerations,
        int[] rightGenerations,
        int[] leftNodeCounts,
        int[] rightNodeCounts,
        int pairCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_prepared_unions_cached")]
    private static extern int cobra_score_prepared_unions_cached(
        int[] leftIds,
        int[] rightIds,
        int pairCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_rebuild_with_repair")]
    private static extern int cobra_score_rebuild_with_repair(
        int[] nodeCounts,
        int[] generations,
        int[] repairCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_analysis_with_repair")]
    private static extern int cobra_score_analysis_with_repair(
        int[] nodeCounts,
        int[] generations,
        int[] unresolvedFlags,
        int[] repairCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_rebuild_with_repair_cached")]
    private static extern int cobra_score_rebuild_with_repair_cached(
        int[] repairCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_rebuild_with_repair_by_id_cached")]
    private static extern int cobra_score_rebuild_with_repair_by_id_cached(
        int[] classIds,
        int[] repairCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_analysis_with_repair_cached")]
    private static extern int cobra_score_analysis_with_repair_cached(
        int[] unresolvedFlags,
        int[] repairCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_analysis_with_repair_by_id_cached")]
    private static extern int cobra_score_analysis_with_repair_by_id_cached(
        int[] classIds,
        int[] unresolvedFlags,
        int[] repairCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_match_priority_v2")]
    private static extern int cobra_score_match_priority_v2(
        int[] hotFlags,
        int[] boundaryFlags,
        int[] suppressedFlags,
        int[] ruleArities,
        int[] directFlags,
        int matchCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_frontier_v2")]
    private static extern int cobra_score_frontier_v2(
        int[] nodeCounts,
        int[] generations,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int[] hotRegionCounts,
        int[] boundaryRegionCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_frontier_v2_by_id_cached")]
    private static extern int cobra_score_frontier_v2_by_id_cached(
        int[] classIds,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int[] hotRegionCounts,
        int[] boundaryRegionCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_frontier_v3")]
    private static extern int cobra_score_frontier_v3(
        int[] nodeCounts,
        int[] generations,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int[] suppressedFlags,
        int[] hotRegionCounts,
        int[] boundaryRegionCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_frontier_v3_by_id_cached")]
    private static extern int cobra_score_frontier_v3_by_id_cached(
        int[] classIds,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int[] suppressedFlags,
        int[] hotRegionCounts,
        int[] boundaryRegionCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_classes_v2")]
    private static extern int cobra_score_direct_classes_v2(
        int[] pairCounts,
        int[] nestedPairCounts,
        int[] generations,
        int[] nodeCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_classes_v2_cached")]
    private static extern int cobra_score_direct_classes_v2_cached(
        int[] pairCounts,
        int[] nestedPairCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_classes_v2_by_id_cached")]
    private static extern int cobra_score_direct_classes_v2_by_id_cached(
        int[] classIds,
        int[] pairCounts,
        int[] nestedPairCounts,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_repair_candidates_v2")]
    private static extern int cobra_score_repair_candidates_v2(
        int[] classIds,
        int[] childCounts,
        int[] generations,
        int[] nodeCounts,
        int[] boundaryFlags,
        int candidateCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_repair_candidates_v2_by_id_cached")]
    private static extern int cobra_score_repair_candidates_v2_by_id_cached(
        int[] classIds,
        int[] childCounts,
        int[] boundaryFlags,
        int candidateCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_repair_application_groups")]
    private static extern int cobra_score_repair_application_groups(
        int[] anchorIds,
        int[] memberCounts,
        int[] generations,
        int[] nodeCounts,
        int groupCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_region_selection_v2")]
    private static extern int cobra_score_region_selection_v2(
        int[] familyCodes,
        int[] benefitScores,
        int[] conflictScores,
        int[] residualFlags,
        int[] transposeFlags,
        int[] boundaryCounts,
        int regionCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_pairs_v2")]
    private static extern int cobra_score_direct_pairs_v2(
        int[] classIds,
        int[] nodeArities,
        int[] generations,
        int[] nodeCounts,
        int[] nestedFlags,
        int pairCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_pairs_v2_cached")]
    private static extern int cobra_score_direct_pairs_v2_cached(
        int[] classIds,
        int[] nodeArities,
        int[] nestedFlags,
        int pairCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_pairs_v2_by_id_cached")]
    private static extern int cobra_score_direct_pairs_v2_by_id_cached(
        int[] classIds,
        int[] nodeArities,
        int[] nestedFlags,
        int pairCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_match_priority_v3")]
    private static extern int cobra_score_match_priority_v3(
        int[] hotFlags,
        int[] boundaryFlags,
        int[] suppressedFlags,
        int[] ruleArities,
        int[] directFlags,
        int[] nestedFlags,
        int matchCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_union_groups")]
    private static extern int cobra_score_union_groups(
        int[] anchorIds,
        int[] memberCounts,
        int[] generations,
        int[] nodeCounts,
        int groupCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_extract_classes")]
    private static extern int cobra_score_extract_classes(
        int[] nodeCounts,
        int[] generations,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_extract_classes_cached")]
    private static extern int cobra_score_extract_classes_cached(
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_extract_classes_by_id_cached")]
    private static extern int cobra_score_extract_classes_by_id_cached(
        int[] classIds,
        int classCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_extract_nodes_cached")]
    private static extern int cobra_score_extract_nodes_cached(
        int[] headCodes,
        int[] arities,
        int[] classIds,
        int nodeCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_cache_extract_node_snapshot")]
    private static extern int cobra_cache_extract_node_snapshot(
        int[] headCodes,
        int[] arities,
        int[] classIds,
        int nodeCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_extract_nodes_fully_cached")]
    private static extern int cobra_score_extract_nodes_fully_cached(
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_extract_equality_unions")]
    private static extern int cobra_extract_equality_unions(
        int[] headCodes,
        int[] childStarts,
        int[] childCounts,
        int[] childIds,
        int nodeCount,
        int[] validFlags,
        int[] leftIds,
        int[] rightIds);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_hash_repair_targets")]
    private static extern int cobra_hash_repair_targets(
        int[] headHashes,
        int[] childStarts,
        int[] childCounts,
        int[] canonicalChildIds,
        int candidateCount,
        int[] targetHashes);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_cache_class_metrics")]
    private static extern int cobra_cache_class_metrics(
        int[] nodeCounts,
        int[] generations,
        int classCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_apply_class_metric_updates_cached")]
    private static extern int cobra_apply_class_metric_updates_cached(
        int[] classIds,
        int[] nodeCounts,
        int[] generations,
        int updateCount);


    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_repair_candidates")]
    private static extern int cobra_score_repair_candidates(
        int[] classIds,
        int[] childCounts,
        int[] generations,
        int[] nodeCounts,
        int candidateCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_node_rule_candidates")]
    private static extern int cobra_score_node_rule_candidates(
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] nodeChildStarts,
        int[] nodeChildIds,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks,
        int nodeCount,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int[] directWildcardFlags,
        int[] ruleArgStarts,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks,
        int ruleCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_cache_node_rule_snapshot")]
    private static extern int cobra_cache_node_rule_snapshot(
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] nodeChildStarts,
        int[] nodeChildIds,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks,
        int nodeCount,
        int childIdCount,
        int classCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_cache_rule_signature")]
    private static extern int cobra_cache_rule_signature(
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int[] directWildcardFlags,
        int[] ruleArgStarts,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks,
        int ruleArgCount,
        int ruleCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_node_rule_candidates_cached")]
    private static extern int cobra_score_node_rule_candidates_cached(
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int[] directWildcardFlags,
        int[] ruleArgStarts,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks,
        int ruleArgCount,
        int ruleCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_node_rule_candidates_fully_cached")]
    private static extern int cobra_score_node_rule_candidates_fully_cached(
        int ruleArgCount,
        int ruleCount,
        int[] scores);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_extract_node_rule_pairs_fully_cached")]
    private static extern int cobra_extract_node_rule_pairs_fully_cached(
        int ruleArgCount,
        int ruleCount,
        int maxPairs,
        int[] pairNodeIndices,
        int[] pairRuleIndices,
        out int pairCount);

    private static readonly object Sync = new();
    private static readonly object KernelTelemetrySync = new();
    private static bool _loadAttempted;
    private static IntPtr _libraryHandle;
    private static int _cachedParentHash;
    private static int _cachedParentLength;
    private static int[]? _cachedParentSnapshot;
    private static int _cachedRepairHash;
    private static int _cachedRepairParentLength;
    private static int _cachedRepairNodeLength;
    private static int _cachedClassMetricHash;
    private static int _cachedClassMetricLength;
    private static int[]? _cachedClassNodeCountsSnapshot;
    private static int[]? _cachedClassGenerationsSnapshot;
    private static int _cachedNodeRuleHash;
    private static int _cachedNodeRuleNodeCount;
    private static int _cachedNodeRuleChildIdCount;
    private static int _cachedNodeRuleClassCount;
    private static int _cachedRegionMaskHash;
    private static int _cachedRegionMaskLength;
    private static int _cachedRuleSignatureHash;
    private static int _cachedRuleSignatureRuleCount;
    private static int _cachedRuleSignatureArgCount;
    private static int _cachedExtractNodeHash;
    private static int _cachedExtractNodeCount;
    private static readonly Dictionary<string, KernelTelemetryAggregate> _kernelTelemetry = new(StringComparer.Ordinal);

    private sealed class KernelTelemetryAggregate
    {
        public int CallCount;
        public long ElapsedTicks;
    }

    private static int InvokeKernel(string kernelName, Func<int> invoke)
    {
        var stopwatch = Stopwatch.StartNew();
        int status = invoke();
        stopwatch.Stop();
        if (status == 0)
        {
            RecordKernelTelemetry(kernelName, stopwatch.ElapsedTicks);
        }

        return status;
    }

    private static void RecordKernelTelemetry(string kernelName, long elapsedTicks)
    {
        lock (KernelTelemetrySync)
        {
            if (!_kernelTelemetry.TryGetValue(kernelName, out var aggregate))
            {
                aggregate = new KernelTelemetryAggregate();
                _kernelTelemetry[kernelName] = aggregate;
            }

            aggregate.CallCount++;
            aggregate.ElapsedTicks += elapsedTicks;
        }
    }

    internal static ImmutableArray<CobraKernelTelemetrySnapshot> GetKernelTelemetrySnapshot()
    {
        lock (KernelTelemetrySync)
        {
            return _kernelTelemetry
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => new CobraKernelTelemetrySnapshot(
                    entry.Key,
                    entry.Value.CallCount,
                    TimeSpan.FromTicks(entry.Value.ElapsedTicks)))
                .ToImmutableArray();
        }
    }

    internal static void RecordKernelTelemetryForTesting(string kernelName, TimeSpan elapsed)
    {
        RecordKernelTelemetry(kernelName, elapsed.Ticks);
    }

    internal static void ResetKernelTelemetryForTesting()
    {
        lock (KernelTelemetrySync)
        {
            _kernelTelemetry.Clear();
        }
    }

    public static bool TryLoad()
    {
        lock (Sync)
        {
            if (_loadAttempted)
            {
                return _libraryHandle != IntPtr.Zero;
            }

            _loadAttempted = true;
            foreach (var path in GetCandidatePaths())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                if (NativeLibrary.TryLoad(path, out _libraryHandle))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static int GetDeviceCount()
    {
        if (!TryLoad())
        {
            return 0;
        }

        try
        {
            return cobra_device_count();
        }
        catch
        {
            return 0;
        }
    }

    public static bool TryBatchMseScores(double[] predictions, double[] targets, int samples, int candidates, out double[] results)
    {
        results = Array.Empty<double>();
        if (!TryLoad())
        {
            return false;
        }

        var gpuResults = new double[candidates];
        int status;
        try
        {
            status = cobra_batch_mse_scores(predictions, targets, samples, candidates, gpuResults);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        results = gpuResults;
        return true;
    }

    public static bool TryScoreRegions(
        int[] familyCodes,
        int[] nodeCounts,
        int[] boundaryCounts,
        out double[] benefitScores,
        out double[] conflictScores)
    {
        benefitScores = Array.Empty<double>();
        conflictScores = Array.Empty<double>();
        if (!TryLoad())
        {
            return false;
        }

        int regionCount = familyCodes.Length;
        if (nodeCounts.Length != regionCount || boundaryCounts.Length != regionCount || regionCount == 0)
        {
            return false;
        }

        var benefit = new double[regionCount];
        var conflict = new double[regionCount];
        int status;
        try
        {
            status = InvokeKernel("score_regions", () => cobra_score_regions(familyCodes, nodeCounts, boundaryCounts, regionCount, benefit, conflict));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        benefitScores = benefit;
        conflictScores = conflict;
        return true;
    }

    public static bool TryScoreMatches(
        int[] hotFlags,
        int[] boundaryFlags,
        int[] suppressedFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int matchCount = hotFlags.Length;
        if (boundaryFlags.Length != matchCount || suppressedFlags.Length != matchCount || matchCount == 0)
        {
            return false;
        }

        var gpuScores = new int[matchCount];
        int status;
        try
        {
            status = cobra_score_matches(hotFlags, boundaryFlags, suppressedFlags, matchCount, gpuScores);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = gpuScores;
        return true;
    }

    public static bool TryScoreClasses(
        int[] nodeCounts,
        int[] generations,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = nodeCounts.Length;
        if (generations.Length != classCount || hotFlags.Length != classCount ||
            boundaryFlags.Length != classCount || residualFlags.Length != classCount || classCount == 0)
        {
            return false;
        }

        var gpuScores = new int[classCount];
        int status;
        try
        {
            status = cobra_score_classes(nodeCounts, generations, hotFlags, boundaryFlags, residualFlags, classCount, gpuScores);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = gpuScores;
        return true;
    }

    public static bool TryPrepareUnions(
        int[] leftIds,
        int[] rightIds,
        out int[] normalizedLeft,
        out int[] normalizedRight,
        out ulong[] pairKeys)
    {
        normalizedLeft = Array.Empty<int>();
        normalizedRight = Array.Empty<int>();
        pairKeys = Array.Empty<ulong>();
        if (!TryLoad())
        {
            return false;
        }

        int pairCount = leftIds.Length;
        if (rightIds.Length != pairCount || pairCount == 0)
        {
            return false;
        }

        var left = new int[pairCount];
        var right = new int[pairCount];
        var keys = new ulong[pairCount];
        int status;
        try
        {
            status = cobra_prepare_unions(leftIds, rightIds, pairCount, left, right, keys);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        normalizedLeft = left;
        normalizedRight = right;
        pairKeys = keys;
        return true;
    }

    public static bool TryScoreRuleCompatibility(
        int[] classMasks,
        int[] ruleMasks,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = classMasks.Length;
        int ruleCount = ruleMasks.Length;
        if (classCount == 0 || ruleCount == 0)
        {
            return false;
        }

        var compatibility = new int[classCount * ruleCount];
        int status;
        try
        {
            status = InvokeKernel("score_rule_compatibility", () => cobra_score_rule_compatibility(classMasks, ruleMasks, classCount, ruleCount, compatibility));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = compatibility;
        return true;
    }

    public static bool TryScoreRebuildClasses(
        int[] nodeCounts,
        int[] generations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = nodeCounts.Length;
        if (classCount == 0 || generations.Length != classCount)
        {
            return false;
        }

        var rebuildScores = new int[classCount];
        int status;
        try
        {
            status = cobra_score_rebuild_classes(nodeCounts, generations, classCount, rebuildScores);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = rebuildScores;
        return true;
    }

    public static bool TryScoreAnalysisClasses(
        int[] nodeCounts,
        int[] generations,
        int[] unresolvedFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = nodeCounts.Length;
        if (classCount == 0 || generations.Length != classCount || unresolvedFlags.Length != classCount)
        {
            return false;
        }

        var analysisScores = new int[classCount];
        int status;
        try
        {
            status = cobra_score_analysis_classes(nodeCounts, generations, unresolvedFlags, classCount, analysisScores);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = analysisScores;
        return true;
    }

    public static bool TryResolveUnionRoots(
        int[] parents,
        int[] leftIds,
        int[] rightIds,
        out int[] resolvedLeft,
        out int[] resolvedRight,
        out ulong[] pairKeys)
    {
        resolvedLeft = Array.Empty<int>();
        resolvedRight = Array.Empty<int>();
        pairKeys = Array.Empty<ulong>();
        if (!TryLoad())
        {
            return false;
        }

        int parentCount = parents.Length;
        int pairCount = leftIds.Length;
        if (parentCount == 0 || pairCount == 0 || rightIds.Length != pairCount)
        {
            return false;
        }

        var left = new int[pairCount];
        var right = new int[pairCount];
        var keys = new ulong[pairCount];
        int status;
        try
        {
            status = EnsureParentSnapshotCached(parents)
                ? InvokeKernel("resolve_union_roots_cached", () => cobra_resolve_union_roots_cached(leftIds, rightIds, pairCount, left, right, keys))
                : InvokeKernel("resolve_union_roots", () => cobra_resolve_union_roots(parents, parentCount, leftIds, rightIds, pairCount, left, right, keys));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        resolvedLeft = left;
        resolvedRight = right;
        pairKeys = keys;
        return true;
    }

    internal static bool TryResolveUnionRootsFromCache(
        int[] leftIds,
        int[] rightIds,
        out int[] resolvedLeft,
        out int[] resolvedRight,
        out ulong[] pairKeys)
    {
        resolvedLeft = Array.Empty<int>();
        resolvedRight = Array.Empty<int>();
        pairKeys = Array.Empty<ulong>();
        if (!TryLoad() || _cachedParentSnapshot == null)
        {
            return false;
        }

        int pairCount = leftIds.Length;
        if (pairCount == 0 || rightIds.Length != pairCount)
        {
            return false;
        }

        var left = new int[pairCount];
        var right = new int[pairCount];
        var keys = new ulong[pairCount];
        int status;
        try
        {
            status = cobra_resolve_union_roots_cached(leftIds, rightIds, pairCount, left, right, keys);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        resolvedLeft = left;
        resolvedRight = right;
        pairKeys = keys;
        return true;
    }

    public static bool TryMarkRepairCandidates(
        int[] parents,
        int[] childStarts,
        int[] childCounts,
        int[] childIds,
        out int[] dirtyFlags)
    {
        dirtyFlags = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int parentCount = parents.Length;
        int nodeCount = childStarts.Length;
        if (parentCount == 0 || nodeCount == 0 || childCounts.Length != nodeCount)
        {
            return false;
        }

        var dirty = new int[nodeCount];
        int status;
        try
        {
            status = EnsureRepairSnapshotCached(parents, childStarts, childCounts, childIds)
                ? cobra_mark_repair_candidates_cached(dirty)
                : cobra_mark_repair_candidates(parents, parentCount, childStarts, childCounts, childIds, nodeCount, dirty);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        dirtyFlags = dirty;
        return true;
    }

    private static bool EnsureParentSnapshotCached(int[] parents)
    {
        int hash = ComputeHash(parents);
        if (_cachedParentLength == parents.Length && _cachedParentHash == hash)
        {
            return true;
        }

        int status = InvokeKernel("cache_parent_snapshot", () => cobra_cache_parent_snapshot(parents, parents.Length));
        if (status != 0)
        {
            return false;
        }

        _cachedParentLength = parents.Length;
        _cachedParentHash = hash;
        _cachedParentSnapshot = (int[])parents.Clone();
        return true;
    }

    public static bool TryApplyParentUpdates(int[] classIds, int[] parentIds)
    {
        if (!TryLoad() || _cachedParentSnapshot == null)
        {
            return false;
        }

        int updateCount = classIds.Length;
        if (updateCount == 0 || parentIds.Length != updateCount)
        {
            return false;
        }

        int status;
        try
        {
            status = InvokeKernel("apply_parent_updates_cached", () => cobra_apply_parent_updates_cached(classIds, parentIds, updateCount));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        for (int i = 0; i < updateCount; i++)
        {
            int classId = classIds[i];
            if ((uint)classId >= (uint)_cachedParentSnapshot.Length)
            {
                return false;
            }

            _cachedParentSnapshot[classId] = parentIds[i];
        }

        _cachedParentHash = ComputeHash(_cachedParentSnapshot);
        _cachedParentLength = _cachedParentSnapshot.Length;
        return true;
    }

    private static bool EnsureRepairSnapshotCached(int[] parents, int[] childStarts, int[] childCounts, int[] childIds)
    {
        int hash = ComputeHash(parents);
        hash = (hash * 397) ^ ComputeHash(childStarts);
        hash = (hash * 397) ^ ComputeHash(childCounts);
        hash = (hash * 397) ^ ComputeHash(childIds);

        if (_cachedRepairParentLength == parents.Length &&
            _cachedRepairNodeLength == childStarts.Length &&
            _cachedRepairHash == hash)
        {
            return true;
        }

        int status = InvokeKernel("cache_repair_snapshot", () => cobra_cache_repair_snapshot(parents, parents.Length, childStarts, childCounts, childIds, childStarts.Length));
        if (status != 0)
        {
            return false;
        }

        _cachedRepairParentLength = parents.Length;
        _cachedRepairNodeLength = childStarts.Length;
        _cachedRepairHash = hash;
        return true;
    }

    private static bool EnsureClassMetricsCached(int[] nodeCounts, int[] generations)
    {
        int hash = ComputeHash(nodeCounts);
        hash = (hash * 397) ^ ComputeHash(generations);
        if (_cachedClassMetricLength == nodeCounts.Length && _cachedClassMetricHash == hash)
        {
            return true;
        }

        int status = InvokeKernel("cache_class_metrics", () => cobra_cache_class_metrics(nodeCounts, generations, nodeCounts.Length));
        if (status != 0)
        {
            return false;
        }

        _cachedClassMetricLength = nodeCounts.Length;
        _cachedClassMetricHash = hash;
        _cachedClassNodeCountsSnapshot = (int[])nodeCounts.Clone();
        _cachedClassGenerationsSnapshot = (int[])generations.Clone();
        return true;
    }

    private static bool CoversClassIds(int[] classIds, int metricLength)
    {
        for (int i = 0; i < classIds.Length; i++)
        {
            int classId = classIds[i];
            if (classId < 0 || classId >= metricLength)
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryApplyClassMetricUpdates(int[] classIds, int[] nodeCounts, int[] generations)
    {
        if (!TryLoad() || _cachedClassNodeCountsSnapshot == null || _cachedClassGenerationsSnapshot == null)
        {
            return false;
        }

        int updateCount = classIds.Length;
        if (updateCount == 0 || nodeCounts.Length != updateCount || generations.Length != updateCount)
        {
            return false;
        }

        int status;
        try
        {
            status = cobra_apply_class_metric_updates_cached(classIds, nodeCounts, generations, updateCount);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        for (int i = 0; i < updateCount; i++)
        {
            int classId = classIds[i];
            if ((uint)classId >= (uint)_cachedClassNodeCountsSnapshot.Length ||
                (uint)classId >= (uint)_cachedClassGenerationsSnapshot.Length)
            {
                return false;
            }

            _cachedClassNodeCountsSnapshot[classId] = nodeCounts[i];
            _cachedClassGenerationsSnapshot[classId] = generations[i];
        }

        int hash = ComputeHash(_cachedClassNodeCountsSnapshot);
        hash = (hash * 397) ^ ComputeHash(_cachedClassGenerationsSnapshot);
        _cachedClassMetricHash = hash;
        _cachedClassMetricLength = _cachedClassNodeCountsSnapshot.Length;
        return true;
    }

    private static bool EnsureNodeRuleSnapshotCached(
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] nodeChildStarts,
        int[] nodeChildIds,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks)
    {
        int hash = ComputeHash(nodeHeadCodes);
        hash = (hash * 397) ^ ComputeHash(nodeArities);
        hash = (hash * 397) ^ ComputeHash(nodeChildStarts);
        hash = (hash * 397) ^ ComputeHash(nodeChildIds);
        hash = (hash * 397) ^ ComputeHash(classConstraintMasks);
        hash = (hash * 397) ^ ComputeHash(classHeadBucketMasks);
        hash = (hash * 397) ^ ComputeHash(classExactHeadMasks);
        hash = (hash * 397) ^ ComputeHash(classChildEqualityMasks);
        hash = (hash * 397) ^ ComputeHash(classChildAtomBucketMasks);
        hash = (hash * 397) ^ ComputeHash(classChildConstraintMasks);
        hash = (hash * 397) ^ ComputeHash(classChildReferenceBloomMasks);

        int nodeCount = nodeHeadCodes.Length;
        int childIdCount = nodeChildIds.Length;
        int classCount = classConstraintMasks.Length;

        if (_cachedNodeRuleNodeCount == nodeCount &&
            _cachedNodeRuleChildIdCount == childIdCount &&
            _cachedNodeRuleClassCount == classCount &&
            _cachedNodeRuleHash == hash)
        {
            return true;
        }

        int status = InvokeKernel(
            "cache_node_rule_snapshot",
            () => cobra_cache_node_rule_snapshot(
                nodeHeadCodes,
                nodeArities,
                nodeChildStarts,
                nodeChildIds,
                classConstraintMasks,
                classHeadBucketMasks,
                classExactHeadMasks,
                classChildEqualityMasks,
                classChildAtomBucketMasks,
                classChildConstraintMasks,
                classChildReferenceBloomMasks,
                nodeCount,
                childIdCount,
                classCount));
        if (status != 0)
        {
            return false;
        }

        _cachedNodeRuleNodeCount = nodeCount;
        _cachedNodeRuleChildIdCount = childIdCount;
        _cachedNodeRuleClassCount = classCount;
        _cachedNodeRuleHash = hash;
        return true;
    }

    private static bool EnsureRuleSignatureCached(
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int[] directWildcardFlags,
        int[] ruleArgStarts,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks)
    {
        int hash = ComputeHash(ruleHeadCodes);
        hash = (hash * 397) ^ ComputeHash(ruleArities);
        hash = (hash * 397) ^ ComputeHash(wildcardFlags);
        hash = (hash * 397) ^ ComputeHash(directWildcardFlags);
        hash = (hash * 397) ^ ComputeHash(ruleArgStarts);
        hash = (hash * 397) ^ ComputeHash(ruleArgGroupIds);
        hash = (hash * 397) ^ ComputeHash(ruleArgConstraintMasks);
        hash = (hash * 397) ^ ComputeHash(ruleArgKinds);
        hash = (hash * 397) ^ ComputeHash(ruleArgHeadBuckets);
        hash = (hash * 397) ^ ComputeHash(ruleArgExactHeadMasks);
        hash = (hash * 397) ^ ComputeHash(ruleArgNestedRepeatMasks);
        hash = (hash * 397) ^ ComputeHash(ruleArgNestedAtomBucketMasks);
        hash = (hash * 397) ^ ComputeHash(ruleArgNestedConstraintMasks);
        hash = (hash * 397) ^ ComputeHash(ruleArgNestedTopLevelReferenceMasks);

        int ruleCount = ruleHeadCodes.Length;
        int ruleArgCount = ruleArgGroupIds.Length;

        if (_cachedRuleSignatureRuleCount == ruleCount &&
            _cachedRuleSignatureArgCount == ruleArgCount &&
            _cachedRuleSignatureHash == hash)
        {
            return true;
        }

        int status = InvokeKernel(
            "cache_rule_signature",
            () => cobra_cache_rule_signature(
                ruleHeadCodes,
                ruleArities,
                wildcardFlags,
                directWildcardFlags,
                ruleArgStarts,
                ruleArgGroupIds,
                ruleArgConstraintMasks,
                ruleArgKinds,
                ruleArgHeadBuckets,
                ruleArgExactHeadMasks,
                ruleArgNestedRepeatMasks,
                ruleArgNestedAtomBucketMasks,
                ruleArgNestedConstraintMasks,
                ruleArgNestedTopLevelReferenceMasks,
                ruleArgCount,
                ruleCount));
        if (status != 0)
        {
            return false;
        }

        _cachedRuleSignatureRuleCount = ruleCount;
        _cachedRuleSignatureArgCount = ruleArgCount;
        _cachedRuleSignatureHash = hash;
        return true;
    }

    private static bool EnsureExtractNodeSnapshotCached(
        int[] headCodes,
        int[] arities,
        int[] classIds)
    {
        int hash = ComputeHash(headCodes);
        hash = (hash * 397) ^ ComputeHash(arities);
        hash = (hash * 397) ^ ComputeHash(classIds);
        if (_cachedExtractNodeCount == headCodes.Length &&
            _cachedExtractNodeHash == hash)
        {
            return true;
        }

        int status = cobra_cache_extract_node_snapshot(headCodes, arities, classIds, headCodes.Length);
        if (status != 0)
        {
            return false;
        }

        _cachedExtractNodeCount = headCodes.Length;
        _cachedExtractNodeHash = hash;
        return true;
    }

    public static bool TryWarmGraphCaches(SymCobra.Core.CobraGraphState graph)
    {
        if (!TryLoad())
        {
            return false;
        }

        try
        {
            int[] parents = graph.GetParentSnapshot();
            int classCount = graph.ClassCount;
            int[] nodeCounts = new int[classCount];
            int[] generations = new int[classCount];
            int nodeCount = graph.NodeCount;
            int[] headCodes = new int[nodeCount];
            int[] arities = new int[nodeCount];
            int[] classIds = new int[nodeCount];
            int[] nodeChildStarts = new int[nodeCount];
            var nodeChildIds = new int[graph.Nodes.Sum(static node => node.CanonicalChildIds.Length)];
            int childOffset = 0;

            for (int classId = 0; classId < classCount; classId++)
            {
                var cls = graph.Classes[classId];
                nodeCounts[classId] = cls.NodeCount;
                generations[classId] = cls.Generation;
            }
            
            for (int nodeId = 0; nodeId < nodeCount; nodeId++)
            {
                var node = graph.Nodes[nodeId];
                headCodes[nodeId] = node.HeadCode;
                arities[nodeId] = node.Arity;
                classIds[nodeId] = graph.Find(node.ClassId);
                nodeChildStarts[nodeId] = childOffset;
                for (int childIndex = 0; childIndex < node.CanonicalChildIds.Length; childIndex++)
                {
                    nodeChildIds[childOffset++] = graph.Find(node.CanonicalChildIds[childIndex]);
                }
            }

            var repairSnapshot = graph.GetRepairSnapshot();

            bool parentOk = parents.Length == 0 || EnsureParentSnapshotCached(parents);
            bool classOk = nodeCounts.Length == 0 || EnsureClassMetricsCached(nodeCounts, generations);
            try
            {
                var snapshot = CobraPlannerSnapshot.Create(graph);
                _ = headCodes.Length == 0 || EnsureNodeRuleSnapshotCached(
                    headCodes,
                    arities,
                    nodeChildStarts,
                    nodeChildIds,
                    snapshot.ClassConstraintMasks,
                    snapshot.ClassHeadBucketMasks,
                    snapshot.ClassExactHeadMasks,
                    snapshot.ClassChildEqualityMasks,
                    snapshot.ClassChildAtomBucketMasks,
                    snapshot.ClassChildConstraintMasks,
                    snapshot.ClassChildReferenceBloomMasks);
            }
            catch
            {
            }
            bool extractOk = headCodes.Length == 0 || EnsureExtractNodeSnapshotCached(headCodes, arities, classIds);
            bool repairOk = repairSnapshot.Candidates.Count == 0 ||
                            EnsureRepairSnapshotCached(
                                repairSnapshot.Parents,
                                repairSnapshot.ChildStarts,
                                repairSnapshot.ChildCounts,
                                repairSnapshot.ChildIds);
            
            return parentOk && classOk && extractOk && repairOk;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryWarmGraphCaches(Sym.Core.EGraph.EGraph graph)
    {
        if (!TryLoad())
        {
            return false;
        }

        try
        {
            int[] parents = graph.GetParentSnapshot();
            int[] nodeCounts = Enumerable.Range(0, graph.ClassCount)
                .Select(id => graph.GetClass(id).Nodes.Count)
                .ToArray();
            int[] generations = Enumerable.Range(0, graph.ClassCount)
                .Select(id => graph.GetClass(id).Generation)
                .ToArray();
            var repairSnapshot = graph.GetRepairSnapshot();

            bool parentOk = parents.Length == 0 || EnsureParentSnapshotCached(parents);
            bool classOk = nodeCounts.Length == 0 || EnsureClassMetricsCached(nodeCounts, generations);
            try
            {
                int nodeCount = graph.NodeCount;
                int[] headCodes = new int[nodeCount];
                int[] arities = new int[nodeCount];
                int[] nodeChildStarts = new int[nodeCount];
                var nodeChildIds = new System.Collections.Generic.List<int>();
                int nodeIndex = 0;
                for (int classId = 0; classId < graph.ClassCount; classId++)
                {
                    foreach (var node in graph.GetClass(classId).Nodes)
                    {
                        headCodes[nodeIndex] = CobraNodeMatchEncoding.EncodeHeadCode(node.Head);
                        arities[nodeIndex] = node.Children.Count;
                        nodeChildStarts[nodeIndex] = nodeChildIds.Count;
                        foreach (int childId in node.Children)
                        {
                            nodeChildIds.Add(graph.Find(childId));
                        }

                        nodeIndex++;
                    }
                }

                var snapshot = CobraPlannerSnapshot.Create(graph);
                _ = headCodes.Length == 0 || EnsureNodeRuleSnapshotCached(
                    headCodes,
                    arities,
                    nodeChildStarts,
                    nodeChildIds.ToArray(),
                    snapshot.ClassConstraintMasks,
                    snapshot.ClassHeadBucketMasks,
                    snapshot.ClassExactHeadMasks,
                    snapshot.ClassChildEqualityMasks,
                    snapshot.ClassChildAtomBucketMasks,
                    snapshot.ClassChildConstraintMasks,
                    snapshot.ClassChildReferenceBloomMasks);
            }
            catch
            {
            }
            bool repairOk = repairSnapshot.Candidates.Count == 0 ||
                            EnsureRepairSnapshotCached(
                                repairSnapshot.Parents,
                                repairSnapshot.ChildStarts,
                                repairSnapshot.ChildCounts,
                                repairSnapshot.ChildIds);

            return parentOk && classOk && repairOk;
        }
        catch
        {
            return false;
        }
    }

    private static int ComputeHash(int[] values)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < values.Length; i++)
            {
                hash = (hash * 31) + values[i];
            }

            return hash;
        }
    }

    public static bool TryGroupUnions(
        int[] leftIds,
        int[] rightIds,
        out int[] groupKeys)
    {
        groupKeys = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int pairCount = leftIds.Length;
        if (pairCount == 0 || rightIds.Length != pairCount)
        {
            return false;
        }

        var groups = new int[pairCount];
        int status;
        try
        {
            status = cobra_group_unions(leftIds, rightIds, pairCount, groups);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        groupKeys = groups;
        return true;
    }

    public static bool TryScoreUnionMembers(
        int[] memberIds,
        int[] nodeCounts,
        int[] generations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int memberCount = memberIds.Length;
        if (memberCount == 0 || nodeCounts.Length == 0 || generations.Length != nodeCounts.Length)
        {
            return false;
        }

        var result = new int[memberCount];
        int status;
        try
        {
            status = EnsureClassMetricsCached(nodeCounts, generations)
                ? cobra_score_union_members_cached(memberIds, memberCount, result)
                : cobra_score_union_members(memberIds, memberCount, nodeCounts, generations, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreDirectPairs(
        int[] classIds,
        int[] nodeArities,
        int[] generations,
        int[] nodeCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int pairCount = classIds.Length;
        if (pairCount == 0 || nodeArities.Length != pairCount || generations.Length != pairCount || nodeCounts.Length != pairCount)
        {
            return false;
        }

        var result = new int[pairCount];
        int status;
        try
        {
            status = cobra_score_direct_pairs(classIds, nodeArities, generations, nodeCounts, pairCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreDirectClasses(
        int[] pairCounts,
        int[] generations,
        int[] nodeCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = pairCounts.Length;
        if (classCount == 0 || generations.Length != classCount || nodeCounts.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            status = cobra_score_direct_classes(pairCounts, generations, nodeCounts, classCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreRegionSelection(
        int[] benefitScores,
        int[] conflictScores,
        int[] residualFlags,
        int[] transposeFlags,
        int[] boundaryCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int regionCount = benefitScores.Length;
        if (regionCount == 0 || conflictScores.Length != regionCount || residualFlags.Length != regionCount ||
            transposeFlags.Length != regionCount || boundaryCounts.Length != regionCount)
        {
            return false;
        }

        var result = new int[regionCount];
        int status;
        try
        {
            status = cobra_score_region_selection(benefitScores, conflictScores, residualFlags, transposeFlags, boundaryCounts, regionCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreRuleOrder(
        int[] compatibilityCounts,
        int[] arities,
        int[] wildcardFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int ruleCount = compatibilityCounts.Length;
        if (ruleCount == 0 || arities.Length != ruleCount || wildcardFlags.Length != ruleCount)
        {
            return false;
        }

        var result = new int[ruleCount];
        int status;
        try
        {
            status = cobra_score_rule_order(compatibilityCounts, arities, wildcardFlags, ruleCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreCandidateRules(
        int[] allowedCounts,
        int[] arities,
        int[] directFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int ruleCount = allowedCounts.Length;
        if (ruleCount == 0 || arities.Length != ruleCount || directFlags.Length != ruleCount)
        {
            return false;
        }

        var result = new int[ruleCount];
        int status;
        try
        {
            status = cobra_score_candidate_rules(allowedCounts, arities, directFlags, ruleCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreDirectRules(
        int[] pairCounts,
        int[] arities,
        int[] nestedFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int ruleCount = pairCounts.Length;
        if (ruleCount == 0 || arities.Length != ruleCount || nestedFlags.Length != ruleCount)
        {
            return false;
        }

        var result = new int[ruleCount];
        int status;
        try
        {
            status = cobra_score_direct_rules(pairCounts, arities, nestedFlags, ruleCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScorePreparedUnions(
        int[] leftGenerations,
        int[] rightGenerations,
        int[] leftNodeCounts,
        int[] rightNodeCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int pairCount = leftGenerations.Length;
        if (pairCount == 0 || rightGenerations.Length != pairCount || leftNodeCounts.Length != pairCount || rightNodeCounts.Length != pairCount)
        {
            return false;
        }

        var result = new int[pairCount];
        int status;
        try
        {
            status = cobra_score_prepared_unions(leftGenerations, rightGenerations, leftNodeCounts, rightNodeCounts, pairCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScorePreparedUnionsByClassId(
        int[] leftIds,
        int[] rightIds,
        int[] nodeCounts,
        int[] generations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int pairCount = leftIds.Length;
        if (pairCount == 0 || rightIds.Length != pairCount || nodeCounts.Length == 0 || generations.Length != nodeCounts.Length)
        {
            return false;
        }

        var result = new int[pairCount];
        int status;
        try
        {
            if (EnsureClassMetricsCached(nodeCounts, generations))
            {
                status = cobra_score_prepared_unions_cached(leftIds, rightIds, pairCount, result);
            }
            else
            {
                var leftGenerations = leftIds.Select(id => generations[id]).ToArray();
                var rightGenerations = rightIds.Select(id => generations[id]).ToArray();
                var leftNodeCounts = leftIds.Select(id => nodeCounts[id]).ToArray();
                var rightNodeCounts = rightIds.Select(id => nodeCounts[id]).ToArray();
                status = cobra_score_prepared_unions(leftGenerations, rightGenerations, leftNodeCounts, rightNodeCounts, pairCount, result);
            }
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreRebuildWithRepair(
        int[] nodeCounts,
        int[] generations,
        int[] repairCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = nodeCounts.Length;
        if (classCount == 0 || generations.Length != classCount || repairCounts.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            status = EnsureClassMetricsCached(nodeCounts, generations)
                ? cobra_score_rebuild_with_repair_cached(repairCounts, classCount, result)
                : cobra_score_rebuild_with_repair(nodeCounts, generations, repairCounts, classCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreRebuildWithRepairById(
        int[] classIds,
        int[] nodeCounts,
        int[] generations,
        int[] repairCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = classIds.Length;
        if (classCount == 0 ||
            repairCounts.Length != classCount ||
            nodeCounts.Length != generations.Length ||
            nodeCounts.Length == 0)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            bool hasCachedMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            status = hasCachedMetrics
                ? InvokeKernel("score_rebuild_with_repair_by_id_cached", () => cobra_score_rebuild_with_repair_by_id_cached(classIds, repairCounts, classCount, result))
                : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreAnalysisWithRepair(
        int[] nodeCounts,
        int[] generations,
        int[] unresolvedFlags,
        int[] repairCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = nodeCounts.Length;
        if (classCount == 0 || generations.Length != classCount || unresolvedFlags.Length != classCount || repairCounts.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            status = EnsureClassMetricsCached(nodeCounts, generations)
                ? InvokeKernel("score_analysis_with_repair_cached", () => cobra_score_analysis_with_repair_cached(unresolvedFlags, repairCounts, classCount, result))
                : InvokeKernel("score_analysis_with_repair", () => cobra_score_analysis_with_repair(nodeCounts, generations, unresolvedFlags, repairCounts, classCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreAnalysisWithRepairById(
        int[] classIds,
        int[] nodeCounts,
        int[] generations,
        int[] unresolvedFlags,
        int[] repairCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = classIds.Length;
        if (classCount == 0 ||
            unresolvedFlags.Length != classCount ||
            repairCounts.Length != classCount ||
            nodeCounts.Length != generations.Length ||
            nodeCounts.Length == 0)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            bool hasCachedMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            status = hasCachedMetrics
                ? InvokeKernel("score_analysis_with_repair_by_id_cached", () => cobra_score_analysis_with_repair_by_id_cached(classIds, unresolvedFlags, repairCounts, classCount, result))
                : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreMatchPriorityV2(
        int[] hotFlags,
        int[] boundaryFlags,
        int[] suppressedFlags,
        int[] ruleArities,
        int[] directFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int matchCount = hotFlags.Length;
        if (matchCount == 0 || boundaryFlags.Length != matchCount || suppressedFlags.Length != matchCount ||
            ruleArities.Length != matchCount || directFlags.Length != matchCount)
        {
            return false;
        }

        var result = new int[matchCount];
        int status;
        try
        {
            status = cobra_score_match_priority_v2(hotFlags, boundaryFlags, suppressedFlags, ruleArities, directFlags, matchCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreFrontierV3(
        int[] nodeCounts,
        int[] generations,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int[] suppressedFlags,
        int[] hotRegionCounts,
        int[] boundaryRegionCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = nodeCounts.Length;
        if (classCount == 0 || generations.Length != classCount || hotFlags.Length != classCount || boundaryFlags.Length != classCount ||
            residualFlags.Length != classCount || suppressedFlags.Length != classCount || hotRegionCounts.Length != classCount || boundaryRegionCounts.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            status = InvokeKernel("score_frontier_v3", () => cobra_score_frontier_v3(nodeCounts, generations, hotFlags, boundaryFlags, residualFlags, suppressedFlags, hotRegionCounts, boundaryRegionCounts, classCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreFrontierV3ById(
        int[] classIds,
        int[] nodeCounts,
        int[] generations,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int[] suppressedFlags,
        int[] hotRegionCounts,
        int[] boundaryRegionCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = classIds.Length;
        if (classCount == 0 || hotFlags.Length != classCount || boundaryFlags.Length != classCount ||
            residualFlags.Length != classCount || suppressedFlags.Length != classCount || hotRegionCounts.Length != classCount || boundaryRegionCounts.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            bool hasCachedMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            status = hasCachedMetrics
                ? InvokeKernel("score_frontier_v3_by_id_cached", () => cobra_score_frontier_v3_by_id_cached(
                    classIds,
                    hotFlags,
                    boundaryFlags,
                    residualFlags,
                    suppressedFlags,
                    hotRegionCounts,
                    boundaryRegionCounts,
                    classCount,
                    result))
                : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreFrontierV2(
        int[] nodeCounts,
        int[] generations,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int[] hotRegionCounts,
        int[] boundaryRegionCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = nodeCounts.Length;
        if (classCount == 0 || generations.Length != classCount || hotFlags.Length != classCount || boundaryFlags.Length != classCount ||
            residualFlags.Length != classCount || hotRegionCounts.Length != classCount || boundaryRegionCounts.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            status = InvokeKernel("score_frontier_v2", () => cobra_score_frontier_v2(nodeCounts, generations, hotFlags, boundaryFlags, residualFlags, hotRegionCounts, boundaryRegionCounts, classCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreFrontierV2ById(
        int[] classIds,
        int[] nodeCounts,
        int[] generations,
        int[] hotFlags,
        int[] boundaryFlags,
        int[] residualFlags,
        int[] hotRegionCounts,
        int[] boundaryRegionCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = classIds.Length;
        if (classCount == 0 || nodeCounts.Length != generations.Length || nodeCounts.Length == 0 ||
            hotFlags.Length != classCount || boundaryFlags.Length != classCount ||
            residualFlags.Length != classCount || hotRegionCounts.Length != classCount ||
            boundaryRegionCounts.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            bool hasCachedMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            status = hasCachedMetrics
                ? InvokeKernel("score_frontier_v2_by_id_cached", () => cobra_score_frontier_v2_by_id_cached(
                    classIds,
                    hotFlags,
                    boundaryFlags,
                    residualFlags,
                    hotRegionCounts,
                    boundaryRegionCounts,
                    classCount,
                    result))
                : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreDirectClassesV2(
        int[] pairCounts,
        int[] nestedPairCounts,
        int[] generations,
        int[] nodeCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = pairCounts.Length;
        if (classCount == 0 || nestedPairCounts.Length != classCount || generations.Length != classCount || nodeCounts.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            status = EnsureClassMetricsCached(nodeCounts, generations)
                ? InvokeKernel("score_direct_classes_v2_cached", () => cobra_score_direct_classes_v2_cached(pairCounts, nestedPairCounts, classCount, result))
                : InvokeKernel("score_direct_classes_v2", () => cobra_score_direct_classes_v2(pairCounts, nestedPairCounts, generations, nodeCounts, classCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreDirectClassesV2ById(
        int[] classIds,
        int[] pairCounts,
        int[] nestedPairCounts,
        int[] allNodeCounts,
        int[] allGenerations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = classIds.Length;
        if (classCount == 0 ||
            pairCounts.Length != classCount ||
            nestedPairCounts.Length != classCount ||
            allNodeCounts.Length != allGenerations.Length ||
            !CoversClassIds(classIds, allNodeCounts.Length))
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            status = EnsureClassMetricsCached(allNodeCounts, allGenerations)
                ? InvokeKernel("score_direct_classes_v2_by_id_cached", () => cobra_score_direct_classes_v2_by_id_cached(classIds, pairCounts, nestedPairCounts, classCount, result))
                : InvokeKernel("score_direct_classes_v2", () => cobra_score_direct_classes_v2(
                    pairCounts,
                    nestedPairCounts,
                    classIds.Select(id => allGenerations[id]).ToArray(),
                    classIds.Select(id => allNodeCounts[id]).ToArray(),
                    classCount,
                    result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreRepairCandidatesV2(
        int[] classIds,
        int[] childCounts,
        int[] generations,
        int[] nodeCounts,
        int[] boundaryFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int candidateCount = classIds.Length;
        if (candidateCount == 0 || childCounts.Length != candidateCount || generations.Length != candidateCount ||
            nodeCounts.Length != candidateCount || boundaryFlags.Length != candidateCount)
        {
            return false;
        }

        var result = new int[candidateCount];
        int status;
        try
        {
            status = InvokeKernel("score_repair_candidates_v2", () => cobra_score_repair_candidates_v2(classIds, childCounts, generations, nodeCounts, boundaryFlags, candidateCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreRepairApplicationGroups(
        int[] anchorIds,
        int[] memberCounts,
        int[] generations,
        int[] nodeCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int groupCount = anchorIds.Length;
        if (groupCount == 0 || memberCounts.Length != groupCount || generations.Length != groupCount || nodeCounts.Length != groupCount)
        {
            return false;
        }

        var result = new int[groupCount];
        int status;
        try
        {
            status = InvokeKernel("score_repair_application_groups", () => cobra_score_repair_application_groups(anchorIds, memberCounts, generations, nodeCounts, groupCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreRepairCandidatesV2ById(
        int[] classIds,
        int[] childCounts,
        int[] nodeCounts,
        int[] generations,
        int[] boundaryFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int candidateCount = classIds.Length;
        if (candidateCount == 0 || childCounts.Length != candidateCount || boundaryFlags.Length != candidateCount ||
            nodeCounts.Length != generations.Length || nodeCounts.Length == 0)
        {
            return false;
        }

        var result = new int[candidateCount];
        int status;
        try
        {
            bool hasCachedMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            status = hasCachedMetrics
                ? InvokeKernel("score_repair_candidates_v2_by_id_cached", () => cobra_score_repair_candidates_v2_by_id_cached(
                    classIds,
                    childCounts,
                    boundaryFlags,
                    candidateCount,
                    result))
                : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreRegionSelectionV2(
        int[] familyCodes,
        int[] benefitScores,
        int[] conflictScores,
        int[] residualFlags,
        int[] transposeFlags,
        int[] boundaryCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int regionCount = familyCodes.Length;
        if (regionCount == 0 || benefitScores.Length != regionCount || conflictScores.Length != regionCount ||
            residualFlags.Length != regionCount || transposeFlags.Length != regionCount || boundaryCounts.Length != regionCount)
        {
            return false;
        }

        var result = new int[regionCount];
        int status;
        try
        {
            status = InvokeKernel("score_region_selection_v2", () => cobra_score_region_selection_v2(familyCodes, benefitScores, conflictScores, residualFlags, transposeFlags, boundaryCounts, regionCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreDirectPairsV2(
        int[] classIds,
        int[] nodeArities,
        int[] generations,
        int[] nodeCounts,
        int[] nestedFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int pairCount = classIds.Length;
        if (pairCount == 0 || nodeArities.Length != pairCount || generations.Length != pairCount || nodeCounts.Length != pairCount || nestedFlags.Length != pairCount)
        {
            return false;
        }

        var result = new int[pairCount];
        int status;
        try
        {
            bool hasCachedMetrics = EnsureClassMetricsCached(nodeCounts, generations) && CoversClassIds(classIds, nodeCounts.Length);
            status = hasCachedMetrics
                ? InvokeKernel("score_direct_pairs_v2_cached", () => cobra_score_direct_pairs_v2_cached(classIds, nodeArities, nestedFlags, pairCount, result))
                : InvokeKernel("score_direct_pairs_v2", () => cobra_score_direct_pairs_v2(classIds, nodeArities, generations, nodeCounts, nestedFlags, pairCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreDirectPairsV2ById(
        int[] classIds,
        int[] nodeArities,
        int[] nestedFlags,
        int[] nodeCounts,
        int[] generations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int pairCount = classIds.Length;
        if (pairCount == 0 || nodeArities.Length != pairCount || nestedFlags.Length != pairCount ||
            nodeCounts.Length != generations.Length || nodeCounts.Length == 0)
        {
            return false;
        }

        var result = new int[pairCount];
        int status;
        try
        {
            bool hasCachedMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            status = hasCachedMetrics
                ? InvokeKernel("score_direct_pairs_v2_by_id_cached", () => cobra_score_direct_pairs_v2_by_id_cached(classIds, nodeArities, nestedFlags, pairCount, result))
                : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreMatchPriorityV3(
        int[] hotFlags,
        int[] boundaryFlags,
        int[] suppressedFlags,
        int[] ruleArities,
        int[] directFlags,
        int[] nestedFlags,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int matchCount = hotFlags.Length;
        if (matchCount == 0 || boundaryFlags.Length != matchCount || suppressedFlags.Length != matchCount ||
            ruleArities.Length != matchCount || directFlags.Length != matchCount || nestedFlags.Length != matchCount)
        {
            return false;
        }

        var result = new int[matchCount];
        int status;
        try
        {
            status = InvokeKernel("score_match_priority_v3", () => cobra_score_match_priority_v3(hotFlags, boundaryFlags, suppressedFlags, ruleArities, directFlags, nestedFlags, matchCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreUnionGroups(
        int[] anchorIds,
        int[] memberCounts,
        int[] generations,
        int[] nodeCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int groupCount = anchorIds.Length;
        if (groupCount == 0 || memberCounts.Length != groupCount || generations.Length != groupCount || nodeCounts.Length != groupCount)
        {
            return false;
        }

        var result = new int[groupCount];
        int status;
        try
        {
            status = InvokeKernel("score_union_groups", () => cobra_score_union_groups(anchorIds, memberCounts, generations, nodeCounts, groupCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreExtractClasses(
        int[] nodeCounts,
        int[] generations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = nodeCounts.Length;
        if (classCount == 0 || generations.Length != classCount)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            status = EnsureClassMetricsCached(nodeCounts, generations)
                ? InvokeKernel("score_extract_classes_cached", () => cobra_score_extract_classes_cached(classCount, result))
                : InvokeKernel("score_extract_classes", () => cobra_score_extract_classes(nodeCounts, generations, classCount, result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreExtractClassesById(
        int[] classIds,
        int[] nodeCounts,
        int[] generations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int classCount = classIds.Length;
        if (classCount == 0 || nodeCounts.Length != generations.Length || nodeCounts.Length == 0)
        {
            return false;
        }

        var result = new int[classCount];
        int status;
        try
        {
            bool hasCachedMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            status = hasCachedMetrics
                ? InvokeKernel("score_extract_classes_by_id_cached", () => cobra_score_extract_classes_by_id_cached(classIds, classCount, result))
                : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreExtractNodes(
        int[] headCodes,
        int[] arities,
        int[] classIds,
        int[] nodeCounts,
        int[] generations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int nodeCount = headCodes.Length;
        if (nodeCount == 0 || arities.Length != nodeCount || classIds.Length != nodeCount ||
            nodeCounts.Length == 0 || generations.Length != nodeCounts.Length)
        {
            return false;
        }

        var result = new int[nodeCount];
        int status;
        try
        {
            bool hasCachedClassMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            bool hasCachedExtractNodes = hasCachedClassMetrics && EnsureExtractNodeSnapshotCached(headCodes, arities, classIds);
            status = hasCachedExtractNodes
                ? InvokeKernel("score_extract_nodes_fully_cached", () => cobra_score_extract_nodes_fully_cached(result))
                : hasCachedClassMetrics
                    ? InvokeKernel("score_extract_nodes_cached", () => cobra_score_extract_nodes_cached(headCodes, arities, classIds, nodeCount, result))
                    : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreExtractNodesFullyCached(
        int nodeCount,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad() || nodeCount <= 0)
        {
            return false;
        }

        var result = new int[nodeCount];
        int status;
        try
        {
            status = InvokeKernel("score_extract_nodes_fully_cached", () => cobra_score_extract_nodes_fully_cached(result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreExtractNodesWithCachedClassMetrics(
        int[] headCodes,
        int[] arities,
        int[] classIds,
        int[] nodeCounts,
        int[] generations,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int nodeCount = headCodes.Length;
        if (nodeCount == 0 || arities.Length != nodeCount || classIds.Length != nodeCount ||
            nodeCounts.Length == 0 || generations.Length != nodeCounts.Length)
        {
            return false;
        }

        var result = new int[nodeCount];
        int status;
        try
        {
            bool hasCachedClassMetrics = EnsureClassMetricsCached(nodeCounts, generations);
            status = hasCachedClassMetrics
                ? InvokeKernel("score_extract_nodes_cached", () => cobra_score_extract_nodes_cached(headCodes, arities, classIds, nodeCount, result))
                : -1;
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryHashRepairTargets(
        int[] headHashes,
        int[] childStarts,
        int[] childCounts,
        int[] canonicalChildIds,
        out int[] targetHashes)
    {
        targetHashes = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int candidateCount = headHashes.Length;
        if (candidateCount == 0 ||
            childStarts.Length != candidateCount ||
            childCounts.Length != candidateCount)
        {
            return false;
        }

        var result = new int[candidateCount];
        int status;
        try
        {
            status = cobra_hash_repair_targets(
                headHashes,
                childStarts,
                childCounts,
                canonicalChildIds,
                candidateCount,
                result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        targetHashes = result;
        return true;
    }

    public static bool TryScoreRepairCandidates(
        int[] classIds,
        int[] childCounts,
        int[] generations,
        int[] nodeCounts,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int candidateCount = classIds.Length;
        if (candidateCount == 0 || childCounts.Length != candidateCount || generations.Length != candidateCount || nodeCounts.Length != candidateCount)
        {
            return false;
        }

        var result = new int[candidateCount];
        int status;
        try
        {
            status = cobra_score_repair_candidates(classIds, childCounts, generations, nodeCounts, candidateCount, result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryExtractEqualityUnions(
        int[] headCodes,
        int[] childStarts,
        int[] childCounts,
        int[] childIds,
        out int[] leftIds,
        out int[] rightIds)
    {
        leftIds = Array.Empty<int>();
        rightIds = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int nodeCount = headCodes.Length;
        if (nodeCount == 0 ||
            childStarts.Length != nodeCount ||
            childCounts.Length != nodeCount)
        {
            return false;
        }

        var validFlags = new int[nodeCount];
        var allLeftIds = new int[nodeCount];
        var allRightIds = new int[nodeCount];
        int status;
        try
        {
            status = cobra_extract_equality_unions(
                headCodes,
                childStarts,
                childCounts,
                childIds,
                nodeCount,
                validFlags,
                allLeftIds,
                allRightIds);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        int validCount = 0;
        for (int i = 0; i < validFlags.Length; i++)
        {
            if (validFlags[i] != 0)
            {
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return true;
        }

        leftIds = new int[validCount];
        rightIds = new int[validCount];
        int next = 0;
        for (int i = 0; i < validFlags.Length; i++)
        {
            if (validFlags[i] == 0)
            {
                continue;
            }

            leftIds[next] = allLeftIds[i];
            rightIds[next] = allRightIds[i];
            next++;
        }

        return true;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_detect_regions_batch")]
    private static extern int cobra_detect_regions_batch(
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int classCount,
        int[] familyCodes);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_cache_region_detection_masks")]
    private static extern int cobra_cache_region_detection_masks(
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int classCount);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_detect_regions_batch_cached")]
    private static extern int cobra_detect_regions_batch_cached(
        int classCount,
        int[] familyCodes);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_regions_v2")]
    private static extern int cobra_score_regions_v2(
        int[] familyCodes,
        int[] nodeCounts,
        int[] boundaryCounts,
        int regionCount,
        double[] benefitScores,
        double[] conflictScores);

    public static bool TryScoreRegionsV2(
        int[] familyCodes,
        int[] nodeCounts,
        int[] boundaryCounts,
        out double[] benefitScores,
        out double[] conflictScores)
    {
        benefitScores = Array.Empty<double>();
        conflictScores = Array.Empty<double>();
        if (!TryLoad()) return false;
        int regionCount = familyCodes.Length;
        if (regionCount <= 0) return false;

        var benefits = new double[regionCount];
        var conflicts = new double[regionCount];
        int status;
        try { status = cobra_score_regions_v2(familyCodes, nodeCounts, boundaryCounts, regionCount, benefits, conflicts); }
        catch { return false; }

        if (status != 0) return false;
        benefitScores = benefits;
        conflictScores = conflicts;
        return true;
    }

    public static bool TryDetectRegionsBatch(
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        out int[] familyCodes)
    {
        familyCodes = Array.Empty<int>();
        if (!TryLoad()) return false;
        int classCount = classHeadBucketMasks.Length;
        if (classCount <= 0) return false;

        var result = new int[classCount];
        int status;
        try { status = cobra_detect_regions_batch(classHeadBucketMasks, classExactHeadMasks, classCount, result); }
        catch { return false; }

        if (status != 0) return false;
        familyCodes = result;
        return true;
    }

    public static bool TryWarmRegionDetectionMasks(
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks)
    {
        if (!TryLoad())
        {
            return false;
        }

        int classCount = classHeadBucketMasks.Length;
        if (classCount <= 0 || classExactHeadMasks.Length != classCount)
        {
            return false;
        }

        int hash = ComputeHash(classHeadBucketMasks);
        hash = (hash * 397) ^ ComputeHash(classExactHeadMasks);
        if (_cachedRegionMaskLength == classCount && _cachedRegionMaskHash == hash)
        {
            return true;
        }

        int status;
        try
        {
            status = cobra_cache_region_detection_masks(classHeadBucketMasks, classExactHeadMasks, classCount);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        _cachedRegionMaskLength = classCount;
        _cachedRegionMaskHash = hash;
        return true;
    }

    public static bool TryDetectRegionsBatchCached(
        int classCount,
        out int[] familyCodes)
    {
        familyCodes = Array.Empty<int>();
        if (!TryLoad()) return false;
        if (classCount <= 0) return false;

        var result = new int[classCount];
        int status;
        try { status = cobra_detect_regions_batch_cached(classCount, result); }
        catch { return false; }

        if (status != 0) return false;
        familyCodes = result;
        return true;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_node_rule_candidates_batch_v4")]
    private static extern int cobra_score_node_rule_candidates_batch_v4(
        int[] classNodeOffsets,
        int[] classRuleOffsets,
        int[] classOutputOffsets,
        int[] classNodeCounts,
        int[] classRuleCounts,
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] nodeChildStarts,
        int[] nodeChildIds,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int[] directWildcardFlags,
        int[] ruleArgStarts,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks,
        int totalOutputSize,
        int totalNodes,
        int totalRules,
        int totalNodeChildren,
        int totalRuleArgs,
        int classCount,
        int graphClassCount,
        int[] hostScores);

    public static bool TryScoreNodeRuleCandidatesBatchV4(
        int[] classNodeOffsets,
        int[] classRuleOffsets,
        int[] classOutputOffsets,
        int[] classNodeCounts,
        int[] classRuleCounts,
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] nodeChildStarts,
        int[] nodeChildIds,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int[] directWildcardFlags,
        int[] ruleArgStarts,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks,
        int totalOutputSize,
        int totalNodes,
        int totalRules,
        int totalNodeChildren,
        int totalRuleArgs,
        int classCount,
        int graphClassCount,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad()) return false;
        if (totalOutputSize <= 0) return false;

        var result = new int[totalOutputSize];
        int status;
        try
        {
            status = InvokeKernel(
                "score_node_rule_candidates_batch_v4",
                () => cobra_score_node_rule_candidates_batch_v4(
                    classNodeOffsets, classRuleOffsets, classOutputOffsets, classNodeCounts, classRuleCounts,
                    nodeHeadCodes, nodeArities, nodeChildStarts, nodeChildIds,
                    classConstraintMasks, classHeadBucketMasks, classExactHeadMasks,
                    classChildEqualityMasks, classChildAtomBucketMasks, classChildConstraintMasks, classChildReferenceBloomMasks,
                    ruleHeadCodes, ruleArities, wildcardFlags, directWildcardFlags, ruleArgStarts,
                    ruleArgGroupIds, ruleArgConstraintMasks, ruleArgKinds, ruleArgHeadBuckets, ruleArgExactHeadMasks,
                    ruleArgNestedRepeatMasks, ruleArgNestedAtomBucketMasks, ruleArgNestedConstraintMasks, ruleArgNestedTopLevelReferenceMasks,
                    totalOutputSize, totalNodes, totalRules, totalNodeChildren, totalRuleArgs, classCount, graphClassCount, result));
        }
        catch { return false; }

        if (status != 0) return false;
        scores = result;
        return true;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_direct_match_candidates_batch")]
    private static extern int cobra_score_direct_match_candidates_batch(
        int[] classNodeOffsets,
        int[] classRuleOffsets,
        int[] classOutputOffsets,
        int[] classNodeCounts,
        int[] classRuleCounts,
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int totalOutputSize,
        int totalNodes,
        int totalRules,
        int classCount,
        int[] hostScores);

    public static bool TryScoreDirectMatchCandidatesBatch(
        int[] classNodeOffsets,
        int[] classRuleOffsets,
        int[] classOutputOffsets,
        int[] classNodeCounts,
        int[] classRuleCounts,
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int totalOutputSize,
        int totalNodes,
        int totalRules,
        int classCount,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        if (totalOutputSize <= 0)
        {
            return false;
        }

        var result = new int[totalOutputSize];
        int status;
        try
        {
            status = InvokeKernel(
                "score_direct_match_candidates_batch",
                () => cobra_score_direct_match_candidates_batch(
                    classNodeOffsets,
                    classRuleOffsets,
                    classOutputOffsets,
                    classNodeCounts,
                    classRuleCounts,
                    nodeHeadCodes,
                    nodeArities,
                    ruleHeadCodes,
                    ruleArities,
                    wildcardFlags,
                    totalOutputSize,
                    totalNodes,
                    totalRules,
                    classCount,
                    result));
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "cobra_score_node_rule_candidates_batch")]
    private static extern int cobra_score_node_rule_candidates_batch(
        int[] classNodeOffsets,
        int[] classRuleOffsets,
        int[] classOutputOffsets,
        int[] classNodeCounts,
        int[] classRuleCounts,
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int totalOutputSize,
        int totalNodes,
        int totalRules,
        int classCount,
        int[] hostScores);

    public static bool TryScoreNodeRuleCandidatesBatch(
        int[] classNodeOffsets,
        int[] classRuleOffsets,
        int[] classOutputOffsets,
        int[] classNodeCounts,
        int[] classRuleCounts,
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int totalOutputSize,
        int totalNodes,
        int totalRules,
        int classCount,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        if (totalOutputSize <= 0)
        {
            return false;
        }

        var result = new int[totalOutputSize];
        int status;
        try
        {
            status = cobra_score_node_rule_candidates_batch(
                classNodeOffsets,
                classRuleOffsets,
                classOutputOffsets,
                classNodeCounts,
                classRuleCounts,
                nodeHeadCodes,
                nodeArities,
                ruleHeadCodes,
                ruleArities,
                wildcardFlags,
                totalOutputSize,
                totalNodes,
                totalRules,
                classCount,
                result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryScoreNodeRuleCandidates(
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] nodeChildStarts,
        int[] nodeChildIds,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int[] directWildcardFlags,
        int[] ruleArgStarts,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks,
        out int[] scores)
    {
        scores = Array.Empty<int>();
        if (!TryLoad())
        {
            return false;
        }

        int nodeCount = nodeHeadCodes.Length;
        int ruleCount = ruleHeadCodes.Length;
        if (nodeCount == 0 || ruleCount == 0 || nodeArities.Length != nodeCount ||
              nodeChildStarts.Length != nodeCount || classConstraintMasks.Length == 0 ||
              classHeadBucketMasks.Length == 0 || classExactHeadMasks.Length == 0 || classChildEqualityMasks.Length == 0 ||
              classChildAtomBucketMasks.Length == 0 || classChildConstraintMasks.Length == 0 || classChildReferenceBloomMasks.Length == 0 ||
              ruleArities.Length != ruleCount ||
              wildcardFlags.Length != ruleCount || directWildcardFlags.Length != ruleCount ||
              ruleArgStarts.Length != ruleCount || ruleArgConstraintMasks.Length != ruleArgGroupIds.Length ||
              ruleArgKinds.Length != ruleArgGroupIds.Length || ruleArgHeadBuckets.Length != ruleArgGroupIds.Length ||
              ruleArgExactHeadMasks.Length != ruleArgGroupIds.Length || ruleArgNestedRepeatMasks.Length != ruleArgGroupIds.Length ||
              ruleArgNestedAtomBucketMasks.Length != ruleArgGroupIds.Length * 4 ||
              ruleArgNestedConstraintMasks.Length != ruleArgGroupIds.Length * 4 ||
              ruleArgNestedTopLevelReferenceMasks.Length != ruleArgGroupIds.Length * 4)
        {
            return false;
        }

        var result = new int[nodeCount * ruleCount];
        int status;
        try
        {
            bool hasCachedNodeSnapshot = EnsureNodeRuleSnapshotCached(
                nodeHeadCodes,
                nodeArities,
                nodeChildStarts,
                nodeChildIds,
                classConstraintMasks,
                classHeadBucketMasks,
                classExactHeadMasks,
                classChildEqualityMasks,
                classChildAtomBucketMasks,
                classChildConstraintMasks,
                classChildReferenceBloomMasks);
            bool hasCachedRuleSignature = hasCachedNodeSnapshot && EnsureRuleSignatureCached(
                ruleHeadCodes,
                ruleArities,
                wildcardFlags,
                directWildcardFlags,
                ruleArgStarts,
                ruleArgGroupIds,
                ruleArgConstraintMasks,
                ruleArgKinds,
                ruleArgHeadBuckets,
                ruleArgExactHeadMasks,
                ruleArgNestedRepeatMasks,
                ruleArgNestedAtomBucketMasks,
                ruleArgNestedConstraintMasks,
                ruleArgNestedTopLevelReferenceMasks);

            status = hasCachedRuleSignature
                ? cobra_score_node_rule_candidates_fully_cached(
                    ruleArgGroupIds.Length,
                    ruleCount,
                    result)
                : hasCachedNodeSnapshot
                    ? cobra_score_node_rule_candidates_cached(
                        ruleHeadCodes,
                        ruleArities,
                        wildcardFlags,
                        directWildcardFlags,
                        ruleArgStarts,
                        ruleArgGroupIds,
                        ruleArgConstraintMasks,
                        ruleArgKinds,
                        ruleArgHeadBuckets,
                        ruleArgExactHeadMasks,
                        ruleArgNestedRepeatMasks,
                        ruleArgNestedAtomBucketMasks,
                        ruleArgNestedConstraintMasks,
                        ruleArgNestedTopLevelReferenceMasks,
                        ruleArgGroupIds.Length,
                        ruleCount,
                        result)
                    : cobra_score_node_rule_candidates(
                        nodeHeadCodes,
                        nodeArities,
                        nodeChildStarts,
                        nodeChildIds,
                        classConstraintMasks,
                        classHeadBucketMasks,
                        classExactHeadMasks,
                        classChildEqualityMasks,
                        classChildAtomBucketMasks,
                        classChildConstraintMasks,
                        classChildReferenceBloomMasks,
                        nodeCount,
                        ruleHeadCodes,
                        ruleArities,
                        wildcardFlags,
                        directWildcardFlags,
                        ruleArgStarts,
                        ruleArgGroupIds,
                        ruleArgConstraintMasks,
                        ruleArgKinds,
                        ruleArgHeadBuckets,
                        ruleArgExactHeadMasks,
                        ruleArgNestedRepeatMasks,
                        ruleArgNestedAtomBucketMasks,
                        ruleArgNestedConstraintMasks,
                        ruleArgNestedTopLevelReferenceMasks,
                        ruleCount,
                        result);
        }
        catch
        {
            return false;
        }

        if (status != 0)
        {
            return false;
        }

        scores = result;
        return true;
    }

    public static bool TryExtractCompatibleNodeRulePairsCached(
        int[] nodeHeadCodes,
        int[] nodeArities,
        int[] nodeChildStarts,
        int[] nodeChildIds,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks,
        int[] ruleHeadCodes,
        int[] ruleArities,
        int[] wildcardFlags,
        int[] directWildcardFlags,
        int[] ruleArgStarts,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks,
        out (int NodeIndex, int RuleIndex)[] pairs)
    {
        pairs = Array.Empty<(int NodeIndex, int RuleIndex)>();
        if (!TryLoad())
        {
            return false;
        }

        int nodeCount = nodeHeadCodes.Length;
        int ruleCount = ruleHeadCodes.Length;
        if (nodeCount == 0 || ruleCount == 0 || nodeArities.Length != nodeCount ||
            nodeChildStarts.Length != nodeCount || classConstraintMasks.Length == 0 ||
            classHeadBucketMasks.Length == 0 || classExactHeadMasks.Length == 0 || classChildEqualityMasks.Length == 0 ||
            classChildAtomBucketMasks.Length == 0 || classChildConstraintMasks.Length == 0 || classChildReferenceBloomMasks.Length == 0 ||
            ruleArities.Length != ruleCount || wildcardFlags.Length != ruleCount || directWildcardFlags.Length != ruleCount ||
            ruleArgStarts.Length != ruleCount || ruleArgConstraintMasks.Length != ruleArgGroupIds.Length ||
            ruleArgKinds.Length != ruleArgGroupIds.Length || ruleArgHeadBuckets.Length != ruleArgGroupIds.Length ||
            ruleArgExactHeadMasks.Length != ruleArgGroupIds.Length || ruleArgNestedRepeatMasks.Length != ruleArgGroupIds.Length ||
            ruleArgNestedAtomBucketMasks.Length != ruleArgGroupIds.Length * 4 ||
            ruleArgNestedConstraintMasks.Length != ruleArgGroupIds.Length * 4 ||
            ruleArgNestedTopLevelReferenceMasks.Length != ruleArgGroupIds.Length * 4)
        {
            return false;
        }

        try
        {
            bool hasCachedNodeSnapshot = EnsureNodeRuleSnapshotCached(
                nodeHeadCodes,
                nodeArities,
                nodeChildStarts,
                nodeChildIds,
                classConstraintMasks,
                classHeadBucketMasks,
                classExactHeadMasks,
                classChildEqualityMasks,
                classChildAtomBucketMasks,
                classChildConstraintMasks,
                classChildReferenceBloomMasks);
            bool hasCachedRuleSignature = hasCachedNodeSnapshot && EnsureRuleSignatureCached(
                ruleHeadCodes,
                ruleArities,
                wildcardFlags,
                directWildcardFlags,
                ruleArgStarts,
                ruleArgGroupIds,
                ruleArgConstraintMasks,
                ruleArgKinds,
                ruleArgHeadBuckets,
                ruleArgExactHeadMasks,
                ruleArgNestedRepeatMasks,
                ruleArgNestedAtomBucketMasks,
                ruleArgNestedConstraintMasks,
                ruleArgNestedTopLevelReferenceMasks);

            if (!hasCachedRuleSignature)
            {
                return false;
            }

            int maxPairs = checked(nodeCount * ruleCount);
            var pairNodeIndices = new int[maxPairs];
            var pairRuleIndices = new int[maxPairs];
            int status = cobra_extract_node_rule_pairs_fully_cached(
                ruleArgGroupIds.Length,
                ruleCount,
                maxPairs,
                pairNodeIndices,
                pairRuleIndices,
                out int pairCount);
            if (status != 0)
            {
                return false;
            }

            var result = new (int NodeIndex, int RuleIndex)[pairCount];
            for (int i = 0; i < pairCount; i++)
            {
                result[i] = (pairNodeIndices[i], pairRuleIndices[i]);
            }

            pairs = result;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string[] GetCandidatePaths()
    {
        string baseDir = AppContext.BaseDirectory;
        string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "native", LibraryName),
            Path.Combine(assemblyDir, "native", LibraryName)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DirectoryInfo? current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 8 && current is not null; i++, current = current.Parent)
        {
            string root = current.FullName;
            candidates.Add(Path.Combine(root, "SymCobra", "bin", "Debug", "net10.0", "native", LibraryName));
            candidates.Add(Path.Combine(root, "src", "SymCobra", "bin", "Debug", "net10.0", "native", LibraryName));
            candidates.Add(Path.Combine(root, "SymCobra.CudaNative", "bin", LibraryName));
            candidates.Add(Path.Combine(root, "src", "SymCobra.CudaNative", "bin", LibraryName));
        }

        return candidates
            .Where(path => seen.Add(path))
            .ToArray();
    }
}
