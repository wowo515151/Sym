#include <cuda_runtime.h>
#include <math.h>

extern "C" __declspec(dllexport) int cobra_device_count()
{
    int count = 0;
    cudaError_t err = cudaGetDeviceCount(&count);
    if (err != cudaSuccess)
    {
        return 0;
    }

    return count;
}

static int* g_cachedParents = nullptr;
static int g_cachedParentCount = 0;
static int* g_cachedRepairParents = nullptr;
static int g_cachedRepairParentCount = 0;
static int* g_cachedRepairChildStarts = nullptr;
static int* g_cachedRepairChildCounts = nullptr;
static int* g_cachedRepairChildIds = nullptr;
static int g_cachedRepairNodeCount = 0;
static int g_cachedRepairChildIdCount = 0;
static int* g_cachedClassNodeCounts = nullptr;
static int* g_cachedClassGenerations = nullptr;
static int g_cachedClassCount = 0;
static int* g_cachedNodeHeadCodes = nullptr;
static int* g_cachedNodeArities = nullptr;
static int* g_cachedNodeChildStarts = nullptr;
static int* g_cachedNodeChildIds = nullptr;
static int* g_cachedClassConstraintMasks = nullptr;
static int* g_cachedClassHeadBucketMasks = nullptr;
static int* g_cachedClassExactHeadMasks = nullptr;
static int* g_cachedClassChildEqualityMasks = nullptr;
static int* g_cachedClassChildAtomBucketMasks = nullptr;
static int* g_cachedClassChildConstraintMasks = nullptr;
static int* g_cachedClassChildReferenceBloomMasks = nullptr;
static int g_cachedNodeRuleNodeCount = 0;
static int g_cachedNodeRuleChildIdCount = 0;
static int g_cachedNodeRuleClassCount = 0;
static int* g_cachedRegionHeadBucketMasks = nullptr;
static int* g_cachedRegionExactHeadMasks = nullptr;
static int g_cachedRegionClassCount = 0;
static int* g_cachedRuleHeadCodes = nullptr;
static int* g_cachedRuleArities = nullptr;
static int* g_cachedWildcardFlags = nullptr;
static int* g_cachedDirectWildcardFlags = nullptr;
static int* g_cachedRuleArgStarts = nullptr;
static int* g_cachedRuleArgGroupIds = nullptr;
static int* g_cachedRuleArgConstraintMasks = nullptr;
static int* g_cachedRuleArgKinds = nullptr;
static int* g_cachedRuleArgHeadBuckets = nullptr;
static int* g_cachedRuleArgExactHeadMasks = nullptr;
static int* g_cachedRuleArgNestedRepeatMasks = nullptr;
static int* g_cachedRuleArgNestedAtomBucketMasks = nullptr;
static int* g_cachedRuleArgNestedConstraintMasks = nullptr;
static int* g_cachedRuleArgNestedTopLevelReferenceMasks = nullptr;
static int g_cachedRuleCount = 0;
static int g_cachedRuleArgCount = 0;
static int* g_cachedExtractHeadCodes = nullptr;
static int* g_cachedExtractArities = nullptr;
static int* g_cachedExtractClassIds = nullptr;
static int g_cachedExtractNodeCount = 0;

__global__ void scoreRebuildWithRepairKernel(
    const int* nodeCounts,
    const int* generations,
    const int* repairCounts,
    int classCount,
    int* scores);

__global__ void hashRepairTargetsKernel(
    const int* headHashes,
    const int* childStarts,
    const int* childCounts,
    const int* canonicalChildIds,
    int candidateCount,
    int* targetHashes);

__global__ void scoreAnalysisWithRepairKernel(
    const int* nodeCounts,
    const int* generations,
    const int* unresolvedFlags,
    const int* repairCounts,
    int classCount,
    int* scores);

__global__ void scoreExtractClassesKernel(
    const int* nodeCounts,
    const int* generations,
    int classCount,
    int* scores);

__global__ void scoreExtractNodesCachedKernel(
    const int* headCodes,
    const int* arities,
    const int* classIds,
    const int* generations,
    const int* nodeCounts,
    int classCount,
    int nodeCount,
    int* scores);

__global__ void scoreUnionMembersKernel(
    const int* memberIds,
    int memberCount,
    const int* nodeCounts,
    const int* generations,
    int* scores);

__global__ void gatherClassMetricKernel(
    const int* classIds,
    const int* metrics,
    int count,
    int* gathered)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= count)
    {
        return;
    }

    int classId = classIds[index];
    gathered[index] = classId >= 0 ? metrics[classId] : 0;
}

__global__ void applyParentUpdatesKernel(
    int* parents,
    int parentCount,
    const int* classIds,
    const int* parentIds,
    int updateCount)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= updateCount)
    {
        return;
    }

    int classId = classIds[index];
    if (classId < 0 || classId >= parentCount)
    {
        return;
    }

    parents[classId] = parentIds[index];
}

static void freeParentCache()
{
    if (g_cachedParents != nullptr)
    {
        cudaFree(g_cachedParents);
        g_cachedParents = nullptr;
    }
    g_cachedParentCount = 0;
}

static void freeRepairCache()
{
    if (g_cachedRepairParents != nullptr)
    {
        cudaFree(g_cachedRepairParents);
        g_cachedRepairParents = nullptr;
    }
    if (g_cachedRepairChildStarts != nullptr)
    {
        cudaFree(g_cachedRepairChildStarts);
        g_cachedRepairChildStarts = nullptr;
    }
    if (g_cachedRepairChildCounts != nullptr)
    {
        cudaFree(g_cachedRepairChildCounts);
        g_cachedRepairChildCounts = nullptr;
    }
    if (g_cachedRepairChildIds != nullptr)
    {
        cudaFree(g_cachedRepairChildIds);
        g_cachedRepairChildIds = nullptr;
    }

    g_cachedRepairParentCount = 0;
    g_cachedRepairNodeCount = 0;
    g_cachedRepairChildIdCount = 0;
}

static void freeClassMetricCache()
{
    if (g_cachedClassNodeCounts != nullptr)
    {
        cudaFree(g_cachedClassNodeCounts);
        g_cachedClassNodeCounts = nullptr;
    }
    if (g_cachedClassGenerations != nullptr)
    {
        cudaFree(g_cachedClassGenerations);
        g_cachedClassGenerations = nullptr;
    }
    g_cachedClassCount = 0;
}

static void freeNodeRuleSnapshotCache()
{
    if (g_cachedNodeHeadCodes != nullptr) { cudaFree(g_cachedNodeHeadCodes); g_cachedNodeHeadCodes = nullptr; }
    if (g_cachedNodeArities != nullptr) { cudaFree(g_cachedNodeArities); g_cachedNodeArities = nullptr; }
    if (g_cachedNodeChildStarts != nullptr) { cudaFree(g_cachedNodeChildStarts); g_cachedNodeChildStarts = nullptr; }
    if (g_cachedNodeChildIds != nullptr) { cudaFree(g_cachedNodeChildIds); g_cachedNodeChildIds = nullptr; }
    if (g_cachedClassConstraintMasks != nullptr) { cudaFree(g_cachedClassConstraintMasks); g_cachedClassConstraintMasks = nullptr; }
    if (g_cachedClassHeadBucketMasks != nullptr) { cudaFree(g_cachedClassHeadBucketMasks); g_cachedClassHeadBucketMasks = nullptr; }
    if (g_cachedClassExactHeadMasks != nullptr) { cudaFree(g_cachedClassExactHeadMasks); g_cachedClassExactHeadMasks = nullptr; }
    if (g_cachedClassChildEqualityMasks != nullptr) { cudaFree(g_cachedClassChildEqualityMasks); g_cachedClassChildEqualityMasks = nullptr; }
    if (g_cachedClassChildAtomBucketMasks != nullptr) { cudaFree(g_cachedClassChildAtomBucketMasks); g_cachedClassChildAtomBucketMasks = nullptr; }
    if (g_cachedClassChildConstraintMasks != nullptr) { cudaFree(g_cachedClassChildConstraintMasks); g_cachedClassChildConstraintMasks = nullptr; }
    if (g_cachedClassChildReferenceBloomMasks != nullptr) { cudaFree(g_cachedClassChildReferenceBloomMasks); g_cachedClassChildReferenceBloomMasks = nullptr; }
    g_cachedNodeRuleNodeCount = 0;
    g_cachedNodeRuleChildIdCount = 0;
    g_cachedNodeRuleClassCount = 0;
}

static void freeRegionDetectionCache()
{
    if (g_cachedRegionHeadBucketMasks != nullptr) { cudaFree(g_cachedRegionHeadBucketMasks); g_cachedRegionHeadBucketMasks = nullptr; }
    if (g_cachedRegionExactHeadMasks != nullptr) { cudaFree(g_cachedRegionExactHeadMasks); g_cachedRegionExactHeadMasks = nullptr; }
    g_cachedRegionClassCount = 0;
}

static void freeRuleSignatureCache()
{
    if (g_cachedRuleHeadCodes != nullptr) { cudaFree(g_cachedRuleHeadCodes); g_cachedRuleHeadCodes = nullptr; }
    if (g_cachedRuleArities != nullptr) { cudaFree(g_cachedRuleArities); g_cachedRuleArities = nullptr; }
    if (g_cachedWildcardFlags != nullptr) { cudaFree(g_cachedWildcardFlags); g_cachedWildcardFlags = nullptr; }
    if (g_cachedDirectWildcardFlags != nullptr) { cudaFree(g_cachedDirectWildcardFlags); g_cachedDirectWildcardFlags = nullptr; }
    if (g_cachedRuleArgStarts != nullptr) { cudaFree(g_cachedRuleArgStarts); g_cachedRuleArgStarts = nullptr; }
    if (g_cachedRuleArgGroupIds != nullptr) { cudaFree(g_cachedRuleArgGroupIds); g_cachedRuleArgGroupIds = nullptr; }
    if (g_cachedRuleArgConstraintMasks != nullptr) { cudaFree(g_cachedRuleArgConstraintMasks); g_cachedRuleArgConstraintMasks = nullptr; }
    if (g_cachedRuleArgKinds != nullptr) { cudaFree(g_cachedRuleArgKinds); g_cachedRuleArgKinds = nullptr; }
    if (g_cachedRuleArgHeadBuckets != nullptr) { cudaFree(g_cachedRuleArgHeadBuckets); g_cachedRuleArgHeadBuckets = nullptr; }
    if (g_cachedRuleArgExactHeadMasks != nullptr) { cudaFree(g_cachedRuleArgExactHeadMasks); g_cachedRuleArgExactHeadMasks = nullptr; }
    if (g_cachedRuleArgNestedRepeatMasks != nullptr) { cudaFree(g_cachedRuleArgNestedRepeatMasks); g_cachedRuleArgNestedRepeatMasks = nullptr; }
    if (g_cachedRuleArgNestedAtomBucketMasks != nullptr) { cudaFree(g_cachedRuleArgNestedAtomBucketMasks); g_cachedRuleArgNestedAtomBucketMasks = nullptr; }
    if (g_cachedRuleArgNestedConstraintMasks != nullptr) { cudaFree(g_cachedRuleArgNestedConstraintMasks); g_cachedRuleArgNestedConstraintMasks = nullptr; }
    if (g_cachedRuleArgNestedTopLevelReferenceMasks != nullptr) { cudaFree(g_cachedRuleArgNestedTopLevelReferenceMasks); g_cachedRuleArgNestedTopLevelReferenceMasks = nullptr; }
    g_cachedRuleCount = 0;
    g_cachedRuleArgCount = 0;
}

static void freeExtractNodeCache()
{
    if (g_cachedExtractHeadCodes != nullptr)
    {
        cudaFree(g_cachedExtractHeadCodes);
        g_cachedExtractHeadCodes = nullptr;
    }
    if (g_cachedExtractArities != nullptr)
    {
        cudaFree(g_cachedExtractArities);
        g_cachedExtractArities = nullptr;
    }
    if (g_cachedExtractClassIds != nullptr)
    {
        cudaFree(g_cachedExtractClassIds);
        g_cachedExtractClassIds = nullptr;
    }
    g_cachedExtractNodeCount = 0;
}

extern "C" __declspec(dllexport) int cobra_cache_parent_snapshot(
    const int* parents,
    int parentCount)
{
    if (parents == nullptr || parentCount <= 0)
    {
        return 1001;
    }

    if (g_cachedParentCount != parentCount)
    {
        freeParentCache();
        cudaError_t err = cudaMalloc(&g_cachedParents, static_cast<size_t>(parentCount) * sizeof(int));
        if (err != cudaSuccess)
        {
            freeParentCache();
            return 1002;
        }
        g_cachedParentCount = parentCount;
    }

    cudaError_t err = cudaMemcpy(
        g_cachedParents,
        parents,
        static_cast<size_t>(parentCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess)
    {
        freeParentCache();
        return 1003;
    }

    return 0;
}

extern "C" __declspec(dllexport) int cobra_apply_parent_updates_cached(
    const int* classIds,
    const int* parentIds,
    int updateCount)
{
    if (g_cachedParents == nullptr || g_cachedParentCount <= 0 || classIds == nullptr || parentIds == nullptr)
    {
        return 1004;
    }

    if (updateCount <= 0)
    {
        return 1005;
    }

    int* deviceClassIds = nullptr;
    int* deviceParentIds = nullptr;
    size_t bytes = static_cast<size_t>(updateCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, bytes);
    if (err != cudaSuccess) return 1006;
    err = cudaMalloc(&deviceParentIds, bytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceClassIds);
        return 1007;
    }

    err = cudaMemcpy(deviceClassIds, classIds, bytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1008;
    err = cudaMemcpy(deviceParentIds, parentIds, bytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1009;

    {
        int blockSize = 128;
        int gridSize = (updateCount + blockSize - 1) / blockSize;
        applyParentUpdatesKernel<<<gridSize, blockSize>>>(
            g_cachedParents,
            g_cachedParentCount,
            deviceClassIds,
            deviceParentIds,
            updateCount);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_1010;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_1011;

    cudaFree(deviceClassIds);
    cudaFree(deviceParentIds);
    return 0;

cleanup_error_1011:
    cudaFree(deviceClassIds);
    cudaFree(deviceParentIds);
    return 1011;
cleanup_error_1010:
    cudaFree(deviceClassIds);
    cudaFree(deviceParentIds);
    return 1010;
cleanup_error_1009:
    cudaFree(deviceClassIds);
    cudaFree(deviceParentIds);
    return 1009;
cleanup_error_1008:
    cudaFree(deviceClassIds);
    cudaFree(deviceParentIds);
    return 1008;
}

extern "C" __declspec(dllexport) int cobra_cache_repair_snapshot(
    const int* parents,
    int parentCount,
    const int* childStarts,
    const int* childCounts,
    const int* childIds,
    int nodeCount)
{
    if (parents == nullptr || childStarts == nullptr || childCounts == nullptr || childIds == nullptr ||
        parentCount <= 0 || nodeCount <= 0)
    {
        return 1010;
    }

    int childIdCount = childStarts[nodeCount - 1] + childCounts[nodeCount - 1];
    if (childIdCount < 0)
    {
        return 1011;
    }

    bool parentSizeChanged = g_cachedRepairParentCount != parentCount;
    bool nodeSizeChanged = g_cachedRepairNodeCount != nodeCount;
    bool childSizeChanged = g_cachedRepairChildIdCount != childIdCount;

    if (parentSizeChanged || nodeSizeChanged || childSizeChanged)
    {
        freeRepairCache();

        cudaError_t err = cudaMalloc(&g_cachedRepairParents, static_cast<size_t>(parentCount) * sizeof(int));
        if (err != cudaSuccess) { freeRepairCache(); return 1012; }
        err = cudaMalloc(&g_cachedRepairChildStarts, static_cast<size_t>(nodeCount) * sizeof(int));
        if (err != cudaSuccess) { freeRepairCache(); return 1013; }
        err = cudaMalloc(&g_cachedRepairChildCounts, static_cast<size_t>(nodeCount) * sizeof(int));
        if (err != cudaSuccess) { freeRepairCache(); return 1014; }
        err = cudaMalloc(&g_cachedRepairChildIds, static_cast<size_t>(childIdCount) * sizeof(int));
        if (err != cudaSuccess) { freeRepairCache(); return 1015; }

        g_cachedRepairParentCount = parentCount;
        g_cachedRepairNodeCount = nodeCount;
        g_cachedRepairChildIdCount = childIdCount;
    }

    cudaError_t err = cudaMemcpy(
        g_cachedRepairParents,
        parents,
        static_cast<size_t>(parentCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeRepairCache(); return 1016; }
    err = cudaMemcpy(
        g_cachedRepairChildStarts,
        childStarts,
        static_cast<size_t>(nodeCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeRepairCache(); return 1017; }
    err = cudaMemcpy(
        g_cachedRepairChildCounts,
        childCounts,
        static_cast<size_t>(nodeCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeRepairCache(); return 1018; }
    err = cudaMemcpy(
        g_cachedRepairChildIds,
        childIds,
        static_cast<size_t>(childIdCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeRepairCache(); return 1019; }

    return 0;
}

extern "C" __declspec(dllexport) int cobra_cache_class_metrics(
    const int* nodeCounts,
    const int* generations,
    int classCount)
{
    if (nodeCounts == nullptr || generations == nullptr || classCount <= 0)
    {
        return 1025;
    }

    if (g_cachedClassCount != classCount)
    {
        freeClassMetricCache();
        cudaError_t err = cudaMalloc(&g_cachedClassNodeCounts, static_cast<size_t>(classCount) * sizeof(int));
        if (err != cudaSuccess) { freeClassMetricCache(); return 1026; }
        err = cudaMalloc(&g_cachedClassGenerations, static_cast<size_t>(classCount) * sizeof(int));
        if (err != cudaSuccess) { freeClassMetricCache(); return 1027; }
        g_cachedClassCount = classCount;
    }

    cudaError_t err = cudaMemcpy(
        g_cachedClassNodeCounts,
        nodeCounts,
        static_cast<size_t>(classCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeClassMetricCache(); return 1028; }
    err = cudaMemcpy(
        g_cachedClassGenerations,
        generations,
        static_cast<size_t>(classCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeClassMetricCache(); return 1029; }

    return 0;
}

extern "C" __declspec(dllexport) int cobra_apply_class_metric_updates_cached(
    const int* classIds,
    const int* nodeCounts,
    const int* generations,
    int updateCount)
{
    if (g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0 ||
        classIds == nullptr || nodeCounts == nullptr || generations == nullptr)
    {
        return 1030;
    }

    if (updateCount <= 0)
    {
        return 1031;
    }

    int* deviceClassIds = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    size_t bytes = static_cast<size_t>(updateCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, bytes);
    if (err != cudaSuccess) return 1032;
    err = cudaMalloc(&deviceNodeCounts, bytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 1033; }
    err = cudaMalloc(&deviceGenerations, bytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); return 1034; }

    err = cudaMemcpy(deviceClassIds, classIds, bytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1035;
    err = cudaMemcpy(deviceNodeCounts, nodeCounts, bytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1036;
    err = cudaMemcpy(deviceGenerations, generations, bytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1037;

    {
        int blockSize = 128;
        int gridSize = (updateCount + blockSize - 1) / blockSize;
        applyParentUpdatesKernel<<<gridSize, blockSize>>>(
            g_cachedClassNodeCounts,
            g_cachedClassCount,
            deviceClassIds,
            deviceNodeCounts,
            updateCount);
        applyParentUpdatesKernel<<<gridSize, blockSize>>>(
            g_cachedClassGenerations,
            g_cachedClassCount,
            deviceClassIds,
            deviceGenerations,
            updateCount);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_1038;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_1039;

    cudaFree(deviceClassIds);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    return 0;

cleanup_error_1039:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 1039;
cleanup_error_1038:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 1038;
cleanup_error_1037:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 1037;
cleanup_error_1036:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 1036;
cleanup_error_1035:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 1035;
}

extern "C" __declspec(dllexport) int cobra_cache_extract_node_snapshot(
    const int* headCodes,
    const int* arities,
    const int* classIds,
    int nodeCount)
{
    if (headCodes == nullptr || arities == nullptr || classIds == nullptr || nodeCount <= 0)
    {
        return 10920;
    }

    if (g_cachedExtractNodeCount != nodeCount)
    {
        freeExtractNodeCache();
        cudaError_t err = cudaMalloc(&g_cachedExtractHeadCodes, static_cast<size_t>(nodeCount) * sizeof(int));
        if (err != cudaSuccess) { freeExtractNodeCache(); return 10921; }
        err = cudaMalloc(&g_cachedExtractArities, static_cast<size_t>(nodeCount) * sizeof(int));
        if (err != cudaSuccess) { freeExtractNodeCache(); return 10922; }
        err = cudaMalloc(&g_cachedExtractClassIds, static_cast<size_t>(nodeCount) * sizeof(int));
        if (err != cudaSuccess) { freeExtractNodeCache(); return 10923; }
        g_cachedExtractNodeCount = nodeCount;
    }

    cudaError_t err = cudaMemcpy(
        g_cachedExtractHeadCodes,
        headCodes,
        static_cast<size_t>(nodeCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeExtractNodeCache(); return 10924; }

    err = cudaMemcpy(
        g_cachedExtractArities,
        arities,
        static_cast<size_t>(nodeCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeExtractNodeCache(); return 10925; }

    err = cudaMemcpy(
        g_cachedExtractClassIds,
        classIds,
        static_cast<size_t>(nodeCount) * sizeof(int),
        cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeExtractNodeCache(); return 10926; }

    return 0;
}

extern "C" __declspec(dllexport) int cobra_cache_node_rule_snapshot(
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* nodeChildStarts,
    const int* nodeChildIds,
    const int* classConstraintMasks,
    const int* classHeadBucketMasks,
    const int* classExactHeadMasks,
    const int* classChildEqualityMasks,
    const int* classChildAtomBucketMasks,
    const int* classChildConstraintMasks,
    const int* classChildReferenceBloomMasks,
    int nodeCount,
    int childIdCount,
    int classCount)
{
    if (nodeHeadCodes == nullptr || nodeArities == nullptr || nodeChildStarts == nullptr || nodeChildIds == nullptr ||
        classConstraintMasks == nullptr || classHeadBucketMasks == nullptr || classExactHeadMasks == nullptr ||
        classChildEqualityMasks == nullptr || classChildAtomBucketMasks == nullptr || classChildConstraintMasks == nullptr ||
        classChildReferenceBloomMasks == nullptr || nodeCount <= 0 || childIdCount < 0 || classCount <= 0)
    {
        return 1035;
    }

    if (g_cachedNodeRuleNodeCount != nodeCount ||
        g_cachedNodeRuleChildIdCount != childIdCount ||
        g_cachedNodeRuleClassCount != classCount)
    {
        freeNodeRuleSnapshotCache();
        cudaError_t err = cudaMalloc(&g_cachedNodeHeadCodes, static_cast<size_t>(nodeCount) * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1036; }
        err = cudaMalloc(&g_cachedNodeArities, static_cast<size_t>(nodeCount) * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1037; }
        err = cudaMalloc(&g_cachedNodeChildStarts, static_cast<size_t>(nodeCount) * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1038; }
        if (childIdCount > 0)
        {
            err = cudaMalloc(&g_cachedNodeChildIds, static_cast<size_t>(childIdCount) * sizeof(int));
            if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1039; }
        }
        err = cudaMalloc(&g_cachedClassConstraintMasks, static_cast<size_t>(classCount) * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1040; }
        err = cudaMalloc(&g_cachedClassHeadBucketMasks, static_cast<size_t>(classCount) * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1041; }
        err = cudaMalloc(&g_cachedClassExactHeadMasks, static_cast<size_t>(classCount) * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1042; }
        err = cudaMalloc(&g_cachedClassChildEqualityMasks, static_cast<size_t>(classCount) * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1043; }
        err = cudaMalloc(&g_cachedClassChildAtomBucketMasks, static_cast<size_t>(classCount) * 4 * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1044; }
        err = cudaMalloc(&g_cachedClassChildConstraintMasks, static_cast<size_t>(classCount) * 4 * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1045; }
        err = cudaMalloc(&g_cachedClassChildReferenceBloomMasks, static_cast<size_t>(classCount) * 4 * sizeof(int));
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1046; }

        g_cachedNodeRuleNodeCount = nodeCount;
        g_cachedNodeRuleChildIdCount = childIdCount;
        g_cachedNodeRuleClassCount = classCount;
    }

    cudaError_t err = cudaMemcpy(g_cachedNodeHeadCodes, nodeHeadCodes, static_cast<size_t>(nodeCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1047; }
    err = cudaMemcpy(g_cachedNodeArities, nodeArities, static_cast<size_t>(nodeCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1048; }
    err = cudaMemcpy(g_cachedNodeChildStarts, nodeChildStarts, static_cast<size_t>(nodeCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1049; }
    if (childIdCount > 0)
    {
        err = cudaMemcpy(g_cachedNodeChildIds, nodeChildIds, static_cast<size_t>(childIdCount) * sizeof(int), cudaMemcpyHostToDevice);
        if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1050; }
    }
    err = cudaMemcpy(g_cachedClassConstraintMasks, classConstraintMasks, static_cast<size_t>(classCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1051; }
    err = cudaMemcpy(g_cachedClassHeadBucketMasks, classHeadBucketMasks, static_cast<size_t>(classCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1052; }
    err = cudaMemcpy(g_cachedClassExactHeadMasks, classExactHeadMasks, static_cast<size_t>(classCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1053; }
    err = cudaMemcpy(g_cachedClassChildEqualityMasks, classChildEqualityMasks, static_cast<size_t>(classCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1054; }
    err = cudaMemcpy(g_cachedClassChildAtomBucketMasks, classChildAtomBucketMasks, static_cast<size_t>(classCount) * 4 * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1055; }
    err = cudaMemcpy(g_cachedClassChildConstraintMasks, classChildConstraintMasks, static_cast<size_t>(classCount) * 4 * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1056; }
    err = cudaMemcpy(g_cachedClassChildReferenceBloomMasks, classChildReferenceBloomMasks, static_cast<size_t>(classCount) * 4 * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeNodeRuleSnapshotCache(); return 1057; }

    return 0;
}

__global__ void batchMseKernel(
    const double* predictions,
    const double* targets,
    int samples,
    int candidates,
    double* results)
{
    int candidate = blockIdx.x;
    int tid = threadIdx.x;
    int stride = blockDim.x;

    double partial = 0.0;
    int base = candidate * samples;
    for (int i = tid; i < samples; i += stride)
    {
        double diff = predictions[base + i] - targets[i];
        partial += diff * diff;
    }

    __shared__ double shared[256];
    shared[tid] = partial;
    __syncthreads();

    for (int offset = blockDim.x / 2; offset > 0; offset >>= 1)
    {
        if (tid < offset)
        {
            shared[tid] += shared[tid + offset];
        }
        __syncthreads();
    }

    if (tid == 0)
    {
        results[candidate] = shared[0] / (samples > 0 ? samples : 1);
    }
}

extern "C" __declspec(dllexport) int cobra_batch_mse_scores(
    const double* hostPredictions,
    const double* hostTargets,
    int samples,
    int candidates,
    double* hostResults)
{
    if (hostPredictions == nullptr || hostTargets == nullptr || hostResults == nullptr)
    {
        return 1;
    }

    if (samples <= 0 || candidates <= 0)
    {
        return 2;
    }

    double* devicePredictions = nullptr;
    double* deviceTargets = nullptr;
    double* deviceResults = nullptr;

    size_t predictionBytes = static_cast<size_t>(samples) * static_cast<size_t>(candidates) * sizeof(double);
    size_t targetBytes = static_cast<size_t>(samples) * sizeof(double);
    size_t resultBytes = static_cast<size_t>(candidates) * sizeof(double);

    cudaError_t err = cudaMalloc(&devicePredictions, predictionBytes);
    if (err != cudaSuccess) return 10;

    err = cudaMalloc(&deviceTargets, targetBytes);
    if (err != cudaSuccess)
    {
        cudaFree(devicePredictions);
        return 11;
    }

    err = cudaMalloc(&deviceResults, resultBytes);
    if (err != cudaSuccess)
    {
        cudaFree(devicePredictions);
        cudaFree(deviceTargets);
        return 12;
    }

    err = cudaMemcpy(devicePredictions, hostPredictions, predictionBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess)
    {
        cudaFree(devicePredictions);
        cudaFree(deviceTargets);
        cudaFree(deviceResults);
        return 13;
    }

    err = cudaMemcpy(deviceTargets, hostTargets, targetBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess)
    {
        cudaFree(devicePredictions);
        cudaFree(deviceTargets);
        cudaFree(deviceResults);
        return 14;
    }

    batchMseKernel<<<candidates, 256>>>(devicePredictions, deviceTargets, samples, candidates, deviceResults);
    err = cudaGetLastError();
    if (err != cudaSuccess)
    {
        cudaFree(devicePredictions);
        cudaFree(deviceTargets);
        cudaFree(deviceResults);
        return 15;
    }

    err = cudaDeviceSynchronize();
    if (err != cudaSuccess)
    {
        cudaFree(devicePredictions);
        cudaFree(deviceTargets);
        cudaFree(deviceResults);
        return 16;
    }

    err = cudaMemcpy(hostResults, deviceResults, resultBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess)
    {
        cudaFree(devicePredictions);
        cudaFree(deviceTargets);
        cudaFree(deviceResults);
        return 17;
    }

    cudaFree(devicePredictions);
    cudaFree(deviceTargets);
    cudaFree(deviceResults);
    return 0;
}

__global__ void scoreRegionsKernel(
    const int* familyCodes,
    const int* nodeCounts,
    const int* boundaryCounts,
    double* benefitScores,
    double* conflictScores,
    int regionCount)
{
    int region = blockIdx.x * blockDim.x + threadIdx.x;
    if (region >= regionCount)
    {
        return;
    }

    double benefit = 0.0;
    double conflict = 0.0;

    switch (familyCodes[region])
    {
        case 1: // SharedSink
            benefit = 8.0;
            conflict = 3.0;
            break;
        case 2: // LeftFactorPack
            benefit = 4.0;
            conflict = 2.0;
            break;
        case 3: // RightFactorPack
            benefit = 4.0;
            conflict = 2.0;
            break;
        case 4: // BilinearOverlap
            benefit = 7.0;
            conflict = 5.0;
            break;
        case 5: // ResidualCoreBundle
            benefit = 6.0;
            conflict = 4.0;
            break;
        case 6: // TransposeBoundaryCore
            benefit = 6.0;
            conflict = 2.0;
            break;
        default:
            benefit = 0.0;
            conflict = 0.0;
            break;
    }

    int nodeCount = nodeCounts[region];
    int boundaryCount = boundaryCounts[region];

    benefit += nodeCount < 4 ? static_cast<double>(nodeCount) : 4.0;
    conflict += boundaryCount > 2 ? static_cast<double>(boundaryCount - 2) : 0.0;

    benefitScores[region] = benefit;
    conflictScores[region] = conflict;
}

extern "C" __declspec(dllexport) int cobra_score_regions(
    const int* familyCodes,
    const int* nodeCounts,
    const int* boundaryCounts,
    int regionCount,
    double* hostBenefitScores,
    double* hostConflictScores)
{
    if (familyCodes == nullptr || nodeCounts == nullptr || boundaryCounts == nullptr ||
        hostBenefitScores == nullptr || hostConflictScores == nullptr)
    {
        return 20;
    }

    if (regionCount <= 0)
    {
        return 21;
    }

    int* deviceFamilyCodes = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceBoundaryCounts = nullptr;
    double* deviceBenefitScores = nullptr;
    double* deviceConflictScores = nullptr;

    size_t intBytes = static_cast<size_t>(regionCount) * sizeof(int);
    size_t scoreBytes = static_cast<size_t>(regionCount) * sizeof(double);

    cudaError_t err = cudaMalloc(&deviceFamilyCodes, intBytes);
    if (err != cudaSuccess) return 30;

    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceFamilyCodes);
        return 31;
    }

    err = cudaMalloc(&deviceBoundaryCounts, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceFamilyCodes);
        cudaFree(deviceNodeCounts);
        return 32;
    }

    err = cudaMalloc(&deviceBenefitScores, scoreBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceFamilyCodes);
        cudaFree(deviceNodeCounts);
        cudaFree(deviceBoundaryCounts);
        return 33;
    }

    err = cudaMalloc(&deviceConflictScores, scoreBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceFamilyCodes);
        cudaFree(deviceNodeCounts);
        cudaFree(deviceBoundaryCounts);
        cudaFree(deviceBenefitScores);
        return 34;
    }

    int blockSize = 128;
    int gridSize = (regionCount + blockSize - 1) / blockSize;

    err = cudaMemcpy(deviceFamilyCodes, familyCodes, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_35;
    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_36;
    err = cudaMemcpy(deviceBoundaryCounts, boundaryCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_37;

    scoreRegionsKernel<<<gridSize, blockSize>>>(
        deviceFamilyCodes,
        deviceNodeCounts,
        deviceBoundaryCounts,
        deviceBenefitScores,
        deviceConflictScores,
        regionCount);

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_38;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_39;

    err = cudaMemcpy(hostBenefitScores, deviceBenefitScores, scoreBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_40;
    err = cudaMemcpy(hostConflictScores, deviceConflictScores, scoreBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_41;

    cudaFree(deviceFamilyCodes);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    return 0;

cleanup_error_41:
    cudaFree(deviceFamilyCodes);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    return 41;
cleanup_error_40:
    cudaFree(deviceFamilyCodes);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    return 40;
cleanup_error_39:
    cudaFree(deviceFamilyCodes);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    return 39;
cleanup_error_38:
    cudaFree(deviceFamilyCodes);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    return 38;
cleanup_error_37:
    cudaFree(deviceFamilyCodes);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    return 37;
cleanup_error_36:
    cudaFree(deviceFamilyCodes);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    return 36;
cleanup_error_35:
    cudaFree(deviceFamilyCodes);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    return 35;
}

__global__ void scoreMatchesKernel(
    const int* hotFlags,
    const int* boundaryFlags,
    const int* suppressedFlags,
    int matchCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= matchCount)
    {
        return;
    }

    int score = 0;
    score += hotFlags[index] != 0 ? 1000 : 0;
    score += boundaryFlags[index] != 0 ? 100 : 0;
    score -= suppressedFlags[index] != 0 ? 1000 : 0;
    scores[index] = score;
}

extern "C" __declspec(dllexport) int cobra_score_matches(
    const int* hotFlags,
    const int* boundaryFlags,
    const int* suppressedFlags,
    int matchCount,
    int* hostScores)
{
    if (hotFlags == nullptr || boundaryFlags == nullptr || suppressedFlags == nullptr || hostScores == nullptr)
    {
        return 50;
    }

    if (matchCount <= 0)
    {
        return 51;
    }

    int* deviceHotFlags = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceSuppressedFlags = nullptr;
    int* deviceScores = nullptr;

    size_t intBytes = static_cast<size_t>(matchCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceHotFlags, intBytes);
    if (err != cudaSuccess) return 60;

    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceHotFlags);
        return 61;
    }

    err = cudaMalloc(&deviceSuppressedFlags, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceHotFlags);
        cudaFree(deviceBoundaryFlags);
        return 62;
    }

    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceHotFlags);
        cudaFree(deviceBoundaryFlags);
        cudaFree(deviceSuppressedFlags);
        return 63;
    }

    int blockSize = 128;
    int gridSize = (matchCount + blockSize - 1) / blockSize;

    err = cudaMemcpy(deviceHotFlags, hotFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_64;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_65;
    err = cudaMemcpy(deviceSuppressedFlags, suppressedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_66;

    scoreMatchesKernel<<<gridSize, blockSize>>>(
        deviceHotFlags,
        deviceBoundaryFlags,
        deviceSuppressedFlags,
        matchCount,
        deviceScores);

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_67;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_68;

    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_69;

    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceScores);
    return 0;

cleanup_error_69:
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceScores);
    return 69;
cleanup_error_68:
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceScores);
    return 68;
cleanup_error_67:
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceScores);
    return 67;
cleanup_error_66:
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceScores);
    return 66;
cleanup_error_65:
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceScores);
    return 65;
cleanup_error_64:
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceScores);
    return 64;
}

__global__ void scoreClassesKernel(
    const int* nodeCounts,
    const int* generations,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    int score = 0;
    score += generations[index] * 4;
    score += nodeCounts[index];
    score += hotFlags[index] != 0 ? 1000 : 0;
    score += boundaryFlags[index] != 0 ? 100 : 0;
    score -= residualFlags[index] != 0 ? 25 : 0;
    scores[index] = score;
}

extern "C" __declspec(dllexport) int cobra_score_classes(
    const int* nodeCounts,
    const int* generations,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    int classCount,
    int* hostScores)
{
    if (nodeCounts == nullptr || generations == nullptr || hotFlags == nullptr ||
        boundaryFlags == nullptr || residualFlags == nullptr || hostScores == nullptr)
    {
        return 70;
    }

    if (classCount <= 0)
    {
        return 71;
    }

    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceHotFlags = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceResidualFlags = nullptr;
    int* deviceScores = nullptr;

    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) return 80;

    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        return 81;
    }

    err = cudaMalloc(&deviceHotFlags, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        cudaFree(deviceGenerations);
        return 82;
    }

    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        cudaFree(deviceGenerations);
        cudaFree(deviceHotFlags);
        return 83;
    }

    err = cudaMalloc(&deviceResidualFlags, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        cudaFree(deviceGenerations);
        cudaFree(deviceHotFlags);
        cudaFree(deviceBoundaryFlags);
        return 84;
    }

    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        cudaFree(deviceGenerations);
        cudaFree(deviceHotFlags);
        cudaFree(deviceBoundaryFlags);
        cudaFree(deviceResidualFlags);
        return 85;
    }

    int blockSize = 128;
    int gridSize = (classCount + blockSize - 1) / blockSize;

    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_86;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_87;
    err = cudaMemcpy(deviceHotFlags, hotFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_88;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_89;
    err = cudaMemcpy(deviceResidualFlags, residualFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_90;

    scoreClassesKernel<<<gridSize, blockSize>>>(
        deviceNodeCounts,
        deviceGenerations,
        deviceHotFlags,
        deviceBoundaryFlags,
        deviceResidualFlags,
        classCount,
        deviceScores);

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_91;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_92;

    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_93;

    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 0;

cleanup_error_93:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 93;
cleanup_error_92:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 92;
cleanup_error_91:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 91;
cleanup_error_90:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 90;
cleanup_error_89:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 89;
cleanup_error_88:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 88;
cleanup_error_87:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 87;
cleanup_error_86:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceScores);
    return 86;
}

__global__ void prepareUnionsKernel(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* normalizedLeft,
    int* normalizedRight,
    unsigned long long* pairKeys)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= pairCount)
    {
        return;
    }

    int left = leftIds[index];
    int right = rightIds[index];
    int minId = left < right ? left : right;
    int maxId = left < right ? right : left;
    normalizedLeft[index] = minId;
    normalizedRight[index] = maxId;
    pairKeys[index] = (static_cast<unsigned long long>(static_cast<unsigned int>(minId)) << 32) |
                      static_cast<unsigned int>(maxId);
}

extern "C" __declspec(dllexport) int cobra_prepare_unions(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* hostNormalizedLeft,
    int* hostNormalizedRight,
    unsigned long long* hostPairKeys)
{
    if (leftIds == nullptr || rightIds == nullptr || hostNormalizedLeft == nullptr ||
        hostNormalizedRight == nullptr || hostPairKeys == nullptr)
    {
        return 100;
    }

    if (pairCount <= 0)
    {
        return 101;
    }

    int* deviceLeftIds = nullptr;
    int* deviceRightIds = nullptr;
    int* deviceNormalizedLeft = nullptr;
    int* deviceNormalizedRight = nullptr;
    unsigned long long* devicePairKeys = nullptr;

    size_t intBytes = static_cast<size_t>(pairCount) * sizeof(int);
    size_t keyBytes = static_cast<size_t>(pairCount) * sizeof(unsigned long long);
    cudaError_t err = cudaMalloc(&deviceLeftIds, intBytes);
    if (err != cudaSuccess) return 110;

    err = cudaMalloc(&deviceRightIds, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceLeftIds);
        return 111;
    }

    err = cudaMalloc(&deviceNormalizedLeft, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        return 112;
    }

    err = cudaMalloc(&deviceNormalizedRight, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        cudaFree(deviceNormalizedLeft);
        return 113;
    }

    err = cudaMalloc(&devicePairKeys, keyBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        cudaFree(deviceNormalizedLeft);
        cudaFree(deviceNormalizedRight);
        return 114;
    }

    int blockSize = 128;
    int gridSize = (pairCount + blockSize - 1) / blockSize;

    err = cudaMemcpy(deviceLeftIds, leftIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_115;
    err = cudaMemcpy(deviceRightIds, rightIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_116;

    prepareUnionsKernel<<<gridSize, blockSize>>>(
        deviceLeftIds,
        deviceRightIds,
        pairCount,
        deviceNormalizedLeft,
        deviceNormalizedRight,
        devicePairKeys);

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_117;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_118;

    err = cudaMemcpy(hostNormalizedLeft, deviceNormalizedLeft, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_119;
    err = cudaMemcpy(hostNormalizedRight, deviceNormalizedRight, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_120;
    err = cudaMemcpy(hostPairKeys, devicePairKeys, keyBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_121;

    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceNormalizedLeft);
    cudaFree(deviceNormalizedRight);
    cudaFree(devicePairKeys);
    return 0;

cleanup_error_121:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceNormalizedLeft);
    cudaFree(deviceNormalizedRight);
    cudaFree(devicePairKeys);
    return 121;
cleanup_error_120:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceNormalizedLeft);
    cudaFree(deviceNormalizedRight);
    cudaFree(devicePairKeys);
    return 120;
cleanup_error_119:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceNormalizedLeft);
    cudaFree(deviceNormalizedRight);
    cudaFree(devicePairKeys);
    return 119;
cleanup_error_118:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceNormalizedLeft);
    cudaFree(deviceNormalizedRight);
    cudaFree(devicePairKeys);
    return 118;
cleanup_error_117:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceNormalizedLeft);
    cudaFree(deviceNormalizedRight);
    cudaFree(devicePairKeys);
    return 117;
cleanup_error_116:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceNormalizedLeft);
    cudaFree(deviceNormalizedRight);
    cudaFree(devicePairKeys);
    return 116;
cleanup_error_115:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceNormalizedLeft);
    cudaFree(deviceNormalizedRight);
    cudaFree(devicePairKeys);
    return 115;
}

__global__ void scoreRuleCompatibilityKernel(
    const int* classMasks,
    const int* ruleMasks,
    int classCount,
    int ruleCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    int total = classCount * ruleCount;
    if (index >= total)
    {
        return;
    }

    int classIndex = index / ruleCount;
    int ruleIndex = index % ruleCount;
    int classMask = classMasks[classIndex];
    int ruleMask = ruleMasks[ruleIndex];
    scores[index] = (ruleMask == 0 || (classMask & ruleMask) != 0) ? 1 : 0;
}

extern "C" __declspec(dllexport) int cobra_score_rule_compatibility(
    const int* classMasks,
    const int* ruleMasks,
    int classCount,
    int ruleCount,
    int* hostScores)
{
    if (classMasks == nullptr || ruleMasks == nullptr || hostScores == nullptr)
    {
        return 130;
    }

    if (classCount <= 0 || ruleCount <= 0)
    {
        return 131;
    }

    int* deviceClassMasks = nullptr;
    int* deviceRuleMasks = nullptr;
    int* deviceScores = nullptr;

    size_t classBytes = static_cast<size_t>(classCount) * sizeof(int);
    size_t ruleBytes = static_cast<size_t>(ruleCount) * sizeof(int);
    size_t scoreBytes = static_cast<size_t>(classCount) * static_cast<size_t>(ruleCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceClassMasks, classBytes);
    if (err != cudaSuccess) return 140;

    err = cudaMalloc(&deviceRuleMasks, ruleBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceClassMasks);
        return 141;
    }

    err = cudaMalloc(&deviceScores, scoreBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceClassMasks);
        cudaFree(deviceRuleMasks);
        return 142;
    }

    err = cudaMemcpy(deviceClassMasks, classMasks, classBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_143;
    err = cudaMemcpy(deviceRuleMasks, ruleMasks, ruleBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_144;

    {
        int total = classCount * ruleCount;
        int blockSize = 128;
        int gridSize = (total + blockSize - 1) / blockSize;
        scoreRuleCompatibilityKernel<<<gridSize, blockSize>>>(
            deviceClassMasks,
            deviceRuleMasks,
            classCount,
            ruleCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_145;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_146;

    err = cudaMemcpy(hostScores, deviceScores, scoreBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_147;

    cudaFree(deviceClassMasks);
    cudaFree(deviceRuleMasks);
    cudaFree(deviceScores);
    return 0;

cleanup_error_147:
    cudaFree(deviceClassMasks);
    cudaFree(deviceRuleMasks);
    cudaFree(deviceScores);
    return 147;
cleanup_error_146:
    cudaFree(deviceClassMasks);
    cudaFree(deviceRuleMasks);
    cudaFree(deviceScores);
    return 146;
cleanup_error_145:
    cudaFree(deviceClassMasks);
    cudaFree(deviceRuleMasks);
    cudaFree(deviceScores);
    return 145;
cleanup_error_144:
    cudaFree(deviceClassMasks);
    cudaFree(deviceRuleMasks);
    cudaFree(deviceScores);
    return 144;
cleanup_error_143:
    cudaFree(deviceClassMasks);
    cudaFree(deviceRuleMasks);
    cudaFree(deviceScores);
    return 143;
}

__global__ void scoreRebuildClassesKernel(
    const int* nodeCounts,
    const int* generations,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] = (generations[index] * 16) + nodeCounts[index];
}

extern "C" __declspec(dllexport) int cobra_score_rebuild_classes(
    const int* nodeCounts,
    const int* generations,
    int classCount,
    int* hostScores)
{
    if (nodeCounts == nullptr || generations == nullptr || hostScores == nullptr)
    {
        return 150;
    }

    if (classCount <= 0)
    {
        return 151;
    }

    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceScores = nullptr;

    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) return 160;

    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        return 161;
    }

    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        cudaFree(deviceGenerations);
        return 162;
    }

    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_163;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_164;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreRebuildClassesKernel<<<gridSize, blockSize>>>(
            deviceNodeCounts,
            deviceGenerations,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_165;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_166;

    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_167;

    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 0;

cleanup_error_167:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 167;
cleanup_error_166:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 166;
cleanup_error_165:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 165;
cleanup_error_164:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 164;
cleanup_error_163:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 163;
}

__global__ void scoreAnalysisClassesKernel(
    const int* nodeCounts,
    const int* generations,
    const int* unresolvedFlags,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] = (unresolvedFlags[index] * 1000) + (generations[index] * 8) + nodeCounts[index];
}

extern "C" __declspec(dllexport) int cobra_score_analysis_classes(
    const int* nodeCounts,
    const int* generations,
    const int* unresolvedFlags,
    int classCount,
    int* hostScores)
{
    if (nodeCounts == nullptr || generations == nullptr || unresolvedFlags == nullptr || hostScores == nullptr)
    {
        return 170;
    }

    if (classCount <= 0)
    {
        return 171;
    }

    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceUnresolvedFlags = nullptr;
    int* deviceScores = nullptr;

    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) return 180;

    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        return 181;
    }

    err = cudaMalloc(&deviceUnresolvedFlags, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        cudaFree(deviceGenerations);
        return 182;
    }

    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeCounts);
        cudaFree(deviceGenerations);
        cudaFree(deviceUnresolvedFlags);
        return 183;
    }

    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_184;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_185;
    err = cudaMemcpy(deviceUnresolvedFlags, unresolvedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_186;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreAnalysisClassesKernel<<<gridSize, blockSize>>>(
            deviceNodeCounts,
            deviceGenerations,
            deviceUnresolvedFlags,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_187;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_188;

    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_189;

    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceScores);
    return 0;

cleanup_error_189:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceScores);
    return 189;
cleanup_error_188:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceScores);
    return 188;
cleanup_error_187:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceScores);
    return 187;
cleanup_error_186:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceScores);
    return 186;
cleanup_error_185:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceScores);
    return 185;
cleanup_error_184:
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceScores);
    return 184;
}

__device__ int findRootDevice(const int* parents, int parentCount, int id)
{
    int current = id;
    int guard = 0;
    while (current >= 0 && current < parentCount && parents[current] != current && guard < parentCount)
    {
        current = parents[current];
        guard++;
    }

    return (current >= 0 && current < parentCount) ? current : id;
}

__global__ void resolveUnionRootsKernel(
    const int* parents,
    int parentCount,
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* resolvedLeft,
    int* resolvedRight,
    unsigned long long* pairKeys)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= pairCount)
    {
        return;
    }

    int leftRoot = findRootDevice(parents, parentCount, leftIds[index]);
    int rightRoot = findRootDevice(parents, parentCount, rightIds[index]);
    int minId = leftRoot < rightRoot ? leftRoot : rightRoot;
    int maxId = leftRoot < rightRoot ? rightRoot : leftRoot;
    resolvedLeft[index] = minId;
    resolvedRight[index] = maxId;
    pairKeys[index] = (static_cast<unsigned long long>(static_cast<unsigned int>(minId)) << 32) |
                      static_cast<unsigned int>(maxId);
}

extern "C" __declspec(dllexport) int cobra_resolve_union_roots(
    const int* parents,
    int parentCount,
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* hostResolvedLeft,
    int* hostResolvedRight,
    unsigned long long* hostPairKeys)
{
    if (parents == nullptr || leftIds == nullptr || rightIds == nullptr ||
        hostResolvedLeft == nullptr || hostResolvedRight == nullptr || hostPairKeys == nullptr)
    {
        return 190;
    }

    if (parentCount <= 0 || pairCount <= 0)
    {
        return 191;
    }

    int* deviceParents = nullptr;
    int* deviceLeftIds = nullptr;
    int* deviceRightIds = nullptr;
    int* deviceResolvedLeft = nullptr;
    int* deviceResolvedRight = nullptr;
    unsigned long long* devicePairKeys = nullptr;

    size_t parentBytes = static_cast<size_t>(parentCount) * sizeof(int);
    size_t pairIntBytes = static_cast<size_t>(pairCount) * sizeof(int);
    size_t pairKeyBytes = static_cast<size_t>(pairCount) * sizeof(unsigned long long);
    cudaError_t err = cudaMalloc(&deviceParents, parentBytes);
    if (err != cudaSuccess) return 200;

    err = cudaMalloc(&deviceLeftIds, pairIntBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        return 201;
    }

    err = cudaMalloc(&deviceRightIds, pairIntBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        cudaFree(deviceLeftIds);
        return 202;
    }

    err = cudaMalloc(&deviceResolvedLeft, pairIntBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        return 203;
    }

    err = cudaMalloc(&deviceResolvedRight, pairIntBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        cudaFree(deviceResolvedLeft);
        return 204;
    }

    err = cudaMalloc(&devicePairKeys, pairKeyBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        cudaFree(deviceResolvedLeft);
        cudaFree(deviceResolvedRight);
        return 205;
    }

    err = cudaMemcpy(deviceParents, parents, parentBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_206;
    err = cudaMemcpy(deviceLeftIds, leftIds, pairIntBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_207;
    err = cudaMemcpy(deviceRightIds, rightIds, pairIntBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_208;

    {
        int blockSize = 128;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        resolveUnionRootsKernel<<<gridSize, blockSize>>>(
            deviceParents,
            parentCount,
            deviceLeftIds,
            deviceRightIds,
            pairCount,
            deviceResolvedLeft,
            deviceResolvedRight,
            devicePairKeys);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_209;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_210;

    err = cudaMemcpy(hostResolvedLeft, deviceResolvedLeft, pairIntBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_211;
    err = cudaMemcpy(hostResolvedRight, deviceResolvedRight, pairIntBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_212;
    err = cudaMemcpy(hostPairKeys, devicePairKeys, pairKeyBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_213;

    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 0;

cleanup_error_213:
    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 213;
cleanup_error_212:
    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 212;
cleanup_error_211:
    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 211;
cleanup_error_210:
    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 210;
cleanup_error_209:
    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 209;
cleanup_error_208:
    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 208;
cleanup_error_207:
    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 207;
cleanup_error_206:
    cudaFree(deviceParents);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 206;
}

extern "C" __declspec(dllexport) int cobra_resolve_union_roots_cached(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* hostResolvedLeft,
    int* hostResolvedRight,
    unsigned long long* hostPairKeys)
{
    if (g_cachedParents == nullptr || g_cachedParentCount <= 0 || leftIds == nullptr || rightIds == nullptr ||
        hostResolvedLeft == nullptr || hostResolvedRight == nullptr || hostPairKeys == nullptr)
    {
        return 1020;
    }

    if (pairCount <= 0)
    {
        return 1021;
    }

    int* deviceLeftIds = nullptr;
    int* deviceRightIds = nullptr;
    int* deviceResolvedLeft = nullptr;
    int* deviceResolvedRight = nullptr;
    unsigned long long* devicePairKeys = nullptr;

    size_t pairIntBytes = static_cast<size_t>(pairCount) * sizeof(int);
    size_t pairKeyBytes = static_cast<size_t>(pairCount) * sizeof(unsigned long long);
    cudaError_t err = cudaMalloc(&deviceLeftIds, pairIntBytes);
    if (err != cudaSuccess) return 1022;
    err = cudaMalloc(&deviceRightIds, pairIntBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftIds); return 1023; }
    err = cudaMalloc(&deviceResolvedLeft, pairIntBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftIds); cudaFree(deviceRightIds); return 1024; }
    err = cudaMalloc(&deviceResolvedRight, pairIntBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftIds); cudaFree(deviceRightIds); cudaFree(deviceResolvedLeft); return 1025; }
    err = cudaMalloc(&devicePairKeys, pairKeyBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftIds); cudaFree(deviceRightIds); cudaFree(deviceResolvedLeft); cudaFree(deviceResolvedRight); return 1026; }

    err = cudaMemcpy(deviceLeftIds, leftIds, pairIntBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1027;
    err = cudaMemcpy(deviceRightIds, rightIds, pairIntBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1028;

    {
        int blockSize = 128;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        resolveUnionRootsKernel<<<gridSize, blockSize>>>(
            g_cachedParents,
            g_cachedParentCount,
            deviceLeftIds,
            deviceRightIds,
            pairCount,
            deviceResolvedLeft,
            deviceResolvedRight,
            devicePairKeys);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_1029;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_1030;
    err = cudaMemcpy(hostResolvedLeft, deviceResolvedLeft, pairIntBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_1031;
    err = cudaMemcpy(hostResolvedRight, deviceResolvedRight, pairIntBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_1032;
    err = cudaMemcpy(hostPairKeys, devicePairKeys, pairKeyBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_1033;

    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 0;

cleanup_error_1033:
cleanup_error_1032:
cleanup_error_1031:
cleanup_error_1030:
cleanup_error_1029:
cleanup_error_1028:
cleanup_error_1027:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceResolvedLeft);
    cudaFree(deviceResolvedRight);
    cudaFree(devicePairKeys);
    return 1033;
}

__global__ void markRepairCandidatesKernel(
    const int* parents,
    int parentCount,
    const int* childStarts,
    const int* childCounts,
    const int* childIds,
    int nodeCount,
    int* dirtyFlags)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= nodeCount)
    {
        return;
    }

    int start = childStarts[index];
    int count = childCounts[index];
    int dirty = 0;
    for (int offset = 0; offset < count; offset++)
    {
        int childId = childIds[start + offset];
        int childRoot = findRootDevice(parents, parentCount, childId);
        if (childRoot != childId)
        {
            dirty = 1;
            break;
        }
    }

    dirtyFlags[index] = dirty;
}

extern "C" __declspec(dllexport) int cobra_mark_repair_candidates(
    const int* parents,
    int parentCount,
    const int* childStarts,
    const int* childCounts,
    const int* childIds,
    int nodeCount,
    int* hostDirtyFlags)
{
    if (parents == nullptr || childStarts == nullptr || childCounts == nullptr || childIds == nullptr || hostDirtyFlags == nullptr)
    {
        return 220;
    }

    if (parentCount <= 0 || nodeCount <= 0)
    {
        return 221;
    }

    int* deviceParents = nullptr;
    int* deviceChildStarts = nullptr;
    int* deviceChildCounts = nullptr;
    int* deviceChildIds = nullptr;
    int* deviceDirtyFlags = nullptr;

    size_t parentBytes = static_cast<size_t>(parentCount) * sizeof(int);
    size_t nodeIntBytes = static_cast<size_t>(nodeCount) * sizeof(int);
    size_t childIdBytes = static_cast<size_t>(childStarts[nodeCount - 1] + childCounts[nodeCount - 1]) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceParents, parentBytes);
    if (err != cudaSuccess) return 230;

    err = cudaMalloc(&deviceChildStarts, nodeIntBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        return 231;
    }

    err = cudaMalloc(&deviceChildCounts, nodeIntBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        cudaFree(deviceChildStarts);
        return 232;
    }

    err = cudaMalloc(&deviceChildIds, childIdBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        cudaFree(deviceChildStarts);
        cudaFree(deviceChildCounts);
        return 233;
    }

    err = cudaMalloc(&deviceDirtyFlags, nodeIntBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceParents);
        cudaFree(deviceChildStarts);
        cudaFree(deviceChildCounts);
        cudaFree(deviceChildIds);
        return 234;
    }

    err = cudaMemcpy(deviceParents, parents, parentBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_235;
    err = cudaMemcpy(deviceChildStarts, childStarts, nodeIntBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_236;
    err = cudaMemcpy(deviceChildCounts, childCounts, nodeIntBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_237;
    err = cudaMemcpy(deviceChildIds, childIds, childIdBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_238;

    {
        int blockSize = 128;
        int gridSize = (nodeCount + blockSize - 1) / blockSize;
        markRepairCandidatesKernel<<<gridSize, blockSize>>>(
            deviceParents,
            parentCount,
            deviceChildStarts,
            deviceChildCounts,
            deviceChildIds,
            nodeCount,
            deviceDirtyFlags);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_239;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_240;

    err = cudaMemcpy(hostDirtyFlags, deviceDirtyFlags, nodeIntBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_241;

    cudaFree(deviceParents);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceDirtyFlags);
    return 0;

cleanup_error_241:
    cudaFree(deviceParents);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceDirtyFlags);
    return 241;
cleanup_error_240:
    cudaFree(deviceParents);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceDirtyFlags);
    return 240;
cleanup_error_239:
    cudaFree(deviceParents);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceDirtyFlags);
    return 239;
cleanup_error_238:
    cudaFree(deviceParents);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceDirtyFlags);
    return 238;
cleanup_error_237:
    cudaFree(deviceParents);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceDirtyFlags);
    return 237;
cleanup_error_236:
    cudaFree(deviceParents);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceDirtyFlags);
    return 236;
cleanup_error_235:
    cudaFree(deviceParents);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceDirtyFlags);
    return 235;
}

extern "C" __declspec(dllexport) int cobra_mark_repair_candidates_cached(
    int* hostDirtyFlags)
{
    if (g_cachedRepairParents == nullptr || g_cachedRepairChildStarts == nullptr || g_cachedRepairChildCounts == nullptr ||
        g_cachedRepairChildIds == nullptr || hostDirtyFlags == nullptr || g_cachedRepairParentCount <= 0 || g_cachedRepairNodeCount <= 0)
    {
        return 1040;
    }

    int* deviceDirtyFlags = nullptr;
    size_t nodeIntBytes = static_cast<size_t>(g_cachedRepairNodeCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceDirtyFlags, nodeIntBytes);
    if (err != cudaSuccess) return 1041;

    {
        int blockSize = 128;
        int gridSize = (g_cachedRepairNodeCount + blockSize - 1) / blockSize;
        markRepairCandidatesKernel<<<gridSize, blockSize>>>(
            g_cachedRepairParents,
            g_cachedRepairParentCount,
            g_cachedRepairChildStarts,
            g_cachedRepairChildCounts,
            g_cachedRepairChildIds,
            g_cachedRepairNodeCount,
            deviceDirtyFlags);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_1042;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_1043;
    err = cudaMemcpy(hostDirtyFlags, deviceDirtyFlags, nodeIntBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_1044;

    cudaFree(deviceDirtyFlags);
    return 0;

cleanup_error_1044:
cleanup_error_1043:
cleanup_error_1042:
    cudaFree(deviceDirtyFlags);
    return 1044;
}

extern "C" __declspec(dllexport) int cobra_score_union_members_cached(
    const int* memberIds,
    int memberCount,
    int* hostScores)
{
    if (g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0 ||
        memberIds == nullptr || hostScores == nullptr || memberCount <= 0)
    {
        return 1080;
    }

    int* deviceMemberIds = nullptr;
    int* deviceScores = nullptr;
    size_t memberBytes = static_cast<size_t>(memberCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceMemberIds, memberBytes);
    if (err != cudaSuccess) return 1081;
    err = cudaMalloc(&deviceScores, memberBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceMemberIds);
        return 1082;
    }

    err = cudaMemcpy(deviceMemberIds, memberIds, memberBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1083;

    {
        int blockSize = 128;
        int gridSize = (memberCount + blockSize - 1) / blockSize;
        scoreUnionMembersKernel<<<gridSize, blockSize>>>(
            deviceMemberIds,
            memberCount,
            g_cachedClassNodeCounts,
            g_cachedClassGenerations,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_1084;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_1085;
    err = cudaMemcpy(hostScores, deviceScores, memberBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_1086;

    cudaFree(deviceMemberIds);
    cudaFree(deviceScores);
    return 0;

cleanup_error_1086:
cleanup_error_1085:
cleanup_error_1084:
cleanup_error_1083:
    cudaFree(deviceMemberIds);
    cudaFree(deviceScores);
    return 1086;
}

extern "C" __declspec(dllexport) int cobra_score_rebuild_with_repair_cached(
    const int* repairCounts,
    int classCount,
    int* hostScores)
{
    if (g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount != classCount ||
        repairCounts == nullptr || hostScores == nullptr || classCount <= 0)
    {
        return 1060;
    }

    int* deviceRepairCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceRepairCounts, intBytes);
    if (err != cudaSuccess) return 1061;
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceRepairCounts); return 1062; }

    err = cudaMemcpy(deviceRepairCounts, repairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1063;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreRebuildWithRepairKernel<<<gridSize, blockSize>>>(
            g_cachedClassNodeCounts,
            g_cachedClassGenerations,
            deviceRepairCounts,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_1064;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_1065;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_1066;

    cudaFree(deviceRepairCounts);
    cudaFree(deviceScores);
    return 0;

cleanup_error_1066:
cleanup_error_1065:
cleanup_error_1064:
cleanup_error_1063:
    cudaFree(deviceRepairCounts);
    cudaFree(deviceScores);
    return 1066;
}

extern "C" __declspec(dllexport) int cobra_score_analysis_with_repair_cached(
    const int* unresolvedFlags,
    const int* repairCounts,
    int classCount,
    int* hostScores)
{
    if (g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount != classCount ||
        unresolvedFlags == nullptr || repairCounts == nullptr || hostScores == nullptr || classCount <= 0)
    {
        return 1070;
    }

    int* deviceUnresolvedFlags = nullptr;
    int* deviceRepairCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceUnresolvedFlags, intBytes);
    if (err != cudaSuccess) return 1071;
    err = cudaMalloc(&deviceRepairCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceUnresolvedFlags); return 1072; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); return 1073; }

    err = cudaMemcpy(deviceUnresolvedFlags, unresolvedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1074;
    err = cudaMemcpy(deviceRepairCounts, repairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_1075;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreAnalysisWithRepairKernel<<<gridSize, blockSize>>>(
            g_cachedClassNodeCounts,
            g_cachedClassGenerations,
            deviceUnresolvedFlags,
            deviceRepairCounts,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_1076;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_1077;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_1078;

    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceRepairCounts);
    cudaFree(deviceScores);
    return 0;

cleanup_error_1078:
cleanup_error_1077:
cleanup_error_1076:
cleanup_error_1075:
cleanup_error_1074:
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceRepairCounts);
    cudaFree(deviceScores);
    return 1078;
}

__global__ void initUnionLabelsKernel(
    int* labels,
    int maxId)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index > maxId)
    {
        return;
    }

    labels[index] = index;
}

__global__ void relaxUnionLabelsKernel(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* labels,
    int* changed)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= pairCount)
    {
        return;
    }

    int left = leftIds[index];
    int right = rightIds[index];
    int leftLabel = labels[left];
    int rightLabel = labels[right];
    int minLabel = leftLabel < rightLabel ? leftLabel : rightLabel;
    if (minLabel < leftLabel)
    {
        atomicMin(&labels[left], minLabel);
        *changed = 1;
    }
    if (minLabel < rightLabel)
    {
        atomicMin(&labels[right], minLabel);
        *changed = 1;
    }
}

__global__ void compressUnionLabelsKernel(
    int* labels,
    int maxId)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index > maxId)
    {
        return;
    }

    int current = labels[index];
    int guard = 0;
    while (current != labels[current] && guard <= maxId)
    {
        current = labels[current];
        guard++;
    }

    labels[index] = current;
}

__global__ void writeUnionGroupKeysKernel(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    const int* labels,
    int* groupKeys)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= pairCount)
    {
        return;
    }

    int left = leftIds[index];
    int right = rightIds[index];
    int leftLabel = labels[left];
    int rightLabel = labels[right];
    groupKeys[index] = leftLabel < rightLabel ? leftLabel : rightLabel;
}

extern "C" __declspec(dllexport) int cobra_group_unions(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* hostGroupKeys)
{
    if (leftIds == nullptr || rightIds == nullptr || hostGroupKeys == nullptr)
    {
        return 250;
    }

    if (pairCount <= 0)
    {
        return 251;
    }

    int maxId = 0;
    for (int i = 0; i < pairCount; i++)
    {
        maxId = leftIds[i] > maxId ? leftIds[i] : maxId;
        maxId = rightIds[i] > maxId ? rightIds[i] : maxId;
    }

    int* deviceLeftIds = nullptr;
    int* deviceRightIds = nullptr;
    int* deviceLabels = nullptr;
    int* deviceGroupKeys = nullptr;
    int* deviceChanged = nullptr;

    size_t pairBytes = static_cast<size_t>(pairCount) * sizeof(int);
    size_t labelBytes = static_cast<size_t>(maxId + 1) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceLeftIds, pairBytes);
    if (err != cudaSuccess) return 260;

    err = cudaMalloc(&deviceRightIds, pairBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceLeftIds);
        return 261;
    }

    err = cudaMalloc(&deviceLabels, labelBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        return 262;
    }

    err = cudaMalloc(&deviceGroupKeys, pairBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        cudaFree(deviceLabels);
        return 263;
    }

    err = cudaMalloc(&deviceChanged, sizeof(int));
    if (err != cudaSuccess)
    {
        cudaFree(deviceLeftIds);
        cudaFree(deviceRightIds);
        cudaFree(deviceLabels);
        cudaFree(deviceGroupKeys);
        return 264;
    }

    err = cudaMemcpy(deviceLeftIds, leftIds, pairBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_265;
    err = cudaMemcpy(deviceRightIds, rightIds, pairBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_266;

    {
        int blockSize = 128;
        int labelGrid = ((maxId + 1) + blockSize - 1) / blockSize;
        int pairGrid = (pairCount + blockSize - 1) / blockSize;
        initUnionLabelsKernel<<<labelGrid, blockSize>>>(deviceLabels, maxId);
        err = cudaGetLastError();
        if (err != cudaSuccess) goto cleanup_error_267;
        err = cudaDeviceSynchronize();
        if (err != cudaSuccess) goto cleanup_error_268;

        for (int iteration = 0; iteration < 16; iteration++)
        {
            int hostChanged = 0;
            err = cudaMemcpy(deviceChanged, &hostChanged, sizeof(int), cudaMemcpyHostToDevice);
            if (err != cudaSuccess) goto cleanup_error_269;

            relaxUnionLabelsKernel<<<pairGrid, blockSize>>>(
                deviceLeftIds,
                deviceRightIds,
                pairCount,
                deviceLabels,
                deviceChanged);
            err = cudaGetLastError();
            if (err != cudaSuccess) goto cleanup_error_270;
            err = cudaDeviceSynchronize();
            if (err != cudaSuccess) goto cleanup_error_271;

            compressUnionLabelsKernel<<<labelGrid, blockSize>>>(deviceLabels, maxId);
            err = cudaGetLastError();
            if (err != cudaSuccess) goto cleanup_error_272;
            err = cudaDeviceSynchronize();
            if (err != cudaSuccess) goto cleanup_error_273;

            err = cudaMemcpy(&hostChanged, deviceChanged, sizeof(int), cudaMemcpyDeviceToHost);
            if (err != cudaSuccess) goto cleanup_error_274;
            if (hostChanged == 0)
            {
                break;
            }
        }

        writeUnionGroupKeysKernel<<<pairGrid, blockSize>>>(
            deviceLeftIds,
            deviceRightIds,
            pairCount,
            deviceLabels,
            deviceGroupKeys);
        err = cudaGetLastError();
        if (err != cudaSuccess) goto cleanup_error_275;
        err = cudaDeviceSynchronize();
        if (err != cudaSuccess) goto cleanup_error_276;
    }

    err = cudaMemcpy(hostGroupKeys, deviceGroupKeys, pairBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_277;

    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 0;

cleanup_error_277:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 277;
cleanup_error_276:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 276;
cleanup_error_275:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 275;
cleanup_error_274:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 274;
cleanup_error_273:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 273;
cleanup_error_272:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 272;
cleanup_error_271:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 271;
cleanup_error_270:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 270;
cleanup_error_269:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 269;
cleanup_error_268:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 268;
cleanup_error_267:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 267;
cleanup_error_266:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 266;
cleanup_error_265:
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceLabels);
    cudaFree(deviceGroupKeys);
    cudaFree(deviceChanged);
    return 265;
}

__global__ void scoreUnionMembersKernel(
    const int* memberIds,
    int memberCount,
    const int* nodeCounts,
    const int* generations,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= memberCount)
    {
        return;
    }

    int memberId = memberIds[index];
    scores[index] = (generations[memberId] * 4) + nodeCounts[memberId];
}

extern "C" __declspec(dllexport) int cobra_score_union_members(
    const int* memberIds,
    int memberCount,
    const int* nodeCounts,
    const int* generations,
    int* hostScores)
{
    if (memberIds == nullptr || nodeCounts == nullptr || generations == nullptr || hostScores == nullptr)
    {
        return 278;
    }

    if (memberCount <= 0)
    {
        return 279;
    }

    int maxMemberId = 0;
    for (int i = 0; i < memberCount; i++)
    {
        if (memberIds[i] > maxMemberId)
        {
            maxMemberId = memberIds[i];
        }
    }

    size_t memberBytes = static_cast<size_t>(memberCount) * sizeof(int);
    size_t classBytes = static_cast<size_t>(maxMemberId + 1) * sizeof(int);
    int* deviceMemberIds = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceScores = nullptr;

    cudaError_t err = cudaMalloc(&deviceMemberIds, memberBytes);
    if (err != cudaSuccess) return 280;
    err = cudaMalloc(&deviceNodeCounts, classBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceMemberIds);
        return 281;
    }
    err = cudaMalloc(&deviceGenerations, classBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceMemberIds);
        cudaFree(deviceNodeCounts);
        return 282;
    }
    err = cudaMalloc(&deviceScores, memberBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceMemberIds);
        cudaFree(deviceNodeCounts);
        cudaFree(deviceGenerations);
        return 283;
    }

    err = cudaMemcpy(deviceMemberIds, memberIds, memberBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_284;
    err = cudaMemcpy(deviceNodeCounts, nodeCounts, classBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_285;
    err = cudaMemcpy(deviceGenerations, generations, classBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_286;

    {
        int blockSize = 128;
        int gridSize = (memberCount + blockSize - 1) / blockSize;
        scoreUnionMembersKernel<<<gridSize, blockSize>>>(
            deviceMemberIds,
            memberCount,
            deviceNodeCounts,
            deviceGenerations,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_287;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_288;
    err = cudaMemcpy(hostScores, deviceScores, memberBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_289;

    cudaFree(deviceMemberIds);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 0;

cleanup_error_289:
    cudaFree(deviceMemberIds);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 289;
cleanup_error_288:
    cudaFree(deviceMemberIds);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 288;
cleanup_error_287:
    cudaFree(deviceMemberIds);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 287;
cleanup_error_286:
    cudaFree(deviceMemberIds);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 286;
cleanup_error_285:
    cudaFree(deviceMemberIds);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 285;
cleanup_error_284:
    cudaFree(deviceMemberIds);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceScores);
    return 284;
}

__global__ void scoreNodeRuleCandidatesKernel(
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* nodeChildStarts,
    const int* nodeChildIds,
    const int* classConstraintMasks,
    const int* classHeadBucketMasks,
    const int* classExactHeadMasks,
    const int* classChildEqualityMasks,
    const int* classChildAtomBucketMasks,
    const int* classChildConstraintMasks,
    const int* classChildReferenceBloomMasks,
    int nodeCount,
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    const int* directWildcardFlags,
    const int* ruleArgStarts,
    const int* ruleArgGroupIds,
    const int* ruleArgConstraintMasks,
    const int* ruleArgKinds,
    const int* ruleArgHeadBuckets,
    const int* ruleArgExactHeadMasks,
    const int* ruleArgNestedRepeatMasks,
    const int* ruleArgNestedAtomBucketMasks,
    const int* ruleArgNestedConstraintMasks,
    const int* ruleArgNestedTopLevelReferenceMasks,
    int ruleCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    int total = nodeCount * ruleCount;
    if (index >= total)
    {
        return;
    }

    int nodeIndex = index / ruleCount;
    int ruleIndex = index % ruleCount;
    int wildcard = wildcardFlags[ruleIndex];
    int nodeHead = nodeHeadCodes[nodeIndex];
    int ruleHead = ruleHeadCodes[ruleIndex];
    int compatible = 0;
    if (wildcard != 0)
    {
        compatible = 1;
    }
    else if (nodeHead == (1 << 30) || ruleHead == (1 << 30))
    {
        compatible = 1;
    }
    else if (nodeHead == ruleHead && nodeArities[nodeIndex] == ruleArities[ruleIndex])
    {
        compatible = 1;
    }

    if (compatible != 0 && directWildcardFlags[ruleIndex] != 0)
    {
        int nodeStart = nodeChildStarts[nodeIndex];
        int ruleStart = ruleArgStarts[ruleIndex];
        int arity = ruleArities[ruleIndex];
        for (int i = 0; i < arity; i++)
        {
            int childClassId = nodeChildIds[nodeStart + i];
            int argKind = ruleArgKinds[ruleStart + i];
            if (argKind == 1)
            {
                int exactHeadMask = ruleArgExactHeadMasks[ruleStart + i];
                int nestedRepeatMask = ruleArgNestedRepeatMasks[ruleStart + i];
                if (exactHeadMask != 0)
                {
                    if ((classExactHeadMasks[childClassId] & exactHeadMask) == 0)
                    {
                        compatible = 0;
                        break;
                    }

                    if (nestedRepeatMask != 0 &&
                        (classChildEqualityMasks[childClassId] & nestedRepeatMask) != nestedRepeatMask)
                    {
                        compatible = 0;
                        break;
                    }

                    for (int childIndex = 0; childIndex < 4; childIndex++)
                    {
                        int requiredBucketMask = ruleArgNestedAtomBucketMasks[((ruleStart + i) * 4) + childIndex];
                        if (requiredBucketMask != 0 &&
                            (classChildAtomBucketMasks[(childClassId * 4) + childIndex] & requiredBucketMask) == 0)
                        {
                            compatible = 0;
                            break;
                        }

                        int requiredConstraintMask = ruleArgNestedConstraintMasks[((ruleStart + i) * 4) + childIndex];
                        if (requiredConstraintMask != 0 &&
                            (classChildConstraintMasks[(childClassId * 4) + childIndex] & requiredConstraintMask) == 0)
                        {
                            compatible = 0;
                            break;
                        }

                        int topLevelRefIndex = ruleArgNestedTopLevelReferenceMasks[((ruleStart + i) * 4) + childIndex];
                        if (topLevelRefIndex >= 0)
                        {
                            int requiredChildClassId = nodeChildIds[nodeStart + topLevelRefIndex];
                            int requiredBloomBit = 1 << (abs(requiredChildClassId % 30));
                            if ((classChildReferenceBloomMasks[(childClassId * 4) + childIndex] & requiredBloomBit) == 0)
                            {
                                compatible = 0;
                                break;
                            }
                        }
                    }

                    if (compatible == 0)
                    {
                        break;
                    }

                    continue;
                }

                int headBucket = ruleArgHeadBuckets[ruleStart + i];
                if ((classHeadBucketMasks[childClassId] & (1 << headBucket)) == 0)
                {
                    compatible = 0;
                    break;
                }

                continue;
            }

            int requiredMask = ruleArgConstraintMasks[ruleStart + i];
            if (requiredMask != 0 && (classConstraintMasks[childClassId] & requiredMask) == 0)
            {
                compatible = 0;
                break;
            }

            int groupId = ruleArgGroupIds[ruleStart + i];
            if (groupId < i)
            {
                int leftChild = childClassId;
                int rightChild = nodeChildIds[nodeStart + groupId];
                if (leftChild != rightChild)
                {
                    compatible = 0;
                    break;
                }
            }
        }
    }

    scores[index] = compatible;
}

__global__ void collectPositiveNodeRulePairsKernel(
    const int* scores,
    int nodeCount,
    int ruleCount,
    int maxPairs,
    int* pairNodeIndices,
    int* pairRuleIndices,
    int* pairCount)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    int total = nodeCount * ruleCount;
    if (index >= total || scores[index] <= 0)
    {
        return;
    }

    int slot = atomicAdd(pairCount, 1);
    if (slot >= maxPairs)
    {
        return;
    }

    pairNodeIndices[slot] = index / ruleCount;
    pairRuleIndices[slot] = index % ruleCount;
}

extern "C" __declspec(dllexport) int cobra_score_node_rule_candidates(
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* nodeChildStarts,
    const int* nodeChildIds,
    const int* classConstraintMasks,
    const int* classHeadBucketMasks,
    const int* classExactHeadMasks,
    const int* classChildEqualityMasks,
    const int* classChildAtomBucketMasks,
    const int* classChildConstraintMasks,
    const int* classChildReferenceBloomMasks,
    int nodeCount,
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    const int* directWildcardFlags,
    const int* ruleArgStarts,
    const int* ruleArgGroupIds,
    const int* ruleArgConstraintMasks,
    const int* ruleArgKinds,
    const int* ruleArgHeadBuckets,
    const int* ruleArgExactHeadMasks,
    const int* ruleArgNestedRepeatMasks,
    const int* ruleArgNestedAtomBucketMasks,
    const int* ruleArgNestedConstraintMasks,
    const int* ruleArgNestedTopLevelReferenceMasks,
    int ruleCount,
    int* hostScores)
{
    if (nodeHeadCodes == nullptr || nodeArities == nullptr || nodeChildStarts == nullptr ||
        nodeChildIds == nullptr || classConstraintMasks == nullptr ||
        classHeadBucketMasks == nullptr || classExactHeadMasks == nullptr || classChildEqualityMasks == nullptr || classChildAtomBucketMasks == nullptr || classChildConstraintMasks == nullptr || classChildReferenceBloomMasks == nullptr ||
        ruleHeadCodes == nullptr || ruleArities == nullptr ||
        wildcardFlags == nullptr || directWildcardFlags == nullptr || ruleArgStarts == nullptr ||
        ruleArgGroupIds == nullptr || ruleArgConstraintMasks == nullptr ||
        ruleArgKinds == nullptr || ruleArgHeadBuckets == nullptr || ruleArgExactHeadMasks == nullptr || ruleArgNestedRepeatMasks == nullptr || ruleArgNestedAtomBucketMasks == nullptr || ruleArgNestedConstraintMasks == nullptr || ruleArgNestedTopLevelReferenceMasks == nullptr || hostScores == nullptr)
    {
        return 280;
    }

    if (nodeCount <= 0 || ruleCount <= 0)
    {
        return 281;
    }

    int* deviceNodeHeadCodes = nullptr;
    int* deviceNodeArities = nullptr;
    int* deviceNodeChildStarts = nullptr;
    int* deviceNodeChildIds = nullptr;
    int* deviceClassConstraintMasks = nullptr;
    int* deviceClassHeadBucketMasks = nullptr;
    int* deviceClassExactHeadMasks = nullptr;
    int* deviceClassChildEqualityMasks = nullptr;
    int* deviceClassChildAtomBucketMasks = nullptr;
    int* deviceClassChildConstraintMasks = nullptr;
    int* deviceClassChildReferenceBloomMasks = nullptr;
    int* deviceRuleHeadCodes = nullptr;
    int* deviceRuleArities = nullptr;
    int* deviceWildcardFlags = nullptr;
    int* deviceDirectWildcardFlags = nullptr;
    int* deviceRuleArgStarts = nullptr;
    int* deviceRuleArgGroupIds = nullptr;
    int* deviceRuleArgConstraintMasks = nullptr;
    int* deviceRuleArgKinds = nullptr;
    int* deviceRuleArgHeadBuckets = nullptr;
    int* deviceRuleArgExactHeadMasks = nullptr;
    int* deviceRuleArgNestedRepeatMasks = nullptr;
    int* deviceRuleArgNestedAtomBucketMasks = nullptr;
    int* deviceRuleArgNestedConstraintMasks = nullptr;
    int* deviceRuleArgNestedTopLevelReferenceMasks = nullptr;
    int* deviceScores = nullptr;

    size_t nodeBytes = static_cast<size_t>(nodeCount) * sizeof(int);
    size_t ruleBytes = static_cast<size_t>(ruleCount) * sizeof(int);
    size_t nodeChildIdCount = static_cast<size_t>(nodeChildStarts[nodeCount - 1] + nodeArities[nodeCount - 1]);
    size_t nodeChildBytes = nodeChildIdCount * sizeof(int);
    int maxClassId = 0;
    for (size_t i = 0; i < nodeChildIdCount; i++)
    {
        if (nodeChildIds[i] > maxClassId)
        {
            maxClassId = nodeChildIds[i];
        }
    }
    size_t classMaskCount = static_cast<size_t>(maxClassId + 1);
    size_t classMaskBytes = classMaskCount * sizeof(int);
    size_t classChildAtomBucketBytes = classMaskCount * 4 * sizeof(int);
    size_t classChildConstraintBytes = classMaskCount * 4 * sizeof(int);
    size_t classChildReferenceBloomBytes = classMaskCount * 4 * sizeof(int);
    size_t ruleArgCount = static_cast<size_t>(ruleArgStarts[ruleCount - 1] + ruleArities[ruleCount - 1]);
    size_t ruleArgBytes = ruleArgCount * sizeof(int);
    size_t ruleArgNestedAtomBucketBytes = ruleArgCount * 4 * sizeof(int);
    size_t ruleArgNestedConstraintBytes = ruleArgCount * 4 * sizeof(int);
    size_t ruleArgNestedTopLevelReferenceBytes = ruleArgCount * 4 * sizeof(int);
    size_t scoreBytes = static_cast<size_t>(nodeCount) * static_cast<size_t>(ruleCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceNodeHeadCodes, nodeBytes);
    if (err != cudaSuccess) return 290;

    err = cudaMalloc(&deviceNodeArities, nodeBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        return 291;
    }

    err = cudaMalloc(&deviceNodeChildStarts, nodeBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        return 292;
    }

    err = cudaMalloc(&deviceNodeChildIds, nodeChildBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        return 293;
    }

    err = cudaMalloc(&deviceRuleHeadCodes, ruleBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        return 294;
    }

    err = cudaMalloc(&deviceClassConstraintMasks, classMaskBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceRuleHeadCodes);
        return 295;
    }

    err = cudaMalloc(&deviceClassHeadBucketMasks, classMaskBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        return 296;
    }

    err = cudaMalloc(&deviceClassExactHeadMasks, classMaskBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceRuleHeadCodes);
        return 296;
    }

    err = cudaMalloc(&deviceClassChildEqualityMasks, classMaskBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceRuleHeadCodes);
        return 296;
    }

    err = cudaMalloc(&deviceClassChildAtomBucketMasks, classChildAtomBucketBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceClassChildEqualityMasks);
        cudaFree(deviceRuleHeadCodes);
        return 318;
    }

    err = cudaMalloc(&deviceClassChildConstraintMasks, classChildConstraintBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceClassChildEqualityMasks);
        cudaFree(deviceClassChildAtomBucketMasks);
        cudaFree(deviceRuleHeadCodes);
        return 320;
    }

    err = cudaMalloc(&deviceClassChildReferenceBloomMasks, classChildReferenceBloomBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceClassChildEqualityMasks);
        cudaFree(deviceClassChildAtomBucketMasks);
        cudaFree(deviceClassChildConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        return 322;
    }

    err = cudaMalloc(&deviceRuleArities, ruleBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        return 296;
    }

    err = cudaMalloc(&deviceWildcardFlags, ruleBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        return 297;
    }

    err = cudaMalloc(&deviceDirectWildcardFlags, ruleBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        return 298;
    }

    err = cudaMalloc(&deviceRuleArgStarts, ruleBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        return 299;
    }

    err = cudaMalloc(&deviceRuleArgGroupIds, ruleArgBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        return 300;
    }

    err = cudaMalloc(&deviceRuleArgConstraintMasks, ruleArgBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        return 301;
    }

    err = cudaMalloc(&deviceRuleArgKinds, ruleArgBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        cudaFree(deviceRuleArgConstraintMasks);
        return 302;
    }

    err = cudaMalloc(&deviceRuleArgHeadBuckets, ruleArgBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        cudaFree(deviceRuleArgConstraintMasks);
        cudaFree(deviceRuleArgKinds);
        return 303;
    }

    err = cudaMalloc(&deviceRuleArgExactHeadMasks, ruleArgBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        cudaFree(deviceRuleArgConstraintMasks);
        cudaFree(deviceRuleArgKinds);
        cudaFree(deviceRuleArgHeadBuckets);
        return 303;
    }

    err = cudaMalloc(&deviceRuleArgNestedRepeatMasks, ruleArgBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceClassChildEqualityMasks);
        cudaFree(deviceClassChildAtomBucketMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        cudaFree(deviceRuleArgConstraintMasks);
        cudaFree(deviceRuleArgKinds);
        cudaFree(deviceRuleArgHeadBuckets);
        cudaFree(deviceRuleArgExactHeadMasks);
        return 303;
    }

    err = cudaMalloc(&deviceRuleArgNestedAtomBucketMasks, ruleArgNestedAtomBucketBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceClassChildEqualityMasks);
        cudaFree(deviceClassChildAtomBucketMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        cudaFree(deviceRuleArgConstraintMasks);
        cudaFree(deviceRuleArgKinds);
        cudaFree(deviceRuleArgHeadBuckets);
        cudaFree(deviceRuleArgExactHeadMasks);
        cudaFree(deviceRuleArgNestedRepeatMasks);
        return 319;
    }

    err = cudaMalloc(&deviceRuleArgNestedConstraintMasks, ruleArgNestedConstraintBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceClassChildEqualityMasks);
        cudaFree(deviceClassChildAtomBucketMasks);
        cudaFree(deviceClassChildConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        cudaFree(deviceRuleArgConstraintMasks);
        cudaFree(deviceRuleArgKinds);
        cudaFree(deviceRuleArgHeadBuckets);
        cudaFree(deviceRuleArgExactHeadMasks);
        cudaFree(deviceRuleArgNestedRepeatMasks);
        cudaFree(deviceRuleArgNestedAtomBucketMasks);
        return 321;
    }

    err = cudaMalloc(&deviceRuleArgNestedTopLevelReferenceMasks, ruleArgNestedTopLevelReferenceBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceClassHeadBucketMasks);
        cudaFree(deviceClassExactHeadMasks);
        cudaFree(deviceClassChildEqualityMasks);
        cudaFree(deviceClassChildAtomBucketMasks);
        cudaFree(deviceClassChildConstraintMasks);
        cudaFree(deviceClassChildReferenceBloomMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        cudaFree(deviceRuleArgConstraintMasks);
        cudaFree(deviceRuleArgKinds);
        cudaFree(deviceRuleArgHeadBuckets);
        cudaFree(deviceRuleArgExactHeadMasks);
        cudaFree(deviceRuleArgNestedRepeatMasks);
        cudaFree(deviceRuleArgNestedAtomBucketMasks);
        cudaFree(deviceRuleArgNestedConstraintMasks);
        return 323;
    }

    err = cudaMalloc(&deviceScores, scoreBytes);
    if (err != cudaSuccess)
    {
        cudaFree(deviceNodeHeadCodes);
        cudaFree(deviceNodeArities);
        cudaFree(deviceNodeChildStarts);
        cudaFree(deviceNodeChildIds);
        cudaFree(deviceClassConstraintMasks);
        cudaFree(deviceRuleHeadCodes);
        cudaFree(deviceRuleArities);
        cudaFree(deviceWildcardFlags);
        cudaFree(deviceDirectWildcardFlags);
        cudaFree(deviceRuleArgStarts);
        cudaFree(deviceRuleArgGroupIds);
        cudaFree(deviceRuleArgConstraintMasks);
        cudaFree(deviceRuleArgKinds);
        cudaFree(deviceRuleArgHeadBuckets);
        cudaFree(deviceRuleArgExactHeadMasks);
        cudaFree(deviceRuleArgNestedRepeatMasks);
        return 304;
    }

    err = cudaMemcpy(deviceNodeHeadCodes, nodeHeadCodes, nodeBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_296;
    err = cudaMemcpy(deviceNodeArities, nodeArities, nodeBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_297;
    err = cudaMemcpy(deviceNodeChildStarts, nodeChildStarts, nodeBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_298;
    err = cudaMemcpy(deviceNodeChildIds, nodeChildIds, nodeChildBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_299;
    err = cudaMemcpy(deviceClassConstraintMasks, classConstraintMasks, classMaskBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_300;
    err = cudaMemcpy(deviceClassHeadBucketMasks, classHeadBucketMasks, classMaskBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_301;
    err = cudaMemcpy(deviceClassExactHeadMasks, classExactHeadMasks, classMaskBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_302;
    err = cudaMemcpy(deviceClassChildEqualityMasks, classChildEqualityMasks, classMaskBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_303;
    err = cudaMemcpy(deviceClassChildAtomBucketMasks, classChildAtomBucketMasks, classChildAtomBucketBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_318;
    err = cudaMemcpy(deviceClassChildConstraintMasks, classChildConstraintMasks, classChildConstraintBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_320;
    err = cudaMemcpy(deviceClassChildReferenceBloomMasks, classChildReferenceBloomMasks, classChildReferenceBloomBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_322;
    err = cudaMemcpy(deviceRuleHeadCodes, ruleHeadCodes, ruleBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_304;
    err = cudaMemcpy(deviceRuleArities, ruleArities, ruleBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_305;
    err = cudaMemcpy(deviceWildcardFlags, wildcardFlags, ruleBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_306;
    err = cudaMemcpy(deviceDirectWildcardFlags, directWildcardFlags, ruleBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_307;
    err = cudaMemcpy(deviceRuleArgStarts, ruleArgStarts, ruleBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_308;
    err = cudaMemcpy(deviceRuleArgGroupIds, ruleArgGroupIds, ruleArgBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_309;
    err = cudaMemcpy(deviceRuleArgConstraintMasks, ruleArgConstraintMasks, ruleArgBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_310;
    err = cudaMemcpy(deviceRuleArgKinds, ruleArgKinds, ruleArgBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_311;
    err = cudaMemcpy(deviceRuleArgHeadBuckets, ruleArgHeadBuckets, ruleArgBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_312;
    err = cudaMemcpy(deviceRuleArgExactHeadMasks, ruleArgExactHeadMasks, ruleArgBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_313;
    err = cudaMemcpy(deviceRuleArgNestedRepeatMasks, ruleArgNestedRepeatMasks, ruleArgBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_314;
    err = cudaMemcpy(deviceRuleArgNestedAtomBucketMasks, ruleArgNestedAtomBucketMasks, ruleArgNestedAtomBucketBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_319;
    err = cudaMemcpy(deviceRuleArgNestedConstraintMasks, ruleArgNestedConstraintMasks, ruleArgNestedConstraintBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_321;
    err = cudaMemcpy(deviceRuleArgNestedTopLevelReferenceMasks, ruleArgNestedTopLevelReferenceMasks, ruleArgNestedTopLevelReferenceBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_323;

    {
        int total = nodeCount * ruleCount;
        int blockSize = 128;
        int gridSize = (total + blockSize - 1) / blockSize;
        scoreNodeRuleCandidatesKernel<<<gridSize, blockSize>>>(
            deviceNodeHeadCodes,
            deviceNodeArities,
            deviceNodeChildStarts,
            deviceNodeChildIds,
            deviceClassConstraintMasks,
            deviceClassHeadBucketMasks,
            deviceClassExactHeadMasks,
            deviceClassChildEqualityMasks,
            deviceClassChildAtomBucketMasks,
            deviceClassChildConstraintMasks,
            deviceClassChildReferenceBloomMasks,
            nodeCount,
            deviceRuleHeadCodes,
            deviceRuleArities,
            deviceWildcardFlags,
            deviceDirectWildcardFlags,
            deviceRuleArgStarts,
            deviceRuleArgGroupIds,
            deviceRuleArgConstraintMasks,
            deviceRuleArgKinds,
            deviceRuleArgHeadBuckets,
            deviceRuleArgExactHeadMasks,
            deviceRuleArgNestedRepeatMasks,
            deviceRuleArgNestedAtomBucketMasks,
            deviceRuleArgNestedConstraintMasks,
            deviceRuleArgNestedTopLevelReferenceMasks,
            ruleCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_315;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_316;

    err = cudaMemcpy(hostScores, deviceScores, scoreBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_317;

    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceClassChildAtomBucketMasks);
    cudaFree(deviceClassChildConstraintMasks);
    cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks);
    cudaFree(deviceRuleArgNestedConstraintMasks);
    cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    cudaFree(deviceScores);
    return 0;

cleanup_error_323:
    cudaFree(deviceNodeHeadCodes); cudaFree(deviceNodeArities); cudaFree(deviceNodeChildStarts); cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks); cudaFree(deviceClassHeadBucketMasks); cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks); cudaFree(deviceClassChildAtomBucketMasks); cudaFree(deviceClassChildConstraintMasks); cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes); cudaFree(deviceRuleArities); cudaFree(deviceWildcardFlags); cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts); cudaFree(deviceRuleArgGroupIds); cudaFree(deviceRuleArgConstraintMasks); cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets); cudaFree(deviceRuleArgExactHeadMasks); cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks); cudaFree(deviceRuleArgNestedConstraintMasks); cudaFree(deviceRuleArgNestedTopLevelReferenceMasks); cudaFree(deviceScores);
    return 323;
cleanup_error_322:
    cudaFree(deviceNodeHeadCodes); cudaFree(deviceNodeArities); cudaFree(deviceNodeChildStarts); cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks); cudaFree(deviceClassHeadBucketMasks); cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks); cudaFree(deviceClassChildAtomBucketMasks); cudaFree(deviceClassChildConstraintMasks); cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes); cudaFree(deviceRuleArities); cudaFree(deviceWildcardFlags); cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts); cudaFree(deviceRuleArgGroupIds); cudaFree(deviceRuleArgConstraintMasks); cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets); cudaFree(deviceRuleArgExactHeadMasks); cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks); cudaFree(deviceRuleArgNestedConstraintMasks); cudaFree(deviceRuleArgNestedTopLevelReferenceMasks); cudaFree(deviceScores);
    return 322;
cleanup_error_321:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceClassChildAtomBucketMasks);
    cudaFree(deviceClassChildConstraintMasks);
    cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks);
    cudaFree(deviceRuleArgNestedConstraintMasks);
    cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    cudaFree(deviceScores);
    return 321;
cleanup_error_320:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceClassChildAtomBucketMasks);
    cudaFree(deviceClassChildConstraintMasks);
    cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks);
    cudaFree(deviceRuleArgNestedConstraintMasks);
    cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    cudaFree(deviceScores);
    return 320;
cleanup_error_319:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceClassChildAtomBucketMasks);
    cudaFree(deviceClassChildConstraintMasks);
    cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks);
    cudaFree(deviceRuleArgNestedConstraintMasks);
    cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    cudaFree(deviceScores);
    return 319;
cleanup_error_318:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceClassChildAtomBucketMasks);
    cudaFree(deviceClassChildConstraintMasks);
    cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks);
    cudaFree(deviceRuleArgNestedConstraintMasks);
    cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    cudaFree(deviceScores);
    return 318;
cleanup_error_317:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceClassChildAtomBucketMasks);
    cudaFree(deviceClassChildConstraintMasks);
    cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks);
    cudaFree(deviceRuleArgNestedConstraintMasks);
    cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    cudaFree(deviceScores);
    return 317;
cleanup_error_316:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceClassChildAtomBucketMasks);
    cudaFree(deviceClassChildConstraintMasks);
    cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks);
    cudaFree(deviceRuleArgNestedConstraintMasks);
    cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    cudaFree(deviceScores);
    return 316;
cleanup_error_315:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceClassChildAtomBucketMasks);
    cudaFree(deviceClassChildConstraintMasks);
    cudaFree(deviceClassChildReferenceBloomMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceRuleArgNestedAtomBucketMasks);
    cudaFree(deviceRuleArgNestedConstraintMasks);
    cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    cudaFree(deviceScores);
    return 315;
cleanup_error_314:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceScores);
    return 314;
cleanup_error_313:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceScores);
    return 313;
cleanup_error_312:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceScores);
    return 312;
cleanup_error_311:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceClassExactHeadMasks);
    cudaFree(deviceClassChildEqualityMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceRuleArgExactHeadMasks);
    cudaFree(deviceRuleArgNestedRepeatMasks);
    cudaFree(deviceScores);
    return 311;
cleanup_error_310:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 310;
cleanup_error_309:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 309;
cleanup_error_308:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 308;
cleanup_error_307:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 307;
cleanup_error_306:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 306;
cleanup_error_305:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 305;
cleanup_error_304:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 304;
cleanup_error_303:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 303;
cleanup_error_302:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 302;
cleanup_error_301:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 301;
cleanup_error_300:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 300;
cleanup_error_299:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 299;
cleanup_error_298:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 298;
cleanup_error_297:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 297;
cleanup_error_296:
    cudaFree(deviceNodeHeadCodes);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNodeChildStarts);
    cudaFree(deviceNodeChildIds);
    cudaFree(deviceClassConstraintMasks);
    cudaFree(deviceClassHeadBucketMasks);
    cudaFree(deviceRuleHeadCodes);
    cudaFree(deviceRuleArities);
    cudaFree(deviceWildcardFlags);
    cudaFree(deviceDirectWildcardFlags);
    cudaFree(deviceRuleArgStarts);
    cudaFree(deviceRuleArgGroupIds);
    cudaFree(deviceRuleArgConstraintMasks);
    cudaFree(deviceRuleArgKinds);
    cudaFree(deviceRuleArgHeadBuckets);
    cudaFree(deviceScores);
    return 296;
}

__global__ void scoreDirectClassesKernel(
    const int* pairCounts,
    const int* generations,
    const int* nodeCounts,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] = (pairCounts[index] * 64) + (generations[index] * 8) + nodeCounts[index];
}

extern "C" __declspec(dllexport) int cobra_score_direct_classes(
    const int* pairCounts,
    const int* generations,
    const int* nodeCounts,
    int classCount,
    int* hostScores)
{
    if (pairCounts == nullptr || generations == nullptr || nodeCounts == nullptr || hostScores == nullptr)
    {
        return 326;
    }

    if (classCount <= 0)
    {
        return 327;
    }

    int* devicePairCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&devicePairCounts, intBytes);
    if (err != cudaSuccess) return 328;

    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(devicePairCounts);
        return 329;
    }

    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(devicePairCounts);
        cudaFree(deviceGenerations);
        return 330;
    }

    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess)
    {
        cudaFree(devicePairCounts);
        cudaFree(deviceGenerations);
        cudaFree(deviceNodeCounts);
        return 331;
    }

    err = cudaMemcpy(devicePairCounts, pairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_332;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_333;
    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_334;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreDirectClassesKernel<<<gridSize, blockSize>>>(devicePairCounts, deviceGenerations, deviceNodeCounts, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_335;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_336;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_337;

    cudaFree(devicePairCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 0;

cleanup_error_337:
    cudaFree(devicePairCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 337;
cleanup_error_336:
    cudaFree(devicePairCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 336;
cleanup_error_335:
    cudaFree(devicePairCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 335;
cleanup_error_334:
    cudaFree(devicePairCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 334;
cleanup_error_333:
    cudaFree(devicePairCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 333;
cleanup_error_332:
    cudaFree(devicePairCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 332;
}

__global__ void scoreRegionSelectionKernel(
    const int* benefitScores,
    const int* conflictScores,
    const int* residualFlags,
    const int* transposeFlags,
    const int* boundaryCounts,
    int regionCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= regionCount)
    {
        return;
    }

    scores[index] =
        (benefitScores[index] * 2) -
        conflictScores[index] -
        (boundaryCounts[index] * 16) -
        (residualFlags[index] != 0 ? 24 : 0) +
        (transposeFlags[index] != 0 ? 12 : 0);
}

extern "C" __declspec(dllexport) int cobra_score_region_selection(
    const int* benefitScores,
    const int* conflictScores,
    const int* residualFlags,
    const int* transposeFlags,
    const int* boundaryCounts,
    int regionCount,
    int* hostScores)
{
    if (benefitScores == nullptr || conflictScores == nullptr || residualFlags == nullptr ||
        transposeFlags == nullptr || boundaryCounts == nullptr || hostScores == nullptr)
    {
        return 338;
    }

    if (regionCount <= 0)
    {
        return 339;
    }

    int* deviceBenefitScores = nullptr;
    int* deviceConflictScores = nullptr;
    int* deviceResidualFlags = nullptr;
    int* deviceTransposeFlags = nullptr;
    int* deviceBoundaryCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(regionCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceBenefitScores, intBytes);
    if (err != cudaSuccess) return 340;
    err = cudaMalloc(&deviceConflictScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceBenefitScores); return 341; }
    err = cudaMalloc(&deviceResidualFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); return 342; }
    err = cudaMalloc(&deviceTransposeFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); return 343; }
    err = cudaMalloc(&deviceBoundaryCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); return 344; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); return 345; }

    err = cudaMemcpy(deviceBenefitScores, benefitScores, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_346;
    err = cudaMemcpy(deviceConflictScores, conflictScores, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_347;
    err = cudaMemcpy(deviceResidualFlags, residualFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_348;
    err = cudaMemcpy(deviceTransposeFlags, transposeFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_349;
    err = cudaMemcpy(deviceBoundaryCounts, boundaryCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_350;

    {
        int blockSize = 128;
        int gridSize = (regionCount + blockSize - 1) / blockSize;
        scoreRegionSelectionKernel<<<gridSize, blockSize>>>(
            deviceBenefitScores,
            deviceConflictScores,
            deviceResidualFlags,
            deviceTransposeFlags,
            deviceBoundaryCounts,
            regionCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_351;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_352;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_353;

    cudaFree(deviceBenefitScores);
    cudaFree(deviceConflictScores);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceTransposeFlags);
    cudaFree(deviceBoundaryCounts);
    cudaFree(deviceScores);
    return 0;

cleanup_error_353:
    cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 353;
cleanup_error_352:
    cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 352;
cleanup_error_351:
    cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 351;
cleanup_error_350:
    cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 350;
cleanup_error_349:
    cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 349;
cleanup_error_348:
    cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 348;
cleanup_error_347:
    cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 347;
cleanup_error_346:
    cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 346;
}

__global__ void scoreRuleOrderKernel(
    const int* compatibilityCounts,
    const int* arities,
    const int* wildcardFlags,
    int ruleCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= ruleCount)
    {
        return;
    }

    int compatibility = compatibilityCounts[index] > 0 ? 1000 / compatibilityCounts[index] : 0;
    scores[index] = compatibility + (arities[index] * 16) - (wildcardFlags[index] != 0 ? 64 : 0);
}

extern "C" __declspec(dllexport) int cobra_score_rule_order(
    const int* compatibilityCounts,
    const int* arities,
    const int* wildcardFlags,
    int ruleCount,
    int* hostScores)
{
    if (compatibilityCounts == nullptr || arities == nullptr || wildcardFlags == nullptr || hostScores == nullptr)
    {
        return 354;
    }
    if (ruleCount <= 0)
    {
        return 355;
    }

    int* deviceCompatibilityCounts = nullptr;
    int* deviceArities = nullptr;
    int* deviceWildcardFlags = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(ruleCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceCompatibilityCounts, intBytes);
    if (err != cudaSuccess) return 356;
    err = cudaMalloc(&deviceArities, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceCompatibilityCounts); return 357; }
    err = cudaMalloc(&deviceWildcardFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); return 358; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); cudaFree(deviceWildcardFlags); return 359; }

    err = cudaMemcpy(deviceCompatibilityCounts, compatibilityCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_360;
    err = cudaMemcpy(deviceArities, arities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_361;
    err = cudaMemcpy(deviceWildcardFlags, wildcardFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_362;

    {
        int blockSize = 128;
        int gridSize = (ruleCount + blockSize - 1) / blockSize;
        scoreRuleOrderKernel<<<gridSize, blockSize>>>(deviceCompatibilityCounts, deviceArities, deviceWildcardFlags, ruleCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_363;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_364;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_365;

    cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); cudaFree(deviceWildcardFlags); cudaFree(deviceScores);
    return 0;
cleanup_error_365:
    cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); cudaFree(deviceWildcardFlags); cudaFree(deviceScores); return 365;
cleanup_error_364:
    cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); cudaFree(deviceWildcardFlags); cudaFree(deviceScores); return 364;
cleanup_error_363:
    cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); cudaFree(deviceWildcardFlags); cudaFree(deviceScores); return 363;
cleanup_error_362:
    cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); cudaFree(deviceWildcardFlags); cudaFree(deviceScores); return 362;
cleanup_error_361:
    cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); cudaFree(deviceWildcardFlags); cudaFree(deviceScores); return 361;
cleanup_error_360:
    cudaFree(deviceCompatibilityCounts); cudaFree(deviceArities); cudaFree(deviceWildcardFlags); cudaFree(deviceScores); return 360;
}

__global__ void scoreCandidateRulesKernel(
    const int* allowedCounts,
    const int* arities,
    const int* directFlags,
    int ruleCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= ruleCount)
    {
        return;
    }

    int rarity = allowedCounts[index] > 0 ? 1000 / allowedCounts[index] : 0;
    scores[index] = rarity + (arities[index] * 12) + (directFlags[index] != 0 ? 40 : 0);
}

extern "C" __declspec(dllexport) int cobra_score_candidate_rules(
    const int* allowedCounts,
    const int* arities,
    const int* directFlags,
    int ruleCount,
    int* hostScores)
{
    if (allowedCounts == nullptr || arities == nullptr || directFlags == nullptr || hostScores == nullptr)
    {
        return 366;
    }
    if (ruleCount <= 0)
    {
        return 367;
    }

    int* deviceAllowedCounts = nullptr;
    int* deviceArities = nullptr;
    int* deviceDirectFlags = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(ruleCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceAllowedCounts, intBytes);
    if (err != cudaSuccess) return 368;
    err = cudaMalloc(&deviceArities, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceAllowedCounts); return 369; }
    err = cudaMalloc(&deviceDirectFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceAllowedCounts); cudaFree(deviceArities); return 370; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceAllowedCounts); cudaFree(deviceArities); cudaFree(deviceDirectFlags); return 371; }

    err = cudaMemcpy(deviceAllowedCounts, allowedCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_372;
    err = cudaMemcpy(deviceArities, arities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_373;
    err = cudaMemcpy(deviceDirectFlags, directFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_374;

    {
        int blockSize = 128;
        int gridSize = (ruleCount + blockSize - 1) / blockSize;
        scoreCandidateRulesKernel<<<gridSize, blockSize>>>(deviceAllowedCounts, deviceArities, deviceDirectFlags, ruleCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_375;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_376;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_377;

    cudaFree(deviceAllowedCounts); cudaFree(deviceArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores);
    return 0;
cleanup_error_377:
    cudaFree(deviceAllowedCounts); cudaFree(deviceArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 377;
cleanup_error_376:
    cudaFree(deviceAllowedCounts); cudaFree(deviceArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 376;
cleanup_error_375:
    cudaFree(deviceAllowedCounts); cudaFree(deviceArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 375;
cleanup_error_374:
    cudaFree(deviceAllowedCounts); cudaFree(deviceArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 374;
cleanup_error_373:
    cudaFree(deviceAllowedCounts); cudaFree(deviceArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 373;
cleanup_error_372:
    cudaFree(deviceAllowedCounts); cudaFree(deviceArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 372;
}

__global__ void scoreDirectRulesKernel(
    const int* pairCounts,
    const int* arities,
    const int* nestedFlags,
    int ruleCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= ruleCount)
    {
        return;
    }

    scores[index] = (pairCounts[index] * 32) + (arities[index] * 12) + (nestedFlags[index] != 0 ? 24 : 0);
}

extern "C" __declspec(dllexport) int cobra_score_direct_rules(
    const int* pairCounts,
    const int* arities,
    const int* nestedFlags,
    int ruleCount,
    int* hostScores)
{
    if (pairCounts == nullptr || arities == nullptr || nestedFlags == nullptr || hostScores == nullptr)
    {
        return 378;
    }
    if (ruleCount <= 0)
    {
        return 379;
    }

    int* devicePairCounts = nullptr;
    int* deviceArities = nullptr;
    int* deviceNestedFlags = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(ruleCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&devicePairCounts, intBytes);
    if (err != cudaSuccess) return 380;
    err = cudaMalloc(&deviceArities, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); return 381; }
    err = cudaMalloc(&deviceNestedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); cudaFree(deviceArities); return 382; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); cudaFree(deviceArities); cudaFree(deviceNestedFlags); return 383; }

    err = cudaMemcpy(devicePairCounts, pairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_384;
    err = cudaMemcpy(deviceArities, arities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_385;
    err = cudaMemcpy(deviceNestedFlags, nestedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_386;

    {
        int blockSize = 128;
        int gridSize = (ruleCount + blockSize - 1) / blockSize;
        scoreDirectRulesKernel<<<gridSize, blockSize>>>(devicePairCounts, deviceArities, deviceNestedFlags, ruleCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_387;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_388;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_389;

    cudaFree(devicePairCounts); cudaFree(deviceArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores);
    return 0;
cleanup_error_389:
    cudaFree(devicePairCounts); cudaFree(deviceArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 389;
cleanup_error_388:
    cudaFree(devicePairCounts); cudaFree(deviceArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 388;
cleanup_error_387:
    cudaFree(devicePairCounts); cudaFree(deviceArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 387;
cleanup_error_386:
    cudaFree(devicePairCounts); cudaFree(deviceArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 386;
cleanup_error_385:
    cudaFree(devicePairCounts); cudaFree(deviceArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 385;
cleanup_error_384:
    cudaFree(devicePairCounts); cudaFree(deviceArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 384;
}

__global__ void scorePreparedUnionsKernel(
    const int* leftGenerations,
    const int* rightGenerations,
    const int* leftNodeCounts,
    const int* rightNodeCounts,
    int pairCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= pairCount)
    {
        return;
    }

    scores[index] = ((leftGenerations[index] + rightGenerations[index]) * 8) + leftNodeCounts[index] + rightNodeCounts[index];
}

extern "C" __declspec(dllexport) int cobra_score_prepared_unions(
    const int* leftGenerations,
    const int* rightGenerations,
    const int* leftNodeCounts,
    const int* rightNodeCounts,
    int pairCount,
    int* hostScores)
{
    if (leftGenerations == nullptr || rightGenerations == nullptr || leftNodeCounts == nullptr || rightNodeCounts == nullptr || hostScores == nullptr)
    {
        return 390;
    }
    if (pairCount <= 0)
    {
        return 391;
    }

    int* deviceLeftGenerations = nullptr;
    int* deviceRightGenerations = nullptr;
    int* deviceLeftNodeCounts = nullptr;
    int* deviceRightNodeCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(pairCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceLeftGenerations, intBytes);
    if (err != cudaSuccess) return 392;
    err = cudaMalloc(&deviceRightGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftGenerations); return 393; }
    err = cudaMalloc(&deviceLeftNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); return 394; }
    err = cudaMalloc(&deviceRightNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); return 395; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); return 396; }

    err = cudaMemcpy(deviceLeftGenerations, leftGenerations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_397;
    err = cudaMemcpy(deviceRightGenerations, rightGenerations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_398;
    err = cudaMemcpy(deviceLeftNodeCounts, leftNodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_399;
    err = cudaMemcpy(deviceRightNodeCounts, rightNodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_400;

    {
        int blockSize = 128;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        scorePreparedUnionsKernel<<<gridSize, blockSize>>>(deviceLeftGenerations, deviceRightGenerations, deviceLeftNodeCounts, deviceRightNodeCounts, pairCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_401;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_402;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_403;

    cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); cudaFree(deviceScores);
    return 0;
cleanup_error_403:
    cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); cudaFree(deviceScores); return 403;
cleanup_error_402:
    cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); cudaFree(deviceScores); return 402;
cleanup_error_401:
    cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); cudaFree(deviceScores); return 401;
cleanup_error_400:
    cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); cudaFree(deviceScores); return 400;
cleanup_error_399:
    cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); cudaFree(deviceScores); return 399;
cleanup_error_398:
    cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); cudaFree(deviceScores); return 398;
cleanup_error_397:
    cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts); cudaFree(deviceScores); return 397;
}

extern "C" __declspec(dllexport) int cobra_score_prepared_unions_cached(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* hostScores)
{
    if (leftIds == nullptr || rightIds == nullptr || hostScores == nullptr)
    {
        return 842;
    }
    if (pairCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 843;
    }

    int* deviceLeftIds = nullptr;
    int* deviceRightIds = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(pairCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceLeftIds, intBytes);
    if (err != cudaSuccess) return 844;
    err = cudaMalloc(&deviceRightIds, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftIds); return 845; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceLeftIds); cudaFree(deviceRightIds); return 846; }

    err = cudaMemcpy(deviceLeftIds, leftIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_847;
    err = cudaMemcpy(deviceRightIds, rightIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_848;

    err = cudaMemset(deviceScores, 0, intBytes);
    if (err != cudaSuccess) goto cleanup_error_849;

    {
        int* deviceLeftGenerations = nullptr;
        int* deviceRightGenerations = nullptr;
        int* deviceLeftNodeCounts = nullptr;
        int* deviceRightNodeCounts = nullptr;
        err = cudaMalloc(&deviceLeftGenerations, intBytes);
        if (err != cudaSuccess) goto cleanup_error_849;
        err = cudaMalloc(&deviceRightGenerations, intBytes);
        if (err != cudaSuccess) { cudaFree(deviceLeftGenerations); goto cleanup_error_849; }
        err = cudaMalloc(&deviceLeftNodeCounts, intBytes);
        if (err != cudaSuccess) { cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); goto cleanup_error_849; }
        err = cudaMalloc(&deviceRightNodeCounts, intBytes);
        if (err != cudaSuccess) { cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); goto cleanup_error_849; }

        int blockSize = 128;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceLeftIds, g_cachedClassGenerations, pairCount, deviceLeftGenerations);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceRightIds, g_cachedClassGenerations, pairCount, deviceRightGenerations);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceLeftIds, g_cachedClassNodeCounts, pairCount, deviceLeftNodeCounts);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceRightIds, g_cachedClassNodeCounts, pairCount, deviceRightNodeCounts);
        err = cudaGetLastError();
        if (err != cudaSuccess)
        {
            cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts);
            goto cleanup_error_849;
        }

        scorePreparedUnionsKernel<<<gridSize, blockSize>>>(
            deviceLeftGenerations,
            deviceRightGenerations,
            deviceLeftNodeCounts,
            deviceRightNodeCounts,
            pairCount,
            deviceScores);
        err = cudaGetLastError();
        if (err != cudaSuccess)
        {
            cudaFree(deviceLeftGenerations); cudaFree(deviceRightGenerations); cudaFree(deviceLeftNodeCounts); cudaFree(deviceRightNodeCounts);
            goto cleanup_error_849;
        }

        cudaFree(deviceLeftGenerations);
        cudaFree(deviceRightGenerations);
        cudaFree(deviceLeftNodeCounts);
        cudaFree(deviceRightNodeCounts);
    }

    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_850;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_851;

    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    cudaFree(deviceScores);
    return 0;
cleanup_error_851:
    cudaFree(deviceLeftIds); cudaFree(deviceRightIds); cudaFree(deviceScores); return 851;
cleanup_error_850:
    cudaFree(deviceLeftIds); cudaFree(deviceRightIds); cudaFree(deviceScores); return 850;
cleanup_error_849:
    cudaFree(deviceLeftIds); cudaFree(deviceRightIds); cudaFree(deviceScores); return 849;
cleanup_error_848:
    cudaFree(deviceLeftIds); cudaFree(deviceRightIds); cudaFree(deviceScores); return 848;
cleanup_error_847:
    cudaFree(deviceLeftIds); cudaFree(deviceRightIds); cudaFree(deviceScores); return 847;
}

__global__ void scoreRebuildWithRepairKernel(
    const int* nodeCounts,
    const int* generations,
    const int* repairCounts,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] = (generations[index] * 4) + nodeCounts[index] + (repairCounts[index] * 64);
}

__global__ void hashRepairTargetsKernel(
    const int* headHashes,
    const int* childStarts,
    const int* childCounts,
    const int* canonicalChildIds,
    int candidateCount,
    int* targetHashes)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= candidateCount)
    {
        return;
    }

    unsigned int hash = static_cast<unsigned int>(headHashes[index]) ^ 2166136261u;
    int start = childStarts[index];
    int count = childCounts[index];
    for (int i = 0; i < count; i++)
    {
        hash ^= static_cast<unsigned int>(canonicalChildIds[start + i]) + 0x9e3779b9u + (hash << 6) + (hash >> 2);
        hash *= 16777619u;
    }

    targetHashes[index] = static_cast<int>(hash & 0x7fffffff);
}

extern "C" __declspec(dllexport) int cobra_score_rebuild_with_repair(
    const int* nodeCounts,
    const int* generations,
    const int* repairCounts,
    int classCount,
    int* hostScores)
{
    if (nodeCounts == nullptr || generations == nullptr || repairCounts == nullptr || hostScores == nullptr)
    {
        return 404;
    }
    if (classCount <= 0)
    {
        return 405;
    }

    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceRepairCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) return 406;
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); return 407; }
    err = cudaMalloc(&deviceRepairCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 408; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceRepairCounts); return 409; }

    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_410;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_411;
    err = cudaMemcpy(deviceRepairCounts, repairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_412;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreRebuildWithRepairKernel<<<gridSize, blockSize>>>(deviceNodeCounts, deviceGenerations, deviceRepairCounts, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_413;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_414;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_415;

    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceRepairCounts); cudaFree(deviceScores);
    return 0;
cleanup_error_415:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 415;
cleanup_error_414:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 414;
cleanup_error_413:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 413;
cleanup_error_412:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 412;
cleanup_error_411:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 411;
cleanup_error_410:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 410;
}

extern "C" __declspec(dllexport) int cobra_score_rebuild_with_repair_by_id_cached(
    const int* classIds,
    const int* repairCounts,
    int classCount,
    int* hostScores)
{
    if (classIds == nullptr || repairCounts == nullptr || hostScores == nullptr)
    {
        return 1041;
    }
    if (classCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 1042;
    }

    for (int index = 0; index < classCount; ++index)
    {
        if (classIds[index] < 0 || classIds[index] >= g_cachedClassCount)
        {
            return 1043;
        }
    }

    int* deviceClassIds = nullptr;
    int* deviceRepairCounts = nullptr;
    int* deviceGatheredNodeCounts = nullptr;
    int* deviceGatheredGenerations = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;
    err = cudaMalloc(&deviceRepairCounts, intBytes);
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;
    err = cudaMalloc(&deviceGatheredNodeCounts, intBytes);
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;
    err = cudaMalloc(&deviceGatheredGenerations, intBytes);
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;
    err = cudaMemcpy(deviceRepairCounts, repairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassNodeCounts, classCount, deviceGatheredNodeCounts);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassGenerations, classCount, deviceGatheredGenerations);
        scoreRebuildWithRepairKernel<<<gridSize, blockSize>>>(deviceGatheredNodeCounts, deviceGatheredGenerations, deviceRepairCounts, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_rebuild_by_id_1044;

    cudaFree(deviceClassIds);
    cudaFree(deviceRepairCounts);
    cudaFree(deviceGatheredNodeCounts);
    cudaFree(deviceGatheredGenerations);
    cudaFree(deviceScores);
    return 0;

cleanup_rebuild_by_id_1044:
    if (deviceClassIds != nullptr) cudaFree(deviceClassIds);
    if (deviceRepairCounts != nullptr) cudaFree(deviceRepairCounts);
    if (deviceGatheredNodeCounts != nullptr) cudaFree(deviceGatheredNodeCounts);
    if (deviceGatheredGenerations != nullptr) cudaFree(deviceGatheredGenerations);
    if (deviceScores != nullptr) cudaFree(deviceScores);
    return 1044;
}

__global__ void scoreAnalysisWithRepairKernel(
    const int* nodeCounts,
    const int* generations,
    const int* unresolvedFlags,
    const int* repairCounts,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] = (unresolvedFlags[index] != 0 ? 512 : 0) + (repairCounts[index] * 48) + (generations[index] * 4) + nodeCounts[index];
}

__global__ void scoreExtractClassesKernel(
    const int* nodeCounts,
    const int* generations,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] = (nodeCounts[index] * 32) + (generations[index] * 4);
}

__global__ void scoreExtractNodesCachedKernel(
    const int* headCodes,
    const int* arities,
    const int* classIds,
    const int* generations,
    const int* nodeCounts,
    int classCount,
    int nodeCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= nodeCount)
    {
        return;
    }

    int classId = classIds[index];
    int generation = (classId >= 0 && classId < classCount) ? generations[classId] : 0;
    int classNodeCount = (classId >= 0 && classId < classCount) ? nodeCounts[classId] : 0;
    int headPenalty = 75;

    switch (headCodes[index])
    {
        case 9: headPenalty = 1; break;
        case 8: headPenalty = 10; break;
        case 4: headPenalty = 60; break;
        case 5: headPenalty = 65; break;
        case 3: headPenalty = 85; break;
        default: break;
    }

    scores[index] = headPenalty + (arities[index] * 12) + classNodeCount + generation;
}

extern "C" __declspec(dllexport) int cobra_score_analysis_with_repair(
    const int* nodeCounts,
    const int* generations,
    const int* unresolvedFlags,
    const int* repairCounts,
    int classCount,
    int* hostScores)
{
    if (nodeCounts == nullptr || generations == nullptr || unresolvedFlags == nullptr || repairCounts == nullptr || hostScores == nullptr)
    {
        return 416;
    }
    if (classCount <= 0)
    {
        return 417;
    }

    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceUnresolvedFlags = nullptr;
    int* deviceRepairCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) return 418;
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); return 419; }
    err = cudaMalloc(&deviceUnresolvedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 420; }
    err = cudaMalloc(&deviceRepairCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); return 421; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); return 422; }

    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_423;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_424;
    err = cudaMemcpy(deviceUnresolvedFlags, unresolvedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_425;
    err = cudaMemcpy(deviceRepairCounts, repairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_426;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreAnalysisWithRepairKernel<<<gridSize, blockSize>>>(deviceNodeCounts, deviceGenerations, deviceUnresolvedFlags, deviceRepairCounts, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_427;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_428;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_429;

    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); cudaFree(deviceScores);
    return 0;
cleanup_error_429:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 429;
cleanup_error_428:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 428;
cleanup_error_427:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 427;
cleanup_error_426:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 426;
cleanup_error_425:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 425;
cleanup_error_424:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 424;
cleanup_error_423:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceUnresolvedFlags); cudaFree(deviceRepairCounts); cudaFree(deviceScores); return 423;
}

extern "C" __declspec(dllexport) int cobra_score_analysis_with_repair_by_id_cached(
    const int* classIds,
    const int* unresolvedFlags,
    const int* repairCounts,
    int classCount,
    int* hostScores)
{
    if (classIds == nullptr || unresolvedFlags == nullptr || repairCounts == nullptr || hostScores == nullptr)
    {
        return 430;
    }
    if (classCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 431;
    }

    for (int index = 0; index < classCount; ++index)
    {
        if (classIds[index] < 0 || classIds[index] >= g_cachedClassCount)
        {
            return 432;
        }
    }

    int* deviceClassIds = nullptr;
    int* deviceUnresolvedFlags = nullptr;
    int* deviceRepairCounts = nullptr;
    int* deviceGatheredNodeCounts = nullptr;
    int* deviceGatheredGenerations = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaMalloc(&deviceUnresolvedFlags, intBytes);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaMalloc(&deviceRepairCounts, intBytes);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaMalloc(&deviceGatheredNodeCounts, intBytes);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaMalloc(&deviceGatheredGenerations, intBytes);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaMemcpy(deviceUnresolvedFlags, unresolvedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaMemcpy(deviceRepairCounts, repairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassNodeCounts, classCount, deviceGatheredNodeCounts);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassGenerations, classCount, deviceGatheredGenerations);
        scoreAnalysisWithRepairKernel<<<gridSize, blockSize>>>(deviceGatheredNodeCounts, deviceGatheredGenerations, deviceUnresolvedFlags, deviceRepairCounts, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_analysis_by_id_433;

    cudaFree(deviceClassIds);
    cudaFree(deviceUnresolvedFlags);
    cudaFree(deviceRepairCounts);
    cudaFree(deviceGatheredNodeCounts);
    cudaFree(deviceGatheredGenerations);
    cudaFree(deviceScores);
    return 0;

cleanup_analysis_by_id_433:
    if (deviceClassIds != nullptr) cudaFree(deviceClassIds);
    if (deviceUnresolvedFlags != nullptr) cudaFree(deviceUnresolvedFlags);
    if (deviceRepairCounts != nullptr) cudaFree(deviceRepairCounts);
    if (deviceGatheredNodeCounts != nullptr) cudaFree(deviceGatheredNodeCounts);
    if (deviceGatheredGenerations != nullptr) cudaFree(deviceGatheredGenerations);
    if (deviceScores != nullptr) cudaFree(deviceScores);
    return 433;
}

extern "C" __declspec(dllexport) int cobra_score_extract_classes(
    const int* nodeCounts,
    const int* generations,
    int classCount,
    int* hostScores)
{
    if (nodeCounts == nullptr || generations == nullptr || hostScores == nullptr)
    {
        return 1080;
    }
    if (classCount <= 0)
    {
        return 1081;
    }

    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) return 1082;
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); return 1083; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 1084; }

    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_extract_classes_1085;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_extract_classes_1086;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreExtractClassesKernel<<<gridSize, blockSize>>>(deviceNodeCounts, deviceGenerations, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_extract_classes_1087;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_extract_classes_1088;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_extract_classes_1089;

    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceScores);
    return 0;

cleanup_extract_classes_1089:
cleanup_extract_classes_1088:
cleanup_extract_classes_1087:
cleanup_extract_classes_1086:
cleanup_extract_classes_1085:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceScores);
    return 1089;
}

extern "C" __declspec(dllexport) int cobra_score_extract_classes_cached(
    int classCount,
    int* hostScores)
{
    if (hostScores == nullptr)
    {
        return 1110;
    }
    if (classCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount != classCount)
    {
        return 1111;
    }

    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess)
    {
        return 1112;
    }

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreExtractClassesKernel<<<gridSize, blockSize>>>(
            g_cachedClassNodeCounts,
            g_cachedClassGenerations,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_extract_classes_cached_1113;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_extract_classes_cached_1114;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_extract_classes_cached_1115;

    cudaFree(deviceScores);
    return 0;

cleanup_extract_classes_cached_1115:
cleanup_extract_classes_cached_1114:
cleanup_extract_classes_cached_1113:
    cudaFree(deviceScores);
    return 1115;
}

extern "C" __declspec(dllexport) int cobra_score_extract_classes_by_id_cached(
    const int* classIds,
    int classCount,
    int* hostScores)
{
    if (classIds == nullptr || hostScores == nullptr)
    {
        return 1118;
    }
    if (classCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 1119;
    }

    for (int index = 0; index < classCount; ++index)
    {
        if (classIds[index] < 0 || classIds[index] >= g_cachedClassCount)
        {
            return 1120;
        }
    }

    int* deviceClassIds = nullptr;
    int* deviceGatheredNodeCounts = nullptr;
    int* deviceGatheredGenerations = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) goto cleanup_extract_classes_by_id_1121;
    err = cudaMalloc(&deviceGatheredNodeCounts, intBytes);
    if (err != cudaSuccess) goto cleanup_extract_classes_by_id_1121;
    err = cudaMalloc(&deviceGatheredGenerations, intBytes);
    if (err != cudaSuccess) goto cleanup_extract_classes_by_id_1121;
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) goto cleanup_extract_classes_by_id_1121;

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_extract_classes_by_id_1121;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassNodeCounts, classCount, deviceGatheredNodeCounts);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassGenerations, classCount, deviceGatheredGenerations);
        scoreExtractClassesKernel<<<gridSize, blockSize>>>(deviceGatheredNodeCounts, deviceGatheredGenerations, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_extract_classes_by_id_1121;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_extract_classes_by_id_1121;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_extract_classes_by_id_1121;

    cudaFree(deviceClassIds);
    cudaFree(deviceGatheredNodeCounts);
    cudaFree(deviceGatheredGenerations);
    cudaFree(deviceScores);
    return 0;

cleanup_extract_classes_by_id_1121:
    if (deviceClassIds != nullptr) cudaFree(deviceClassIds);
    if (deviceGatheredNodeCounts != nullptr) cudaFree(deviceGatheredNodeCounts);
    if (deviceGatheredGenerations != nullptr) cudaFree(deviceGatheredGenerations);
    if (deviceScores != nullptr) cudaFree(deviceScores);
    return 1121;
}

extern "C" __declspec(dllexport) int cobra_score_extract_nodes_cached(
    const int* headCodes,
    const int* arities,
    const int* classIds,
    int nodeCount,
    int* hostScores)
{
    if (headCodes == nullptr || arities == nullptr || classIds == nullptr || hostScores == nullptr)
    {
        return 1090;
    }
    if (nodeCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 1091;
    }

    int* deviceHeadCodes = nullptr;
    int* deviceArities = nullptr;
    int* deviceClassIds = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(nodeCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceHeadCodes, intBytes);
    if (err != cudaSuccess) return 1092;
    err = cudaMalloc(&deviceArities, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHeadCodes); return 1093; }
    err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHeadCodes); cudaFree(deviceArities); return 1094; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHeadCodes); cudaFree(deviceArities); cudaFree(deviceClassIds); return 1095; }

    err = cudaMemcpy(deviceHeadCodes, headCodes, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_extract_nodes_1096;
    err = cudaMemcpy(deviceArities, arities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_extract_nodes_1097;
    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_extract_nodes_1098;

    {
        int blockSize = 128;
        int gridSize = (nodeCount + blockSize - 1) / blockSize;
        scoreExtractNodesCachedKernel<<<gridSize, blockSize>>>(
            deviceHeadCodes,
            deviceArities,
            deviceClassIds,
            g_cachedClassGenerations,
            g_cachedClassNodeCounts,
            g_cachedClassCount,
            nodeCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_extract_nodes_1099;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_extract_nodes_1100;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_extract_nodes_1101;

    cudaFree(deviceHeadCodes); cudaFree(deviceArities); cudaFree(deviceClassIds); cudaFree(deviceScores);
    return 0;

cleanup_extract_nodes_1101:
cleanup_extract_nodes_1100:
cleanup_extract_nodes_1099:
cleanup_extract_nodes_1098:
cleanup_extract_nodes_1097:
cleanup_extract_nodes_1096:
    cudaFree(deviceHeadCodes); cudaFree(deviceArities); cudaFree(deviceClassIds); cudaFree(deviceScores);
    return 1101;
}

extern "C" __declspec(dllexport) int cobra_score_extract_nodes_fully_cached(
    int* hostScores)
{
    if (hostScores == nullptr)
    {
        return 1102;
    }
    if (g_cachedExtractHeadCodes == nullptr || g_cachedExtractArities == nullptr || g_cachedExtractClassIds == nullptr ||
        g_cachedExtractNodeCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 1103;
    }

    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(g_cachedExtractNodeCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess)
    {
        return 1104;
    }

    {
        int blockSize = 128;
        int gridSize = (g_cachedExtractNodeCount + blockSize - 1) / blockSize;
        scoreExtractNodesCachedKernel<<<gridSize, blockSize>>>(
            g_cachedExtractHeadCodes,
            g_cachedExtractArities,
            g_cachedExtractClassIds,
            g_cachedClassGenerations,
            g_cachedClassNodeCounts,
            g_cachedClassCount,
            g_cachedExtractNodeCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_extract_nodes_fully_cached_1105;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_extract_nodes_fully_cached_1106;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_extract_nodes_fully_cached_1107;

    cudaFree(deviceScores);
    return 0;

cleanup_extract_nodes_fully_cached_1107:
cleanup_extract_nodes_fully_cached_1106:
cleanup_extract_nodes_fully_cached_1105:
    cudaFree(deviceScores);
    return 1107;
}

__global__ void scoreMatchPriorityV2Kernel(
    const int* hotFlags,
    const int* boundaryFlags,
    const int* suppressedFlags,
    const int* ruleArities,
    const int* directFlags,
    int matchCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= matchCount)
    {
        return;
    }

    scores[index] =
        (hotFlags[index] != 0 ? 1000 : 0) +
        (boundaryFlags[index] != 0 ? 100 : 0) -
        (suppressedFlags[index] != 0 ? 1000 : 0) +
        (ruleArities[index] * 12) +
        (directFlags[index] != 0 ? 32 : 0);
}

__global__ void extractEqualityUnionsKernel(
    const int* headCodes,
    const int* childStarts,
    const int* childCounts,
    const int* childIds,
    int nodeCount,
    int* validFlags,
    int* leftIds,
    int* rightIds)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= nodeCount)
    {
        return;
    }

    validFlags[index] = 0;
    leftIds[index] = -1;
    rightIds[index] = -1;

    if (headCodes[index] != 6 || childCounts[index] != 2)
    {
        return;
    }

    int start = childStarts[index];
    int leftId = childIds[start];
    int rightId = childIds[start + 1];
    if (leftId == rightId)
    {
        return;
    }

    validFlags[index] = 1;
    leftIds[index] = leftId;
    rightIds[index] = rightId;
}

extern "C" __declspec(dllexport) int cobra_extract_equality_unions(
    const int* headCodes,
    const int* childStarts,
    const int* childCounts,
    const int* childIds,
    int nodeCount,
    int* validFlags,
    int* leftIds,
    int* rightIds)
{
    if (headCodes == nullptr || childStarts == nullptr || childCounts == nullptr || childIds == nullptr ||
        validFlags == nullptr || leftIds == nullptr || rightIds == nullptr)
    {
        return 446;
    }
    if (nodeCount <= 0)
    {
        return 447;
    }

    int childIdCount = childStarts[nodeCount - 1] + childCounts[nodeCount - 1];
    if (childIdCount < 0)
    {
        return 448;
    }

    int* deviceHeadCodes = nullptr;
    int* deviceChildStarts = nullptr;
    int* deviceChildCounts = nullptr;
    int* deviceChildIds = nullptr;
    int* deviceValidFlags = nullptr;
    int* deviceLeftIds = nullptr;
    int* deviceRightIds = nullptr;
    size_t nodeBytes = static_cast<size_t>(nodeCount) * sizeof(int);
    size_t childBytes = static_cast<size_t>(childIdCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceHeadCodes, nodeBytes);
    if (err != cudaSuccess) return 449;
    err = cudaMalloc(&deviceChildStarts, nodeBytes);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMalloc(&deviceChildCounts, nodeBytes);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMalloc(&deviceChildIds, childBytes);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMalloc(&deviceValidFlags, nodeBytes);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMalloc(&deviceLeftIds, nodeBytes);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMalloc(&deviceRightIds, nodeBytes);
    if (err != cudaSuccess) goto cleanup_equality_450;

    err = cudaMemcpy(deviceHeadCodes, headCodes, nodeBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMemcpy(deviceChildStarts, childStarts, nodeBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMemcpy(deviceChildCounts, childCounts, nodeBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_equality_450;
    if (childIdCount > 0)
    {
        err = cudaMemcpy(deviceChildIds, childIds, childBytes, cudaMemcpyHostToDevice);
        if (err != cudaSuccess) goto cleanup_equality_450;
    }

    {
        int blockSize = 128;
        int gridSize = (nodeCount + blockSize - 1) / blockSize;
        extractEqualityUnionsKernel<<<gridSize, blockSize>>>(
            deviceHeadCodes,
            deviceChildStarts,
            deviceChildCounts,
            deviceChildIds,
            nodeCount,
            deviceValidFlags,
            deviceLeftIds,
            deviceRightIds);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMemcpy(validFlags, deviceValidFlags, nodeBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMemcpy(leftIds, deviceLeftIds, nodeBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_equality_450;
    err = cudaMemcpy(rightIds, deviceRightIds, nodeBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_equality_450;

    cudaFree(deviceHeadCodes);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceChildIds);
    cudaFree(deviceValidFlags);
    cudaFree(deviceLeftIds);
    cudaFree(deviceRightIds);
    return 0;

cleanup_equality_450:
    if (deviceHeadCodes != nullptr) cudaFree(deviceHeadCodes);
    if (deviceChildStarts != nullptr) cudaFree(deviceChildStarts);
    if (deviceChildCounts != nullptr) cudaFree(deviceChildCounts);
    if (deviceChildIds != nullptr) cudaFree(deviceChildIds);
    if (deviceValidFlags != nullptr) cudaFree(deviceValidFlags);
    if (deviceLeftIds != nullptr) cudaFree(deviceLeftIds);
    if (deviceRightIds != nullptr) cudaFree(deviceRightIds);
    return 450;
}

extern "C" __declspec(dllexport) int cobra_score_match_priority_v2(
    const int* hotFlags,
    const int* boundaryFlags,
    const int* suppressedFlags,
    const int* ruleArities,
    const int* directFlags,
    int matchCount,
    int* hostScores)
{
    if (hotFlags == nullptr || boundaryFlags == nullptr || suppressedFlags == nullptr || ruleArities == nullptr || directFlags == nullptr || hostScores == nullptr)
    {
        return 430;
    }
    if (matchCount <= 0)
    {
        return 431;
    }

    int* deviceHotFlags = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceSuppressedFlags = nullptr;
    int* deviceRuleArities = nullptr;
    int* deviceDirectFlags = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(matchCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceHotFlags, intBytes);
    if (err != cudaSuccess) return 432;
    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); return 433; }
    err = cudaMalloc(&deviceSuppressedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); return 434; }
    err = cudaMalloc(&deviceRuleArities, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); return 435; }
    err = cudaMalloc(&deviceDirectFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); return 436; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); return 437; }

    err = cudaMemcpy(deviceHotFlags, hotFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_438;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_439;
    err = cudaMemcpy(deviceSuppressedFlags, suppressedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_440;
    err = cudaMemcpy(deviceRuleArities, ruleArities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_441;
    err = cudaMemcpy(deviceDirectFlags, directFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_442;

    {
        int blockSize = 128;
        int gridSize = (matchCount + blockSize - 1) / blockSize;
        scoreMatchPriorityV2Kernel<<<gridSize, blockSize>>>(deviceHotFlags, deviceBoundaryFlags, deviceSuppressedFlags, deviceRuleArities, deviceDirectFlags, matchCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_443;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_444;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_445;

    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores);
    return 0;
cleanup_error_445:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 445;
cleanup_error_444:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 444;
cleanup_error_443:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 443;
cleanup_error_442:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 442;
cleanup_error_441:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 441;
cleanup_error_440:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 440;
cleanup_error_439:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 439;
cleanup_error_438:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceScores); return 438;
}

__global__ void scoreFrontierV2Kernel(
    const int* nodeCounts,
    const int* generations,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    const int* hotRegionCounts,
    const int* boundaryRegionCounts,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] =
        generations[index] * 4 +
        nodeCounts[index] +
        (hotFlags[index] != 0 ? 1000 : 0) +
        (boundaryFlags[index] != 0 ? 100 : 0) -
        (residualFlags[index] != 0 ? 25 : 0) +
        (hotRegionCounts[index] * 48) +
        (boundaryRegionCounts[index] * 12);
}

extern "C" __declspec(dllexport) int cobra_score_frontier_v2(
    const int* nodeCounts,
    const int* generations,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    const int* hotRegionCounts,
    const int* boundaryRegionCounts,
    int classCount,
    int* hostScores)
{
    if (nodeCounts == nullptr || generations == nullptr || hotFlags == nullptr || boundaryFlags == nullptr || residualFlags == nullptr ||
        hotRegionCounts == nullptr || boundaryRegionCounts == nullptr || hostScores == nullptr)
    {
        return 446;
    }
    if (classCount <= 0)
    {
        return 447;
    }

    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceHotFlags = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceResidualFlags = nullptr;
    int* deviceHotRegionCounts = nullptr;
    int* deviceBoundaryRegionCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) return 448;
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); return 449; }
    err = cudaMalloc(&deviceHotFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 450; }
    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); return 451; }
    err = cudaMalloc(&deviceResidualFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); return 452; }
    err = cudaMalloc(&deviceHotRegionCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); return 453; }
    err = cudaMalloc(&deviceBoundaryRegionCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); return 454; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); return 455; }

    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_456;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_457;
    err = cudaMemcpy(deviceHotFlags, hotFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_458;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_459;
    err = cudaMemcpy(deviceResidualFlags, residualFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_460;
    err = cudaMemcpy(deviceHotRegionCounts, hotRegionCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_461;
    err = cudaMemcpy(deviceBoundaryRegionCounts, boundaryRegionCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_462;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreFrontierV2Kernel<<<gridSize, blockSize>>>(deviceNodeCounts, deviceGenerations, deviceHotFlags, deviceBoundaryFlags, deviceResidualFlags, deviceHotRegionCounts, deviceBoundaryRegionCounts, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_463;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_464;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_465;

    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores);
    return 0;
cleanup_error_465:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 465;
cleanup_error_464:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 464;
cleanup_error_463:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 463;
cleanup_error_462:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 462;
cleanup_error_461:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 461;
cleanup_error_460:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 460;
cleanup_error_459:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 459;
cleanup_error_458:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 458;
cleanup_error_457:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 457;
cleanup_error_456:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 456;
}

extern "C" __declspec(dllexport) int cobra_score_frontier_v2_by_id_cached(
    const int* classIds,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    const int* hotRegionCounts,
    const int* boundaryRegionCounts,
    int classCount,
    int* hostScores)
{
    if (classIds == nullptr || hotFlags == nullptr || boundaryFlags == nullptr || residualFlags == nullptr ||
        hotRegionCounts == nullptr || boundaryRegionCounts == nullptr || hostScores == nullptr)
    {
        return 10940;
    }
    if (classCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 10941;
    }

    int* deviceClassIds = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceHotFlags = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceResidualFlags = nullptr;
    int* deviceHotRegionCounts = nullptr;
    int* deviceBoundaryRegionCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) return 10942;
    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 10943; }
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); return 10944; }
    err = cudaMalloc(&deviceHotFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 10945; }
    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); return 10946; }
    err = cudaMalloc(&deviceResidualFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); return 10947; }
    err = cudaMalloc(&deviceHotRegionCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); return 10948; }
    err = cudaMalloc(&deviceBoundaryRegionCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); return 10949; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); return 10950; }

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10951;
    err = cudaMemcpy(deviceHotFlags, hotFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10952;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10953;
    err = cudaMemcpy(deviceResidualFlags, residualFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10954;
    err = cudaMemcpy(deviceHotRegionCounts, hotRegionCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10955;
    err = cudaMemcpy(deviceBoundaryRegionCounts, boundaryRegionCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10956;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassNodeCounts, classCount, deviceNodeCounts);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassGenerations, classCount, deviceGenerations);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_10957;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_10958;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreFrontierV2Kernel<<<gridSize, blockSize>>>(
            deviceNodeCounts,
            deviceGenerations,
            deviceHotFlags,
            deviceBoundaryFlags,
            deviceResidualFlags,
            deviceHotRegionCounts,
            deviceBoundaryRegionCounts,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_10959;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_10960;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_10961;

    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores);
    return 0;

cleanup_error_10961:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10961;
cleanup_error_10960:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10960;
cleanup_error_10959:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10959;
cleanup_error_10958:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10958;
cleanup_error_10957:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10957;
cleanup_error_10956:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10956;
cleanup_error_10955:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10955;
cleanup_error_10954:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10954;
cleanup_error_10953:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10953;
cleanup_error_10952:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10952;
cleanup_error_10951:
    cudaFree(deviceClassIds); cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 10951;
}

__global__ void scoreDirectClassesV2Kernel(
    const int* pairCounts,
    const int* nestedPairCounts,
    const int* generations,
    const int* nodeCounts,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] = (pairCounts[index] * 64) + (nestedPairCounts[index] * 160) + (generations[index] * 8) + nodeCounts[index];
}

extern "C" __declspec(dllexport) int cobra_score_direct_classes_v2(
    const int* pairCounts,
    const int* nestedPairCounts,
    const int* generations,
    const int* nodeCounts,
    int classCount,
    int* hostScores)
{
    if (pairCounts == nullptr || nestedPairCounts == nullptr || generations == nullptr || nodeCounts == nullptr || hostScores == nullptr)
    {
        return 466;
    }
    if (classCount <= 0)
    {
        return 467;
    }

    int* devicePairCounts = nullptr;
    int* deviceNestedPairCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&devicePairCounts, intBytes);
    if (err != cudaSuccess) return 468;
    err = cudaMalloc(&deviceNestedPairCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); return 469; }
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); return 470; }
    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); return 471; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); return 472; }

    err = cudaMemcpy(devicePairCounts, pairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_473;
    err = cudaMemcpy(deviceNestedPairCounts, nestedPairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_474;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_475;
    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_476;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreDirectClassesV2Kernel<<<gridSize, blockSize>>>(devicePairCounts, deviceNestedPairCounts, deviceGenerations, deviceNodeCounts, classCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_477;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_478;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_479;

    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores);
    return 0;
cleanup_error_479:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 479;
cleanup_error_478:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 478;
cleanup_error_477:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 477;
cleanup_error_476:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 476;
cleanup_error_475:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 475;
cleanup_error_474:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 474;
cleanup_error_473:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 473;
}

extern "C" __declspec(dllexport) int cobra_score_direct_classes_v2_cached(
    const int* pairCounts,
    const int* nestedPairCounts,
    int classCount,
    int* hostScores)
{
    if (pairCounts == nullptr || nestedPairCounts == nullptr || hostScores == nullptr)
    {
        return 820;
    }
    if (classCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount != classCount)
    {
        return 821;
    }

    int* devicePairCounts = nullptr;
    int* deviceNestedPairCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&devicePairCounts, intBytes);
    if (err != cudaSuccess) return 822;
    err = cudaMalloc(&deviceNestedPairCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); return 823; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); return 824; }

    err = cudaMemcpy(devicePairCounts, pairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_825;
    err = cudaMemcpy(deviceNestedPairCounts, nestedPairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_826;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        scoreDirectClassesV2Kernel<<<gridSize, blockSize>>>(
            devicePairCounts,
            deviceNestedPairCounts,
            g_cachedClassGenerations,
            g_cachedClassNodeCounts,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_827;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_828;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_829;

    cudaFree(devicePairCounts);
    cudaFree(deviceNestedPairCounts);
    cudaFree(deviceScores);
    return 0;
cleanup_error_829:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceScores); return 829;
cleanup_error_828:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceScores); return 828;
cleanup_error_827:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceScores); return 827;
cleanup_error_826:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceScores); return 826;
cleanup_error_825:
    cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceScores); return 825;
}

extern "C" __declspec(dllexport) int cobra_score_direct_classes_v2_by_id_cached(
    const int* classIds,
    const int* pairCounts,
    const int* nestedPairCounts,
    int classCount,
    int* hostScores)
{
    if (classIds == nullptr || pairCounts == nullptr || nestedPairCounts == nullptr || hostScores == nullptr)
    {
        return 830;
    }
    if (classCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 831;
    }

    int* deviceClassIds = nullptr;
    int* devicePairCounts = nullptr;
    int* deviceNestedPairCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) return 832;
    err = cudaMalloc(&devicePairCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 833; }
    err = cudaMalloc(&deviceNestedPairCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(devicePairCounts); return 834; }
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); return 835; }
    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); return 836; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); return 837; }

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_838;
    err = cudaMemcpy(devicePairCounts, pairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_839;
    err = cudaMemcpy(deviceNestedPairCounts, nestedPairCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_840;

    {
        int blockSize = 128;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassGenerations, classCount, deviceGenerations);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassNodeCounts, classCount, deviceNodeCounts);
        scoreDirectClassesV2Kernel<<<gridSize, blockSize>>>(
            devicePairCounts,
            deviceNestedPairCounts,
            deviceGenerations,
            deviceNodeCounts,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_841;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_842;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_843;

    cudaFree(deviceClassIds);
    cudaFree(devicePairCounts);
    cudaFree(deviceNestedPairCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 0;
cleanup_error_843:
    cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 843;
cleanup_error_842:
    cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 842;
cleanup_error_841:
    cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 841;
cleanup_error_840:
    cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 840;
cleanup_error_839:
    cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 839;
cleanup_error_838:
    cudaFree(deviceClassIds); cudaFree(devicePairCounts); cudaFree(deviceNestedPairCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 838;
}

__global__ void scoreRepairCandidatesV2Kernel(
    const int* classIds,
    const int* childCounts,
    const int* generations,
    const int* nodeCounts,
    const int* boundaryFlags,
    int candidateCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= candidateCount)
    {
        return;
    }

    scores[index] = (generations[index] * 16) + (nodeCounts[index] * 4) + (childCounts[index] * 8) + (boundaryFlags[index] != 0 ? 96 : 0) + (classIds[index] & 1);
}

extern "C" __declspec(dllexport) int cobra_score_repair_candidates_v2(
    const int* classIds,
    const int* childCounts,
    const int* generations,
    const int* nodeCounts,
    const int* boundaryFlags,
    int candidateCount,
    int* hostScores)
{
    if (classIds == nullptr || childCounts == nullptr || generations == nullptr || nodeCounts == nullptr || boundaryFlags == nullptr || hostScores == nullptr)
    {
        return 480;
    }
    if (candidateCount <= 0)
    {
        return 481;
    }

    int* deviceClassIds = nullptr;
    int* deviceChildCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(candidateCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) return 482;
    err = cudaMalloc(&deviceChildCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 483; }
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceChildCounts); return 484; }
    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); return 485; }
    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); return 486; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); return 487; }

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_488;
    err = cudaMemcpy(deviceChildCounts, childCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_489;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_490;
    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_491;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_492;

    {
        int blockSize = 128;
        int gridSize = (candidateCount + blockSize - 1) / blockSize;
        scoreRepairCandidatesV2Kernel<<<gridSize, blockSize>>>(deviceClassIds, deviceChildCounts, deviceGenerations, deviceNodeCounts, deviceBoundaryFlags, candidateCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_493;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_494;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_495;

    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores);
    return 0;
cleanup_error_495:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores); return 495;
cleanup_error_494:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores); return 494;
cleanup_error_493:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores); return 493;
cleanup_error_492:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores); return 492;
cleanup_error_491:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores); return 491;
cleanup_error_490:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores); return 490;
cleanup_error_489:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores); return 489;
cleanup_error_488:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceScores); return 488;
}

extern "C" __declspec(dllexport) int cobra_score_repair_candidates_v2_by_id_cached(
    const int* classIds,
    const int* childCounts,
    const int* boundaryFlags,
    int candidateCount,
    int* hostScores)
{
    if (classIds == nullptr || childCounts == nullptr || boundaryFlags == nullptr || hostScores == nullptr)
    {
        return 10970;
    }
    if (candidateCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 10971;
    }

    int* deviceClassIds = nullptr;
    int* deviceChildCounts = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(candidateCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) return 10972;
    err = cudaMalloc(&deviceChildCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 10973; }
    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceChildCounts); return 10974; }
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); return 10975; }
    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); return 10976; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); return 10977; }

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10978;
    err = cudaMemcpy(deviceChildCounts, childCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10979;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10980;

    {
        int blockSize = 128;
        int gridSize = (candidateCount + blockSize - 1) / blockSize;
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassGenerations, candidateCount, deviceGenerations);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassNodeCounts, candidateCount, deviceNodeCounts);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_10981;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_10982;

    {
        int blockSize = 128;
        int gridSize = (candidateCount + blockSize - 1) / blockSize;
        scoreRepairCandidatesV2Kernel<<<gridSize, blockSize>>>(
            deviceClassIds,
            deviceChildCounts,
            deviceGenerations,
            deviceNodeCounts,
            deviceBoundaryFlags,
            candidateCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_10983;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_10984;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_10985;

    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores);
    return 0;

cleanup_error_10985:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10985;
cleanup_error_10984:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10984;
cleanup_error_10983:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10983;
cleanup_error_10982:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10982;
cleanup_error_10981:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10981;
cleanup_error_10980:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10980;
cleanup_error_10979:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10979;
cleanup_error_10978:
    cudaFree(deviceClassIds); cudaFree(deviceChildCounts); cudaFree(deviceBoundaryFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10978;
}

__global__ void scoreRegionSelectionV2Kernel(
    const int* familyCodes,
    const int* benefitScores,
    const int* conflictScores,
    const int* residualFlags,
    const int* transposeFlags,
    const int* boundaryCounts,
    int regionCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= regionCount)
    {
        return;
    }

    int familyBonus = 0;
    switch (familyCodes[index])
    {
        case 1: familyBonus = 24; break; // SharedSink
        case 4: familyBonus = 18; break; // BilinearOverlap
        case 6: familyBonus = 12; break; // TransposeBoundaryCore
        default: familyBonus = 0; break;
    }

    scores[index] =
        (benefitScores[index] * 2) -
        conflictScores[index] -
        (boundaryCounts[index] * 16) -
        (residualFlags[index] != 0 ? 24 : 0) +
        (transposeFlags[index] != 0 ? 12 : 0) +
        familyBonus;
}

extern "C" __declspec(dllexport) int cobra_score_region_selection_v2(
    const int* familyCodes,
    const int* benefitScores,
    const int* conflictScores,
    const int* residualFlags,
    const int* transposeFlags,
    const int* boundaryCounts,
    int regionCount,
    int* hostScores)
{
    if (familyCodes == nullptr || benefitScores == nullptr || conflictScores == nullptr || residualFlags == nullptr ||
        transposeFlags == nullptr || boundaryCounts == nullptr || hostScores == nullptr)
    {
        return 496;
    }
    if (regionCount <= 0)
    {
        return 497;
    }

    int* deviceFamilyCodes = nullptr;
    int* deviceBenefitScores = nullptr;
    int* deviceConflictScores = nullptr;
    int* deviceResidualFlags = nullptr;
    int* deviceTransposeFlags = nullptr;
    int* deviceBoundaryCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(regionCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceFamilyCodes, intBytes);
    if (err != cudaSuccess) return 498;
    err = cudaMalloc(&deviceBenefitScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceFamilyCodes); return 499; }
    err = cudaMalloc(&deviceConflictScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); return 500; }
    err = cudaMalloc(&deviceResidualFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); return 501; }
    err = cudaMalloc(&deviceTransposeFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); return 502; }
    err = cudaMalloc(&deviceBoundaryCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); return 503; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); return 504; }

    err = cudaMemcpy(deviceFamilyCodes, familyCodes, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_505;
    err = cudaMemcpy(deviceBenefitScores, benefitScores, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_506;
    err = cudaMemcpy(deviceConflictScores, conflictScores, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_507;
    err = cudaMemcpy(deviceResidualFlags, residualFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_508;
    err = cudaMemcpy(deviceTransposeFlags, transposeFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_509;
    err = cudaMemcpy(deviceBoundaryCounts, boundaryCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_510;

    {
        int blockSize = 128;
        int gridSize = (regionCount + blockSize - 1) / blockSize;
        scoreRegionSelectionV2Kernel<<<gridSize, blockSize>>>(deviceFamilyCodes, deviceBenefitScores, deviceConflictScores, deviceResidualFlags, deviceTransposeFlags, deviceBoundaryCounts, regionCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_511;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_512;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_513;

    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores);
    return 0;
cleanup_error_513:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 513;
cleanup_error_512:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 512;
cleanup_error_511:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 511;
cleanup_error_510:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 510;
cleanup_error_509:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 509;
cleanup_error_508:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 508;
cleanup_error_507:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 507;
cleanup_error_506:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 506;
cleanup_error_505:
    cudaFree(deviceFamilyCodes); cudaFree(deviceBenefitScores); cudaFree(deviceConflictScores); cudaFree(deviceResidualFlags); cudaFree(deviceTransposeFlags); cudaFree(deviceBoundaryCounts); cudaFree(deviceScores); return 505;
}

__global__ void scoreDirectPairsV2Kernel(
    const int* classIds,
    const int* nodeArities,
    const int* generations,
    const int* nodeCounts,
    const int* nestedFlags,
    int pairCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= pairCount)
    {
        return;
    }

    scores[index] =
        (generations[index] * 16) +
        (nodeCounts[index] * 4) +
        nodeArities[index] +
        (nestedFlags[index] != 0 ? 96 : 0) +
        (classIds[index] & 1);
}

extern "C" __declspec(dllexport) int cobra_score_node_rule_candidates_cached(
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    const int* directWildcardFlags,
    const int* ruleArgStarts,
    const int* ruleArgGroupIds,
    const int* ruleArgConstraintMasks,
    const int* ruleArgKinds,
    const int* ruleArgHeadBuckets,
    const int* ruleArgExactHeadMasks,
    const int* ruleArgNestedRepeatMasks,
    const int* ruleArgNestedAtomBucketMasks,
    const int* ruleArgNestedConstraintMasks,
    const int* ruleArgNestedTopLevelReferenceMasks,
    int ruleArgCount,
    int ruleCount,
    int* hostScores);

extern "C" __declspec(dllexport) int cobra_hash_repair_targets(
    const int* headHashes,
    const int* childStarts,
    const int* childCounts,
    const int* canonicalChildIds,
    int candidateCount,
    int* targetHashes)
{
    if (headHashes == nullptr || childStarts == nullptr || childCounts == nullptr || canonicalChildIds == nullptr || targetHashes == nullptr)
    {
        return 773;
    }

    if (candidateCount <= 0)
    {
        return 774;
    }

    int* deviceHeadHashes = nullptr;
    int* deviceChildStarts = nullptr;
    int* deviceChildCounts = nullptr;
    int* deviceCanonicalChildIds = nullptr;
    int* deviceTargetHashes = nullptr;

    size_t candidateBytes = static_cast<size_t>(candidateCount) * sizeof(int);
    int childIdCount = 0;
    for (int i = 0; i < candidateCount; i++)
    {
        int end = childStarts[i] + childCounts[i];
        if (end > childIdCount)
        {
            childIdCount = end;
        }
    }

    size_t childBytes = static_cast<size_t>(childIdCount) * sizeof(int);
    cudaError_t err = cudaMalloc(&deviceHeadHashes, candidateBytes); if (err != cudaSuccess) return 775;
    err = cudaMalloc(&deviceChildStarts, candidateBytes); if (err != cudaSuccess) goto cleanup_hash;
    err = cudaMalloc(&deviceChildCounts, candidateBytes); if (err != cudaSuccess) goto cleanup_hash;
    err = cudaMalloc(&deviceCanonicalChildIds, childBytes); if (err != cudaSuccess) goto cleanup_hash;
    err = cudaMalloc(&deviceTargetHashes, candidateBytes); if (err != cudaSuccess) goto cleanup_hash;

    err = cudaMemcpy(deviceHeadHashes, headHashes, candidateBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_hash;
    err = cudaMemcpy(deviceChildStarts, childStarts, candidateBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_hash;
    err = cudaMemcpy(deviceChildCounts, childCounts, candidateBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_hash;
    if (childIdCount > 0)
    {
        err = cudaMemcpy(deviceCanonicalChildIds, canonicalChildIds, childBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_hash;
    }

    {
        int blockSize = 128;
        int gridSize = (candidateCount + blockSize - 1) / blockSize;
        hashRepairTargetsKernel<<<gridSize, blockSize>>>(
            deviceHeadHashes,
            deviceChildStarts,
            deviceChildCounts,
            deviceCanonicalChildIds,
            candidateCount,
            deviceTargetHashes);
    }

    err = cudaGetLastError(); if (err != cudaSuccess) goto cleanup_hash;
    err = cudaDeviceSynchronize(); if (err != cudaSuccess) goto cleanup_hash;
    err = cudaMemcpy(targetHashes, deviceTargetHashes, candidateBytes, cudaMemcpyDeviceToHost); if (err != cudaSuccess) goto cleanup_hash;

    cudaFree(deviceHeadHashes);
    cudaFree(deviceChildStarts);
    cudaFree(deviceChildCounts);
    cudaFree(deviceCanonicalChildIds);
    cudaFree(deviceTargetHashes);
    return 0;

cleanup_hash:
    if (deviceHeadHashes != nullptr) cudaFree(deviceHeadHashes);
    if (deviceChildStarts != nullptr) cudaFree(deviceChildStarts);
    if (deviceChildCounts != nullptr) cudaFree(deviceChildCounts);
    if (deviceCanonicalChildIds != nullptr) cudaFree(deviceCanonicalChildIds);
    if (deviceTargetHashes != nullptr) cudaFree(deviceTargetHashes);
    return 776;
}

extern "C" __declspec(dllexport) int cobra_cache_rule_signature(
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    const int* directWildcardFlags,
    const int* ruleArgStarts,
    const int* ruleArgGroupIds,
    const int* ruleArgConstraintMasks,
    const int* ruleArgKinds,
    const int* ruleArgHeadBuckets,
    const int* ruleArgExactHeadMasks,
    const int* ruleArgNestedRepeatMasks,
    const int* ruleArgNestedAtomBucketMasks,
    const int* ruleArgNestedConstraintMasks,
    const int* ruleArgNestedTopLevelReferenceMasks,
    int ruleArgCount,
    int ruleCount)
{
    if (ruleHeadCodes == nullptr || ruleArities == nullptr || wildcardFlags == nullptr || directWildcardFlags == nullptr ||
        ruleArgStarts == nullptr)
    {
        return 330;
    }

    if (ruleCount <= 0 || ruleArgCount < 0)
    {
        return 331;
    }

    bool ruleSizeChanged = g_cachedRuleCount != ruleCount;
    bool argSizeChanged = g_cachedRuleArgCount != ruleArgCount;
    if (ruleSizeChanged || argSizeChanged)
    {
        freeRuleSignatureCache();
        cudaError_t err = cudaMalloc(&g_cachedRuleHeadCodes, static_cast<size_t>(ruleCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 332; }
        err = cudaMalloc(&g_cachedRuleArities, static_cast<size_t>(ruleCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 333; }
        err = cudaMalloc(&g_cachedWildcardFlags, static_cast<size_t>(ruleCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 334; }
        err = cudaMalloc(&g_cachedDirectWildcardFlags, static_cast<size_t>(ruleCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 335; }
        err = cudaMalloc(&g_cachedRuleArgStarts, static_cast<size_t>(ruleCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 336; }
        if (ruleArgCount > 0)
        {
            err = cudaMalloc(&g_cachedRuleArgGroupIds, static_cast<size_t>(ruleArgCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 337; }
            err = cudaMalloc(&g_cachedRuleArgConstraintMasks, static_cast<size_t>(ruleArgCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 338; }
            err = cudaMalloc(&g_cachedRuleArgKinds, static_cast<size_t>(ruleArgCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 339; }
            err = cudaMalloc(&g_cachedRuleArgHeadBuckets, static_cast<size_t>(ruleArgCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 340; }
            err = cudaMalloc(&g_cachedRuleArgExactHeadMasks, static_cast<size_t>(ruleArgCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 341; }
            err = cudaMalloc(&g_cachedRuleArgNestedRepeatMasks, static_cast<size_t>(ruleArgCount) * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 342; }
            err = cudaMalloc(&g_cachedRuleArgNestedAtomBucketMasks, static_cast<size_t>(ruleArgCount) * 4 * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 343; }
            err = cudaMalloc(&g_cachedRuleArgNestedConstraintMasks, static_cast<size_t>(ruleArgCount) * 4 * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 344; }
            err = cudaMalloc(&g_cachedRuleArgNestedTopLevelReferenceMasks, static_cast<size_t>(ruleArgCount) * 4 * sizeof(int)); if (err != cudaSuccess) { freeRuleSignatureCache(); return 345; }
        }

        g_cachedRuleCount = ruleCount;
        g_cachedRuleArgCount = ruleArgCount;
    }

    cudaError_t err = cudaMemcpy(g_cachedRuleHeadCodes, ruleHeadCodes, static_cast<size_t>(ruleCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 346; }
    err = cudaMemcpy(g_cachedRuleArities, ruleArities, static_cast<size_t>(ruleCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 347; }
    err = cudaMemcpy(g_cachedWildcardFlags, wildcardFlags, static_cast<size_t>(ruleCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 348; }
    err = cudaMemcpy(g_cachedDirectWildcardFlags, directWildcardFlags, static_cast<size_t>(ruleCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 349; }
    err = cudaMemcpy(g_cachedRuleArgStarts, ruleArgStarts, static_cast<size_t>(ruleCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 350; }
    if (ruleArgCount > 0)
    {
        err = cudaMemcpy(g_cachedRuleArgGroupIds, ruleArgGroupIds, static_cast<size_t>(ruleArgCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 351; }
        err = cudaMemcpy(g_cachedRuleArgConstraintMasks, ruleArgConstraintMasks, static_cast<size_t>(ruleArgCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 352; }
        err = cudaMemcpy(g_cachedRuleArgKinds, ruleArgKinds, static_cast<size_t>(ruleArgCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 353; }
        err = cudaMemcpy(g_cachedRuleArgHeadBuckets, ruleArgHeadBuckets, static_cast<size_t>(ruleArgCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 354; }
        err = cudaMemcpy(g_cachedRuleArgExactHeadMasks, ruleArgExactHeadMasks, static_cast<size_t>(ruleArgCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 355; }
        err = cudaMemcpy(g_cachedRuleArgNestedRepeatMasks, ruleArgNestedRepeatMasks, static_cast<size_t>(ruleArgCount) * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 356; }
        err = cudaMemcpy(g_cachedRuleArgNestedAtomBucketMasks, ruleArgNestedAtomBucketMasks, static_cast<size_t>(ruleArgCount) * 4 * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 357; }
        err = cudaMemcpy(g_cachedRuleArgNestedConstraintMasks, ruleArgNestedConstraintMasks, static_cast<size_t>(ruleArgCount) * 4 * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 358; }
        err = cudaMemcpy(g_cachedRuleArgNestedTopLevelReferenceMasks, ruleArgNestedTopLevelReferenceMasks, static_cast<size_t>(ruleArgCount) * 4 * sizeof(int), cudaMemcpyHostToDevice); if (err != cudaSuccess) { freeRuleSignatureCache(); return 359; }
    }

    return 0;
}

extern "C" __declspec(dllexport) int cobra_score_node_rule_candidates_fully_cached(
    int ruleArgCount,
    int ruleCount,
    int* hostScores)
{
    bool missingCachedRuleArgs = ruleArgCount > 0 &&
        (g_cachedRuleArgGroupIds == nullptr || g_cachedRuleArgConstraintMasks == nullptr || g_cachedRuleArgKinds == nullptr ||
         g_cachedRuleArgHeadBuckets == nullptr || g_cachedRuleArgExactHeadMasks == nullptr ||
         g_cachedRuleArgNestedRepeatMasks == nullptr || g_cachedRuleArgNestedAtomBucketMasks == nullptr ||
         g_cachedRuleArgNestedConstraintMasks == nullptr || g_cachedRuleArgNestedTopLevelReferenceMasks == nullptr);

    if (g_cachedNodeHeadCodes == nullptr || g_cachedNodeArities == nullptr || g_cachedNodeChildStarts == nullptr ||
        g_cachedClassConstraintMasks == nullptr || g_cachedClassHeadBucketMasks == nullptr || g_cachedClassExactHeadMasks == nullptr ||
        g_cachedClassChildEqualityMasks == nullptr || g_cachedClassChildAtomBucketMasks == nullptr ||
        g_cachedClassChildConstraintMasks == nullptr || g_cachedClassChildReferenceBloomMasks == nullptr ||
        g_cachedRuleHeadCodes == nullptr || g_cachedRuleArities == nullptr || g_cachedWildcardFlags == nullptr ||
        g_cachedDirectWildcardFlags == nullptr || g_cachedRuleArgStarts == nullptr || hostScores == nullptr ||
        g_cachedRuleCount != ruleCount || g_cachedRuleArgCount != ruleArgCount || missingCachedRuleArgs)
    {
        return 360;
    }

    return cobra_score_node_rule_candidates_cached(
        g_cachedRuleHeadCodes,
        g_cachedRuleArities,
        g_cachedWildcardFlags,
        g_cachedDirectWildcardFlags,
        g_cachedRuleArgStarts,
        g_cachedRuleArgGroupIds,
        g_cachedRuleArgConstraintMasks,
        g_cachedRuleArgKinds,
        g_cachedRuleArgHeadBuckets,
        g_cachedRuleArgExactHeadMasks,
        g_cachedRuleArgNestedRepeatMasks,
        g_cachedRuleArgNestedAtomBucketMasks,
        g_cachedRuleArgNestedConstraintMasks,
        g_cachedRuleArgNestedTopLevelReferenceMasks,
        ruleArgCount,
        ruleCount,
        hostScores);
}

extern "C" __declspec(dllexport) int cobra_score_node_rule_candidates_cached(
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    const int* directWildcardFlags,
    const int* ruleArgStarts,
    const int* ruleArgGroupIds,
    const int* ruleArgConstraintMasks,
    const int* ruleArgKinds,
    const int* ruleArgHeadBuckets,
    const int* ruleArgExactHeadMasks,
    const int* ruleArgNestedRepeatMasks,
    const int* ruleArgNestedAtomBucketMasks,
    const int* ruleArgNestedConstraintMasks,
    const int* ruleArgNestedTopLevelReferenceMasks,
    int ruleArgCount,
    int ruleCount,
    int* hostScores)
{
    bool missingRuleArgs = ruleArgCount > 0 &&
        (ruleArgGroupIds == nullptr || ruleArgConstraintMasks == nullptr || ruleArgKinds == nullptr ||
         ruleArgHeadBuckets == nullptr || ruleArgExactHeadMasks == nullptr || ruleArgNestedRepeatMasks == nullptr ||
         ruleArgNestedAtomBucketMasks == nullptr || ruleArgNestedConstraintMasks == nullptr ||
         ruleArgNestedTopLevelReferenceMasks == nullptr);

    if (g_cachedNodeHeadCodes == nullptr || g_cachedNodeArities == nullptr || g_cachedNodeChildStarts == nullptr ||
        g_cachedClassConstraintMasks == nullptr || g_cachedClassHeadBucketMasks == nullptr || g_cachedClassExactHeadMasks == nullptr ||
        g_cachedClassChildEqualityMasks == nullptr || g_cachedClassChildAtomBucketMasks == nullptr ||
        g_cachedClassChildConstraintMasks == nullptr || g_cachedClassChildReferenceBloomMasks == nullptr ||
        ruleHeadCodes == nullptr || ruleArities == nullptr || wildcardFlags == nullptr || directWildcardFlags == nullptr ||
        ruleArgStarts == nullptr || hostScores == nullptr || missingRuleArgs)
    {
        return 324;
    }

    int nodeCount = g_cachedNodeRuleNodeCount;
    if (nodeCount <= 0 || ruleCount <= 0 || ruleArgCount < 0)
    {
        return 325;
    }

    int* deviceRuleHeadCodes = nullptr;
    int* deviceRuleArities = nullptr;
    int* deviceWildcardFlags = nullptr;
    int* deviceDirectWildcardFlags = nullptr;
    int* deviceRuleArgStarts = nullptr;
    int* deviceRuleArgGroupIds = nullptr;
    int* deviceRuleArgConstraintMasks = nullptr;
    int* deviceRuleArgKinds = nullptr;
    int* deviceRuleArgHeadBuckets = nullptr;
    int* deviceRuleArgExactHeadMasks = nullptr;
    int* deviceRuleArgNestedRepeatMasks = nullptr;
    int* deviceRuleArgNestedAtomBucketMasks = nullptr;
    int* deviceRuleArgNestedConstraintMasks = nullptr;
    int* deviceRuleArgNestedTopLevelReferenceMasks = nullptr;
    int* deviceScores = nullptr;

    size_t ruleBytes = static_cast<size_t>(ruleCount) * sizeof(int);
    size_t ruleArgBytes = static_cast<size_t>(ruleArgCount) * sizeof(int);
    size_t nestedArgBytes = static_cast<size_t>(ruleArgCount) * 4 * sizeof(int);
    size_t scoreBytes = static_cast<size_t>(nodeCount) * static_cast<size_t>(ruleCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceRuleHeadCodes, ruleBytes); if (err != cudaSuccess) return 326;
    err = cudaMalloc(&deviceRuleArities, ruleBytes); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaMalloc(&deviceWildcardFlags, ruleBytes); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaMalloc(&deviceDirectWildcardFlags, ruleBytes); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaMalloc(&deviceRuleArgStarts, ruleBytes); if (err != cudaSuccess) goto cleanup_cached;
    if (ruleArgCount > 0)
    {
        err = cudaMalloc(&deviceRuleArgGroupIds, ruleArgBytes); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMalloc(&deviceRuleArgConstraintMasks, ruleArgBytes); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMalloc(&deviceRuleArgKinds, ruleArgBytes); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMalloc(&deviceRuleArgHeadBuckets, ruleArgBytes); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMalloc(&deviceRuleArgExactHeadMasks, ruleArgBytes); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMalloc(&deviceRuleArgNestedRepeatMasks, ruleArgBytes); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMalloc(&deviceRuleArgNestedAtomBucketMasks, nestedArgBytes); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMalloc(&deviceRuleArgNestedConstraintMasks, nestedArgBytes); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMalloc(&deviceRuleArgNestedTopLevelReferenceMasks, nestedArgBytes); if (err != cudaSuccess) goto cleanup_cached;
    }
    err = cudaMalloc(&deviceScores, scoreBytes); if (err != cudaSuccess) goto cleanup_cached;

    err = cudaMemcpy(deviceRuleHeadCodes, ruleHeadCodes, ruleBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaMemcpy(deviceRuleArities, ruleArities, ruleBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaMemcpy(deviceWildcardFlags, wildcardFlags, ruleBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaMemcpy(deviceDirectWildcardFlags, directWildcardFlags, ruleBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaMemcpy(deviceRuleArgStarts, ruleArgStarts, ruleBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
    if (ruleArgCount > 0)
    {
        err = cudaMemcpy(deviceRuleArgGroupIds, ruleArgGroupIds, ruleArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMemcpy(deviceRuleArgConstraintMasks, ruleArgConstraintMasks, ruleArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMemcpy(deviceRuleArgKinds, ruleArgKinds, ruleArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMemcpy(deviceRuleArgHeadBuckets, ruleArgHeadBuckets, ruleArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMemcpy(deviceRuleArgExactHeadMasks, ruleArgExactHeadMasks, ruleArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMemcpy(deviceRuleArgNestedRepeatMasks, ruleArgNestedRepeatMasks, ruleArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMemcpy(deviceRuleArgNestedAtomBucketMasks, ruleArgNestedAtomBucketMasks, nestedArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMemcpy(deviceRuleArgNestedConstraintMasks, ruleArgNestedConstraintMasks, nestedArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
        err = cudaMemcpy(deviceRuleArgNestedTopLevelReferenceMasks, ruleArgNestedTopLevelReferenceMasks, nestedArgBytes, cudaMemcpyHostToDevice); if (err != cudaSuccess) goto cleanup_cached;
    }

    int blockSize = 128;
    int gridSize = (nodeCount * ruleCount + blockSize - 1) / blockSize;
    scoreNodeRuleCandidatesKernel<<<gridSize, blockSize>>>(
        g_cachedNodeHeadCodes,
        g_cachedNodeArities,
        g_cachedNodeChildStarts,
        g_cachedNodeChildIds,
        g_cachedClassConstraintMasks,
        g_cachedClassHeadBucketMasks,
        g_cachedClassExactHeadMasks,
        g_cachedClassChildEqualityMasks,
        g_cachedClassChildAtomBucketMasks,
        g_cachedClassChildConstraintMasks,
        g_cachedClassChildReferenceBloomMasks,
        nodeCount,
        deviceRuleHeadCodes,
        deviceRuleArities,
        deviceWildcardFlags,
        deviceDirectWildcardFlags,
        deviceRuleArgStarts,
        deviceRuleArgGroupIds,
        deviceRuleArgConstraintMasks,
        deviceRuleArgKinds,
        deviceRuleArgHeadBuckets,
        deviceRuleArgExactHeadMasks,
        deviceRuleArgNestedRepeatMasks,
        deviceRuleArgNestedAtomBucketMasks,
        deviceRuleArgNestedConstraintMasks,
        deviceRuleArgNestedTopLevelReferenceMasks,
        ruleCount,
        deviceScores);

    err = cudaGetLastError(); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaDeviceSynchronize(); if (err != cudaSuccess) goto cleanup_cached;
    err = cudaMemcpy(hostScores, deviceScores, scoreBytes, cudaMemcpyDeviceToHost); if (err != cudaSuccess) goto cleanup_cached;

    if (deviceScores != nullptr) cudaFree(deviceScores);
    if (deviceRuleArgNestedTopLevelReferenceMasks != nullptr) cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    if (deviceRuleArgNestedConstraintMasks != nullptr) cudaFree(deviceRuleArgNestedConstraintMasks);
    if (deviceRuleArgNestedAtomBucketMasks != nullptr) cudaFree(deviceRuleArgNestedAtomBucketMasks);
    if (deviceRuleArgNestedRepeatMasks != nullptr) cudaFree(deviceRuleArgNestedRepeatMasks);
    if (deviceRuleArgExactHeadMasks != nullptr) cudaFree(deviceRuleArgExactHeadMasks);
    if (deviceRuleArgHeadBuckets != nullptr) cudaFree(deviceRuleArgHeadBuckets);
    if (deviceRuleArgKinds != nullptr) cudaFree(deviceRuleArgKinds);
    if (deviceRuleArgConstraintMasks != nullptr) cudaFree(deviceRuleArgConstraintMasks);
    if (deviceRuleArgGroupIds != nullptr) cudaFree(deviceRuleArgGroupIds);
    if (deviceRuleArgStarts != nullptr) cudaFree(deviceRuleArgStarts);
    if (deviceDirectWildcardFlags != nullptr) cudaFree(deviceDirectWildcardFlags);
    if (deviceWildcardFlags != nullptr) cudaFree(deviceWildcardFlags);
    if (deviceRuleArities != nullptr) cudaFree(deviceRuleArities);
    if (deviceRuleHeadCodes != nullptr) cudaFree(deviceRuleHeadCodes);
    return 0;

cleanup_cached:
    if (deviceScores != nullptr) cudaFree(deviceScores);
    if (deviceRuleArgNestedTopLevelReferenceMasks != nullptr) cudaFree(deviceRuleArgNestedTopLevelReferenceMasks);
    if (deviceRuleArgNestedConstraintMasks != nullptr) cudaFree(deviceRuleArgNestedConstraintMasks);
    if (deviceRuleArgNestedAtomBucketMasks != nullptr) cudaFree(deviceRuleArgNestedAtomBucketMasks);
    if (deviceRuleArgNestedRepeatMasks != nullptr) cudaFree(deviceRuleArgNestedRepeatMasks);
    if (deviceRuleArgExactHeadMasks != nullptr) cudaFree(deviceRuleArgExactHeadMasks);
    if (deviceRuleArgHeadBuckets != nullptr) cudaFree(deviceRuleArgHeadBuckets);
    if (deviceRuleArgKinds != nullptr) cudaFree(deviceRuleArgKinds);
    if (deviceRuleArgConstraintMasks != nullptr) cudaFree(deviceRuleArgConstraintMasks);
    if (deviceRuleArgGroupIds != nullptr) cudaFree(deviceRuleArgGroupIds);
    if (deviceRuleArgStarts != nullptr) cudaFree(deviceRuleArgStarts);
    if (deviceDirectWildcardFlags != nullptr) cudaFree(deviceDirectWildcardFlags);
    if (deviceWildcardFlags != nullptr) cudaFree(deviceWildcardFlags);
    if (deviceRuleArities != nullptr) cudaFree(deviceRuleArities);
    if (deviceRuleHeadCodes != nullptr) cudaFree(deviceRuleHeadCodes);
    return 327;
}

extern "C" __declspec(dllexport) int cobra_extract_node_rule_pairs_fully_cached(
    int ruleArgCount,
    int ruleCount,
    int maxPairs,
    int* hostPairNodeIndices,
    int* hostPairRuleIndices,
    int* hostPairCount)
{
    if (hostPairNodeIndices == nullptr || hostPairRuleIndices == nullptr || hostPairCount == nullptr)
    {
        return 328;
    }
    if (g_cachedNodeHeadCodes == nullptr || g_cachedNodeArities == nullptr || g_cachedNodeChildStarts == nullptr ||
        g_cachedNodeChildIds == nullptr || g_cachedClassConstraintMasks == nullptr || g_cachedClassHeadBucketMasks == nullptr ||
        g_cachedClassExactHeadMasks == nullptr || g_cachedClassChildEqualityMasks == nullptr || g_cachedClassChildAtomBucketMasks == nullptr ||
        g_cachedClassChildConstraintMasks == nullptr || g_cachedClassChildReferenceBloomMasks == nullptr ||
        g_cachedRuleHeadCodes == nullptr || g_cachedRuleArities == nullptr || g_cachedWildcardFlags == nullptr ||
        g_cachedDirectWildcardFlags == nullptr || g_cachedRuleArgStarts == nullptr || g_cachedRuleArgGroupIds == nullptr ||
        g_cachedRuleArgConstraintMasks == nullptr || g_cachedRuleArgKinds == nullptr || g_cachedRuleArgHeadBuckets == nullptr ||
        g_cachedRuleArgExactHeadMasks == nullptr || g_cachedRuleArgNestedRepeatMasks == nullptr || g_cachedRuleArgNestedAtomBucketMasks == nullptr ||
        g_cachedRuleArgNestedConstraintMasks == nullptr || g_cachedRuleArgNestedTopLevelReferenceMasks == nullptr)
    {
        return 329;
    }
    if (g_cachedRuleArgCount != ruleArgCount || g_cachedRuleCount != ruleCount || g_cachedNodeRuleNodeCount <= 0 || maxPairs <= 0)
    {
        return 330;
    }

    int total = g_cachedNodeRuleNodeCount * ruleCount;
    int* deviceScores = nullptr;
    int* devicePairNodeIndices = nullptr;
    int* devicePairRuleIndices = nullptr;
    int* devicePairCount = nullptr;
    size_t scoreBytes = static_cast<size_t>(total) * sizeof(int);
    size_t pairBytes = static_cast<size_t>(maxPairs) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceScores, scoreBytes);
    if (err != cudaSuccess) return 331;
    err = cudaMalloc(&devicePairNodeIndices, pairBytes);
    if (err != cudaSuccess) goto cleanup_extract_error_332;
    err = cudaMalloc(&devicePairRuleIndices, pairBytes);
    if (err != cudaSuccess) goto cleanup_extract_error_333;
    err = cudaMalloc(&devicePairCount, sizeof(int));
    if (err != cudaSuccess) goto cleanup_extract_error_334;
    err = cudaMemset(devicePairCount, 0, sizeof(int));
    if (err != cudaSuccess) goto cleanup_extract_error_335;

    {
        int blockSize = 128;
        int gridSize = (total + blockSize - 1) / blockSize;
        scoreNodeRuleCandidatesKernel<<<gridSize, blockSize>>>(
            g_cachedNodeHeadCodes,
            g_cachedNodeArities,
            g_cachedNodeChildStarts,
            g_cachedNodeChildIds,
            g_cachedClassConstraintMasks,
            g_cachedClassHeadBucketMasks,
            g_cachedClassExactHeadMasks,
            g_cachedClassChildEqualityMasks,
            g_cachedClassChildAtomBucketMasks,
            g_cachedClassChildConstraintMasks,
            g_cachedClassChildReferenceBloomMasks,
            g_cachedNodeRuleNodeCount,
            g_cachedRuleHeadCodes,
            g_cachedRuleArities,
            g_cachedWildcardFlags,
            g_cachedDirectWildcardFlags,
            g_cachedRuleArgStarts,
            g_cachedRuleArgGroupIds,
            g_cachedRuleArgConstraintMasks,
            g_cachedRuleArgKinds,
            g_cachedRuleArgHeadBuckets,
            g_cachedRuleArgExactHeadMasks,
            g_cachedRuleArgNestedRepeatMasks,
            g_cachedRuleArgNestedAtomBucketMasks,
            g_cachedRuleArgNestedConstraintMasks,
            g_cachedRuleArgNestedTopLevelReferenceMasks,
            ruleCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_extract_error_336;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_extract_error_337;

    {
        int blockSize = 128;
        int gridSize = (total + blockSize - 1) / blockSize;
        collectPositiveNodeRulePairsKernel<<<gridSize, blockSize>>>(
            deviceScores,
            g_cachedNodeRuleNodeCount,
            ruleCount,
            maxPairs,
            devicePairNodeIndices,
            devicePairRuleIndices,
            devicePairCount);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_extract_error_338;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_extract_error_339;

    int pairCount = 0;
    err = cudaMemcpy(&pairCount, devicePairCount, sizeof(int), cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_extract_error_340;
    if (pairCount > maxPairs)
    {
        pairCount = maxPairs;
    }

    if (pairCount > 0)
    {
        err = cudaMemcpy(hostPairNodeIndices, devicePairNodeIndices, static_cast<size_t>(pairCount) * sizeof(int), cudaMemcpyDeviceToHost);
        if (err != cudaSuccess) goto cleanup_extract_error_341;
        err = cudaMemcpy(hostPairRuleIndices, devicePairRuleIndices, static_cast<size_t>(pairCount) * sizeof(int), cudaMemcpyDeviceToHost);
        if (err != cudaSuccess) goto cleanup_extract_error_342;
    }

    *hostPairCount = pairCount;
    cudaFree(deviceScores);
    cudaFree(devicePairNodeIndices);
    cudaFree(devicePairRuleIndices);
    cudaFree(devicePairCount);
    return 0;

cleanup_extract_error_342:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); cudaFree(devicePairCount); return 342;
cleanup_extract_error_341:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); cudaFree(devicePairCount); return 341;
cleanup_extract_error_340:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); cudaFree(devicePairCount); return 340;
cleanup_extract_error_339:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); cudaFree(devicePairCount); return 339;
cleanup_extract_error_338:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); cudaFree(devicePairCount); return 338;
cleanup_extract_error_337:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); cudaFree(devicePairCount); return 337;
cleanup_extract_error_336:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); cudaFree(devicePairCount); return 336;
cleanup_extract_error_335:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); cudaFree(devicePairCount); return 335;
cleanup_extract_error_334:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); cudaFree(devicePairRuleIndices); return 334;
cleanup_extract_error_333:
    cudaFree(deviceScores); cudaFree(devicePairNodeIndices); return 333;
cleanup_extract_error_332:
    cudaFree(deviceScores); return 332;
}

extern "C" __declspec(dllexport) int cobra_score_direct_pairs_v2(
    const int* classIds,
    const int* nodeArities,
    const int* generations,
    const int* nodeCounts,
    const int* nestedFlags,
    int pairCount,
    int* hostScores)
{
    if (classIds == nullptr || nodeArities == nullptr || generations == nullptr || nodeCounts == nullptr || nestedFlags == nullptr || hostScores == nullptr)
    {
        return 514;
    }
    if (pairCount <= 0)
    {
        return 515;
    }

    int* deviceClassIds = nullptr;
    int* deviceNodeArities = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceNestedFlags = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(pairCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) return 516;
    err = cudaMalloc(&deviceNodeArities, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 517; }
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); return 518; }
    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); return 519; }
    err = cudaMalloc(&deviceNestedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); return 520; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); return 521; }

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_522;
    err = cudaMemcpy(deviceNodeArities, nodeArities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_523;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_524;
    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_525;
    err = cudaMemcpy(deviceNestedFlags, nestedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_526;

    {
        int blockSize = 128;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        scoreDirectPairsV2Kernel<<<gridSize, blockSize>>>(deviceClassIds, deviceNodeArities, deviceGenerations, deviceNodeCounts, deviceNestedFlags, pairCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_527;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_528;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_529;

    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores);
    return 0;
cleanup_error_529:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 529;
cleanup_error_528:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 528;
cleanup_error_527:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 527;
cleanup_error_526:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 526;
cleanup_error_525:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 525;
cleanup_error_524:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 524;
cleanup_error_523:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 523;
cleanup_error_522:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 522;
}

extern "C" __declspec(dllexport) int cobra_score_direct_pairs_v2_cached(
    const int* classIds,
    const int* nodeArities,
    const int* nestedFlags,
    int pairCount,
    int* hostScores)
{
    if (classIds == nullptr || nodeArities == nullptr || nestedFlags == nullptr || hostScores == nullptr)
    {
        return 830;
    }
    if (pairCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 831;
    }

    int* deviceClassIds = nullptr;
    int* deviceNodeArities = nullptr;
    int* deviceNestedFlags = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(pairCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) return 832;
    err = cudaMalloc(&deviceNodeArities, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 833; }
    err = cudaMalloc(&deviceNestedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); return 834; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); return 835; }

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_836;
    err = cudaMemcpy(deviceNodeArities, nodeArities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_837;
    err = cudaMemcpy(deviceNestedFlags, nestedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_838;

    {
        int blockSize = 128;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        scoreDirectPairsV2Kernel<<<gridSize, blockSize>>>(
            deviceClassIds,
            deviceNodeArities,
            g_cachedClassGenerations,
            g_cachedClassNodeCounts,
            deviceNestedFlags,
            pairCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_839;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_840;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_841;

    cudaFree(deviceClassIds);
    cudaFree(deviceNodeArities);
    cudaFree(deviceNestedFlags);
    cudaFree(deviceScores);
    return 0;
cleanup_error_841:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 841;
cleanup_error_840:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 840;
cleanup_error_839:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 839;
cleanup_error_838:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 838;
cleanup_error_837:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 837;
cleanup_error_836:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 836;
}

extern "C" __declspec(dllexport) int cobra_score_direct_pairs_v2_by_id_cached(
    const int* classIds,
    const int* nodeArities,
    const int* nestedFlags,
    int pairCount,
    int* hostScores)
{
    if (classIds == nullptr || nodeArities == nullptr || nestedFlags == nullptr || hostScores == nullptr)
    {
        return 10990;
    }
    if (pairCount <= 0 || g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr || g_cachedClassCount <= 0)
    {
        return 10991;
    }

    int* deviceClassIds = nullptr;
    int* deviceNodeArities = nullptr;
    int* deviceNestedFlags = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(pairCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) return 10992;
    err = cudaMalloc(&deviceNodeArities, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 10993; }
    err = cudaMalloc(&deviceNestedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); return 10994; }
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); return 10995; }
    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); return 10996; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); return 10997; }

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10998;
    err = cudaMemcpy(deviceNodeArities, nodeArities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_10999;
    err = cudaMemcpy(deviceNestedFlags, nestedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_11000;

    {
        int blockSize = 128;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassGenerations, pairCount, deviceGenerations);
        gatherClassMetricKernel<<<gridSize, blockSize>>>(deviceClassIds, g_cachedClassNodeCounts, pairCount, deviceNodeCounts);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_11001;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_11002;

    {
        int blockSize = 128;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        scoreDirectPairsV2Kernel<<<gridSize, blockSize>>>(
            deviceClassIds,
            deviceNodeArities,
            deviceGenerations,
            deviceNodeCounts,
            deviceNestedFlags,
            pairCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_11003;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_11004;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_11005;

    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores);
    return 0;

cleanup_error_11005:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 11005;
cleanup_error_11004:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 11004;
cleanup_error_11003:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 11003;
cleanup_error_11002:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 11002;
cleanup_error_11001:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 11001;
cleanup_error_11000:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 11000;
cleanup_error_10999:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10999;
cleanup_error_10998:
    cudaFree(deviceClassIds); cudaFree(deviceNodeArities); cudaFree(deviceNestedFlags); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 10998;
}

__global__ void scoreMatchPriorityV3Kernel(
    const int* hotFlags,
    const int* boundaryFlags,
    const int* suppressedFlags,
    const int* ruleArities,
    const int* directFlags,
    const int* nestedFlags,
    int matchCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= matchCount)
    {
        return;
    }

    scores[index] =
        (hotFlags[index] != 0 ? 1000 : 0) +
        (boundaryFlags[index] != 0 ? 100 : 0) -
        (suppressedFlags[index] != 0 ? 1000 : 0) +
        (ruleArities[index] * 12) +
        (directFlags[index] != 0 ? 32 : 0) +
        (nestedFlags[index] != 0 ? 72 : 0);
}

extern "C" __declspec(dllexport) int cobra_score_match_priority_v3(
    const int* hotFlags,
    const int* boundaryFlags,
    const int* suppressedFlags,
    const int* ruleArities,
    const int* directFlags,
    const int* nestedFlags,
    int matchCount,
    int* hostScores)
{
    if (hotFlags == nullptr || boundaryFlags == nullptr || suppressedFlags == nullptr || ruleArities == nullptr || directFlags == nullptr || nestedFlags == nullptr || hostScores == nullptr)
    {
        return 530;
    }
    if (matchCount <= 0)
    {
        return 531;
    }

    int* deviceHotFlags = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceSuppressedFlags = nullptr;
    int* deviceRuleArities = nullptr;
    int* deviceDirectFlags = nullptr;
    int* deviceNestedFlags = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(matchCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceHotFlags, intBytes);
    if (err != cudaSuccess) return 532;
    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); return 533; }
    err = cudaMalloc(&deviceSuppressedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); return 534; }
    err = cudaMalloc(&deviceRuleArities, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); return 535; }
    err = cudaMalloc(&deviceDirectFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); return 536; }
    err = cudaMalloc(&deviceNestedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); return 537; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); return 538; }

    err = cudaMemcpy(deviceHotFlags, hotFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_539;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_540;
    err = cudaMemcpy(deviceSuppressedFlags, suppressedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_541;
    err = cudaMemcpy(deviceRuleArities, ruleArities, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_542;
    err = cudaMemcpy(deviceDirectFlags, directFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_543;
    err = cudaMemcpy(deviceNestedFlags, nestedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_544;

    {
        int blockSize = 128;
        int gridSize = (matchCount + blockSize - 1) / blockSize;
        scoreMatchPriorityV3Kernel<<<gridSize, blockSize>>>(deviceHotFlags, deviceBoundaryFlags, deviceSuppressedFlags, deviceRuleArities, deviceDirectFlags, deviceNestedFlags, matchCount, deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_545;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_546;
    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_547;

    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores);
    return 0;
cleanup_error_547:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 547;
cleanup_error_546:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 546;
cleanup_error_545:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 545;
cleanup_error_544:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 544;
cleanup_error_543:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 543;
cleanup_error_542:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 542;
cleanup_error_541:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 541;
cleanup_error_540:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 540;
cleanup_error_539:
    cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceRuleArities); cudaFree(deviceDirectFlags); cudaFree(deviceNestedFlags); cudaFree(deviceScores); return 539;
}

__global__ void scoreFrontierV3Kernel(
    const int* nodeCounts,
    const int* generations,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    const int* suppressedFlags,
    const int* hotRegionCounts,
    const int* boundaryRegionCounts,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    scores[index] =
        generations[index] * 8 +
        nodeCounts[index] * 2 +
        (hotFlags[index] != 0 ? 1600 : 0) -
        (boundaryFlags[index] != 0 ? 180 : 0) -
        (residualFlags[index] != 0 ? 140 : 0) -
        (suppressedFlags[index] != 0 ? 1200 : 0) +
        (hotRegionCounts[index] * 300) -
        (boundaryRegionCounts[index] * 40);
}

extern "C" __declspec(dllexport) int cobra_score_frontier_v3(
    const int* nodeCounts,
    const int* generations,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    const int* suppressedFlags,
    const int* hotRegionCounts,
    const int* boundaryRegionCounts,
    int classCount,
    int* hostScores)
{
    if (nodeCounts == nullptr || generations == nullptr || hotFlags == nullptr || boundaryFlags == nullptr || residualFlags == nullptr || suppressedFlags == nullptr ||
        hotRegionCounts == nullptr || boundaryRegionCounts == nullptr || hostScores == nullptr)
    {
        return 5001;
    }
    if (classCount <= 0)
    {
        return 5002;
    }

    int* deviceNodeCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceHotFlags = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceResidualFlags = nullptr;
    int* deviceSuppressedFlags = nullptr;
    int* deviceHotRegionCounts = nullptr;
    int* deviceBoundaryRegionCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) return 5003;
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); return 5004; }
    err = cudaMalloc(&deviceHotFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); return 5005; }
    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); return 5006; }
    err = cudaMalloc(&deviceResidualFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); return 5007; }
    err = cudaMalloc(&deviceSuppressedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); return 5008; }
    err = cudaMalloc(&deviceHotRegionCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceSuppressedFlags); return 5009; }
    err = cudaMalloc(&deviceBoundaryRegionCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceHotRegionCounts); return 5010; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); return 5011; }

    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5012;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5013;
    err = cudaMemcpy(deviceHotFlags, hotFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5014;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5015;
    err = cudaMemcpy(deviceResidualFlags, residualFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5016;
    err = cudaMemcpy(deviceSuppressedFlags, suppressedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5017;
    err = cudaMemcpy(deviceHotRegionCounts, hotRegionCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5018;
    err = cudaMemcpy(deviceBoundaryRegionCounts, boundaryRegionCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5019;

    {
        int blockSize = 256;
        int gridSize = (classCount + blockSize - 1) / blockSize;

        scoreFrontierV3Kernel<<<gridSize, blockSize>>>(
            deviceNodeCounts,
            deviceGenerations,
            deviceHotFlags,
            deviceBoundaryFlags,
            deviceResidualFlags,
            deviceSuppressedFlags,
            deviceHotRegionCounts,
            deviceBoundaryRegionCounts,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_5020;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_5021;

    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_5022;

    cudaFree(deviceNodeCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceHotRegionCounts);
    cudaFree(deviceBoundaryRegionCounts);
    cudaFree(deviceScores);
    return 0;

cleanup_error_5022:
cleanup_error_5021:
cleanup_error_5020:
cleanup_error_5019:
cleanup_error_5018:
cleanup_error_5017:
cleanup_error_5016:
cleanup_error_5015:
cleanup_error_5014:
cleanup_error_5013:
cleanup_error_5012:
    cudaFree(deviceNodeCounts); cudaFree(deviceGenerations); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 5012;
}

__global__ void scoreFrontierV3ByIdKernel(
    const int* classIds,
    const int* allNodeCounts,
    const int* allGenerations,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    const int* suppressedFlags,
    const int* hotRegionCounts,
    const int* boundaryRegionCounts,
    int classCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= classCount)
    {
        return;
    }

    int classId = classIds[index];
    scores[index] =
        allGenerations[classId] * 8 +
        allNodeCounts[classId] * 2 +
        (hotFlags[index] != 0 ? 1600 : 0) -
        (boundaryFlags[index] != 0 ? 180 : 0) -
        (residualFlags[index] != 0 ? 140 : 0) -
        (suppressedFlags[index] != 0 ? 1200 : 0) +
        (hotRegionCounts[index] * 300) -
        (boundaryRegionCounts[index] * 40);
}

extern "C" __declspec(dllexport) int cobra_score_frontier_v3_by_id_cached(
    const int* classIds,
    const int* hotFlags,
    const int* boundaryFlags,
    const int* residualFlags,
    const int* suppressedFlags,
    const int* hotRegionCounts,
    const int* boundaryRegionCounts,
    int classCount,
    int* hostScores)
{
    if (classIds == nullptr || hotFlags == nullptr || boundaryFlags == nullptr || residualFlags == nullptr || suppressedFlags == nullptr ||
        hotRegionCounts == nullptr || boundaryRegionCounts == nullptr || hostScores == nullptr)
    {
        return 5030;
    }
    if (classCount <= 0)
    {
        return 5031;
    }
    if (g_cachedClassNodeCounts == nullptr || g_cachedClassGenerations == nullptr)
    {
        return 5032;
    }

    int* deviceClassIds = nullptr;
    int* deviceHotFlags = nullptr;
    int* deviceBoundaryFlags = nullptr;
    int* deviceResidualFlags = nullptr;
    int* deviceSuppressedFlags = nullptr;
    int* deviceHotRegionCounts = nullptr;
    int* deviceBoundaryRegionCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(classCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceClassIds, intBytes);
    if (err != cudaSuccess) return 5033;
    err = cudaMalloc(&deviceHotFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); return 5034; }
    err = cudaMalloc(&deviceBoundaryFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceHotFlags); return 5035; }
    err = cudaMalloc(&deviceResidualFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); return 5036; }
    err = cudaMalloc(&deviceSuppressedFlags, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); return 5037; }
    err = cudaMalloc(&deviceHotRegionCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceSuppressedFlags); return 5038; }
    err = cudaMalloc(&deviceBoundaryRegionCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceHotRegionCounts); return 5039; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceClassIds); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); return 5040; }

    err = cudaMemcpy(deviceClassIds, classIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5041;
    err = cudaMemcpy(deviceHotFlags, hotFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5042;
    err = cudaMemcpy(deviceBoundaryFlags, boundaryFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5043;
    err = cudaMemcpy(deviceResidualFlags, residualFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5044;
    err = cudaMemcpy(deviceSuppressedFlags, suppressedFlags, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5045;
    err = cudaMemcpy(deviceHotRegionCounts, hotRegionCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5046;
    err = cudaMemcpy(deviceBoundaryRegionCounts, boundaryRegionCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_5047;

    {
        int blockSize = 256;
        int gridSize = (classCount + blockSize - 1) / blockSize;

        scoreFrontierV3ByIdKernel<<<gridSize, blockSize>>>(
            deviceClassIds,
            g_cachedClassNodeCounts,
            g_cachedClassGenerations,
            deviceHotFlags,
            deviceBoundaryFlags,
            deviceResidualFlags,
            deviceSuppressedFlags,
            deviceHotRegionCounts,
            deviceBoundaryRegionCounts,
            classCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_5048;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_5049;

    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_5050;

    cudaFree(deviceClassIds);
    cudaFree(deviceHotFlags);
    cudaFree(deviceBoundaryFlags);
    cudaFree(deviceResidualFlags);
    cudaFree(deviceSuppressedFlags);
    cudaFree(deviceHotRegionCounts);
    cudaFree(deviceBoundaryRegionCounts);
    cudaFree(deviceScores);
    return 0;

cleanup_error_5050:
cleanup_error_5049:
cleanup_error_5048:
cleanup_error_5047:
cleanup_error_5046:
cleanup_error_5045:
cleanup_error_5044:
cleanup_error_5043:
cleanup_error_5042:
cleanup_error_5041:
    cudaFree(deviceClassIds); cudaFree(deviceHotFlags); cudaFree(deviceBoundaryFlags); cudaFree(deviceResidualFlags); cudaFree(deviceSuppressedFlags); cudaFree(deviceHotRegionCounts); cudaFree(deviceBoundaryRegionCounts); cudaFree(deviceScores); return 5041;
}

__global__ void scoreRepairApplicationGroupsKernel(
    const int* anchorIds,
    const int* memberCounts,
    const int* generations,
    const int* nodeCounts,
    int groupCount,
    int* scores)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= groupCount)
    {
        return;
    }

    // Heuristic matches C# OrderByDescending/ThenBy
    // Primary: memberCount (Candidates.Count)
    // Secondary: generation
    // Tertiary: nodeCount (bonus)
    // Tie-breaker: anchorId (negative)
    scores[index] = 
        (memberCounts[index] * 1000) + 
        (generations[index] * 50) + 
        (nodeCounts[index] / 4) - 
        (anchorIds[index] % 100);
}

extern "C" __declspec(dllexport) int cobra_score_repair_application_groups(
    const int* anchorIds,
    const int* memberCounts,
    const int* generations,
    const int* nodeCounts,
    int groupCount,
    int* hostScores)
{
    if (anchorIds == nullptr || memberCounts == nullptr || generations == nullptr || nodeCounts == nullptr || hostScores == nullptr)
    {
        return 6001;
    }
    if (groupCount <= 0)
    {
        return 6002;
    }

    int* deviceAnchorIds = nullptr;
    int* deviceMemberCounts = nullptr;
    int* deviceGenerations = nullptr;
    int* deviceNodeCounts = nullptr;
    int* deviceScores = nullptr;
    size_t intBytes = static_cast<size_t>(groupCount) * sizeof(int);

    cudaError_t err = cudaMalloc(&deviceAnchorIds, intBytes);
    if (err != cudaSuccess) return 6003;
    err = cudaMalloc(&deviceMemberCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceAnchorIds); return 6004; }
    err = cudaMalloc(&deviceGenerations, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceAnchorIds); cudaFree(deviceMemberCounts); return 6005; }
    err = cudaMalloc(&deviceNodeCounts, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceAnchorIds); cudaFree(deviceMemberCounts); cudaFree(deviceGenerations); return 6006; }
    err = cudaMalloc(&deviceScores, intBytes);
    if (err != cudaSuccess) { cudaFree(deviceAnchorIds); cudaFree(deviceMemberCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); return 6007; }

    err = cudaMemcpy(deviceAnchorIds, anchorIds, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_6010;
    err = cudaMemcpy(deviceMemberCounts, memberCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_6011;
    err = cudaMemcpy(deviceGenerations, generations, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_6012;
    err = cudaMemcpy(deviceNodeCounts, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    if (err != cudaSuccess) goto cleanup_error_6013;

    {
        int blockSize = 128;
        int gridSize = (groupCount + blockSize - 1) / blockSize;
        scoreRepairApplicationGroupsKernel<<<gridSize, blockSize>>>(
            deviceAnchorIds,
            deviceMemberCounts,
            deviceGenerations,
            deviceNodeCounts,
            groupCount,
            deviceScores);
    }

    err = cudaGetLastError();
    if (err != cudaSuccess) goto cleanup_error_6014;
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) goto cleanup_error_6015;

    err = cudaMemcpy(hostScores, deviceScores, intBytes, cudaMemcpyDeviceToHost);
    if (err != cudaSuccess) goto cleanup_error_6016;

    cudaFree(deviceAnchorIds);
    cudaFree(deviceMemberCounts);
    cudaFree(deviceGenerations);
    cudaFree(deviceNodeCounts);
    cudaFree(deviceScores);
    return 0;

cleanup_error_6016:
cleanup_error_6015:
cleanup_error_6014:
cleanup_error_6013:
cleanup_error_6012:
cleanup_error_6011:
cleanup_error_6010:
    cudaFree(deviceAnchorIds); cudaFree(deviceMemberCounts); cudaFree(deviceGenerations); cudaFree(deviceNodeCounts); cudaFree(deviceScores); return 6010;
}

__global__ void scoreNodeRuleCandidatesBatchV3Kernel(
    const int* classNodeOffsets,
    const int* classRuleOffsets,
    const int* classOutputOffsets,
    const int* classNodeCounts,
    const int* classRuleCounts,
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    int totalOutputSize,
    int classCount,
    int* scores)
{
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid >= totalOutputSize) return;

    // Binary search to find classIdx such that classOutputOffsets[classIdx] <= tid
    int low = 0;
    int high = classCount - 1;
    int classIdx = 0;
    while (low <= high) {
        int mid = (low + high) / 2;
        if (classOutputOffsets[mid] <= tid) {
            classIdx = mid;
            low = mid + 1;
        } else {
            high = mid - 1;
        }
    }

    int localOffset = tid - classOutputOffsets[classIdx];
    int nRules = classRuleCounts[classIdx];
    int nodeIdxInClass = localOffset / nRules;
    int ruleIdxInClass = localOffset % nRules;

    int nodeIdx = classNodeOffsets[classIdx] + nodeIdxInClass;
    int ruleIdx = classRuleOffsets[classIdx] + ruleIdxInClass;

    int nHead = nodeHeadCodes[nodeIdx];
    int rHead = ruleHeadCodes[ruleIdx];
    int nArity = nodeArities[nodeIdx];
    int rArity = ruleArities[ruleIdx];

    bool match = (wildcardFlags[ruleIdx] != 0) || (nHead == rHead && nArity == rArity);
    scores[tid] = match ? 1 : 0;
}

extern "C" __declspec(dllexport) int cobra_score_node_rule_candidates_batch(
    const int* classNodeOffsets,
    const int* classRuleOffsets,
    const int* classOutputOffsets,
    const int* classNodeCounts,
    const int* classRuleCounts,
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    int totalOutputSize,
    int totalNodes,
    int totalRules,
    int classCount,
    int* hostScores)
{
    if (classNodeOffsets == nullptr || classRuleOffsets == nullptr || classOutputOffsets == nullptr ||
        classNodeCounts == nullptr || classRuleCounts == nullptr || nodeHeadCodes == nullptr ||
        nodeArities == nullptr || ruleHeadCodes == nullptr || ruleArities == nullptr ||
        wildcardFlags == nullptr || hostScores == nullptr)
    {
        return 7001;
    }
    if (totalOutputSize <= 0 || classCount <= 0)
    {
        return 7002;
    }

    int* d_classNodeOffsets = nullptr;
    int* d_classRuleOffsets = nullptr;
    int* d_classOutputOffsets = nullptr;
    int* d_classNodeCounts = nullptr;
    int* d_classRuleCounts = nullptr;
    int* d_nodeHeadCodes = nullptr;
    int* d_nodeArities = nullptr;
    int* d_ruleHeadCodes = nullptr;
    int* d_ruleArities = nullptr;
    int* d_wildcardFlags = nullptr;
    int* d_scores = nullptr;

    cudaError_t err;
    size_t classBytes = (size_t)classCount * sizeof(int);
    size_t nodeBytes = (size_t)totalNodes * sizeof(int);
    size_t ruleBytes = (size_t)totalRules * sizeof(int);
    size_t outputBytes = (size_t)totalOutputSize * sizeof(int);

    err = cudaMalloc(&d_classNodeOffsets, classBytes); if (err != cudaSuccess) return 7003;
    err = cudaMalloc(&d_classRuleOffsets, classBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); return 7004; }
    err = cudaMalloc(&d_classOutputOffsets, classBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); return 7005; }
    err = cudaMalloc(&d_classNodeCounts, classBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); return 7006; }
    err = cudaMalloc(&d_classRuleCounts, classBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); return 7007; }
    err = cudaMalloc(&d_nodeHeadCodes, nodeBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); return 7008; }
    err = cudaMalloc(&d_nodeArities, nodeBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); return 7009; }
    err = cudaMalloc(&d_ruleHeadCodes, ruleBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); return 7010; }
    err = cudaMalloc(&d_ruleArities, ruleBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); cudaFree(d_ruleHeadCodes); return 7011; }
    err = cudaMalloc(&d_wildcardFlags, ruleBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); cudaFree(d_ruleHeadCodes); cudaFree(d_ruleArities); return 7012; }
    err = cudaMalloc(&d_scores, outputBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); cudaFree(d_ruleHeadCodes); cudaFree(d_ruleArities); cudaFree(d_wildcardFlags); return 7013; }

    cudaMemcpy(d_classNodeOffsets, classNodeOffsets, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_classRuleOffsets, classRuleOffsets, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_classOutputOffsets, classOutputOffsets, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_classNodeCounts, classNodeCounts, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_classRuleCounts, classRuleCounts, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodeHeadCodes, nodeHeadCodes, nodeBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodeArities, nodeArities, nodeBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleHeadCodes, ruleHeadCodes, ruleBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArities, ruleArities, ruleBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_wildcardFlags, wildcardFlags, ruleBytes, cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        int gridSize = (totalOutputSize + blockSize - 1) / blockSize;
        scoreNodeRuleCandidatesBatchV3Kernel<<<gridSize, blockSize>>>(
            d_classNodeOffsets, d_classRuleOffsets, d_classOutputOffsets,
            d_classNodeCounts, d_classRuleCounts,
            d_nodeHeadCodes, d_nodeArities,
            d_ruleHeadCodes, d_ruleArities, d_wildcardFlags,
            totalOutputSize, classCount, d_scores);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostScores, d_scores, outputBytes, cudaMemcpyDeviceToHost);

    cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets);
    cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts);
    cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities);
    cudaFree(d_ruleHeadCodes); cudaFree(d_ruleArities); cudaFree(d_wildcardFlags);
    cudaFree(d_scores);

    return 0;
}

__global__ void scoreDirectMatchCandidatesBatchKernel(
    const int* classNodeOffsets,
    const int* classRuleOffsets,
    const int* classOutputOffsets,
    const int* classNodeCounts,
    const int* classRuleCounts,
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    int totalOutputSize,
    int classCount,
    int* scores)
{
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid >= totalOutputSize) return;

    // Binary search to find classIdx such that classOutputOffsets[classIdx] <= tid
    int low = 0;
    int high = classCount - 1;
    int classIdx = 0;
    while (low <= high) {
        int mid = (low + high) / 2;
        if (classOutputOffsets[mid] <= tid) {
            classIdx = mid;
            low = mid + 1;
        } else {
            high = mid - 1;
        }
    }

    int localOffset = tid - classOutputOffsets[classIdx];
    int nRules = classRuleCounts[classIdx];
    int nodeIdxInClass = localOffset / nRules;
    int ruleIdxInClass = localOffset % nRules;

    int nodeIdx = classNodeOffsets[classIdx] + nodeIdxInClass;
    int ruleIdx = classRuleOffsets[classIdx] + ruleIdxInClass;

    int nHead = nodeHeadCodes[nodeIdx];
    int rHead = ruleHeadCodes[ruleIdx];
    int nArity = nodeArities[nodeIdx];
    int rArity = ruleArities[ruleIdx];

    // Direct match: wildcard OR head/arity match
    bool match = (wildcardFlags[ruleIdx] != 0) || (nHead == rHead && nArity == rArity);
    scores[tid] = match ? 1 : 0;
}

extern "C" __declspec(dllexport) int cobra_score_direct_match_candidates_batch(
    const int* classNodeOffsets,
    const int* classRuleOffsets,
    const int* classOutputOffsets,
    const int* classNodeCounts,
    const int* classRuleCounts,
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    int totalOutputSize,
    int totalNodes,
    int totalRules,
    int classCount,
    int* hostScores)
{
    if (classNodeOffsets == nullptr || classRuleOffsets == nullptr || classOutputOffsets == nullptr ||
        classNodeCounts == nullptr || classRuleCounts == nullptr || nodeHeadCodes == nullptr ||
        nodeArities == nullptr || ruleHeadCodes == nullptr || ruleArities == nullptr ||
        wildcardFlags == nullptr || hostScores == nullptr)
    {
        return 8001;
    }
    if (totalOutputSize <= 0 || classCount <= 0)
    {
        return 8002;
    }

    int* d_classNodeOffsets = nullptr;
    int* d_classRuleOffsets = nullptr;
    int* d_classOutputOffsets = nullptr;
    int* d_classNodeCounts = nullptr;
    int* d_classRuleCounts = nullptr;
    int* d_nodeHeadCodes = nullptr;
    int* d_nodeArities = nullptr;
    int* d_ruleHeadCodes = nullptr;
    int* d_ruleArities = nullptr;
    int* d_wildcardFlags = nullptr;
    int* d_scores = nullptr;

    cudaError_t err;
    size_t classBytes = (size_t)classCount * sizeof(int);
    size_t nodeBytes = (size_t)totalNodes * sizeof(int);
    size_t ruleBytes = (size_t)totalRules * sizeof(int);
    size_t outputBytes = (size_t)totalOutputSize * sizeof(int);

    err = cudaMalloc(&d_classNodeOffsets, classBytes); if (err != cudaSuccess) return 8003;
    err = cudaMalloc(&d_classRuleOffsets, classBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); return 8004; }
    err = cudaMalloc(&d_classOutputOffsets, classBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); return 8005; }
    err = cudaMalloc(&d_classNodeCounts, classBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); return 8006; }
    err = cudaMalloc(&d_classRuleCounts, classBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); return 8007; }
    err = cudaMalloc(&d_nodeHeadCodes, nodeBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); return 8008; }
    err = cudaMalloc(&d_nodeArities, nodeBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); return 8009; }
    err = cudaMalloc(&d_ruleHeadCodes, ruleBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); return 8010; }
    err = cudaMalloc(&d_ruleArities, ruleBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); cudaFree(d_ruleHeadCodes); return 8011; }
    err = cudaMalloc(&d_wildcardFlags, ruleBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); cudaFree(d_ruleHeadCodes); cudaFree(d_ruleArities); return 8012; }
    err = cudaMalloc(&d_scores, outputBytes); if (err != cudaSuccess) { cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts); cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); cudaFree(d_ruleHeadCodes); cudaFree(d_ruleArities); cudaFree(d_wildcardFlags); return 8013; }

    cudaMemcpy(d_classNodeOffsets, classNodeOffsets, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_classRuleOffsets, classRuleOffsets, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_classOutputOffsets, classOutputOffsets, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_classNodeCounts, classNodeCounts, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_classRuleCounts, classRuleCounts, classBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodeHeadCodes, nodeHeadCodes, nodeBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodeArities, nodeArities, nodeBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleHeadCodes, ruleHeadCodes, ruleBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArities, ruleArities, ruleBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_wildcardFlags, wildcardFlags, ruleBytes, cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        int gridSize = (totalOutputSize + blockSize - 1) / blockSize;
        scoreDirectMatchCandidatesBatchKernel<<<gridSize, blockSize>>>(
            d_classNodeOffsets, d_classRuleOffsets, d_classOutputOffsets,
            d_classNodeCounts, d_classRuleCounts,
            d_nodeHeadCodes, d_nodeArities,
            d_ruleHeadCodes, d_ruleArities, d_wildcardFlags,
            totalOutputSize, classCount, d_scores);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostScores, d_scores, outputBytes, cudaMemcpyDeviceToHost);

    cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets);
    cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts);
    cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities);
    cudaFree(d_ruleHeadCodes); cudaFree(d_ruleArities); cudaFree(d_wildcardFlags);
    cudaFree(d_scores);

    return 0;
}

__global__ void scoreNodeRuleCandidatesBatchV4Kernel(
    const int* classNodeOffsets,
    const int* classRuleOffsets,
    const int* classOutputOffsets,
    const int* classNodeCounts,
    const int* classRuleCounts,
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* nodeChildStarts,
    const int* nodeChildIds,
    const int* classConstraintMasks,
    const int* classHeadBucketMasks,
    const int* classExactHeadMasks,
    const int* classChildEqualityMasks,
    const int* classChildAtomBucketMasks,
    const int* classChildConstraintMasks,
    const int* classChildReferenceBloomMasks,
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    const int* directWildcardFlags,
    const int* ruleArgStarts,
    const int* ruleArgGroupIds,
    const int* ruleArgConstraintMasks,
    const int* ruleArgKinds,
    const int* ruleArgHeadBuckets,
    const int* ruleArgExactHeadMasks,
    const int* ruleArgNestedRepeatMasks,
    const int* ruleArgNestedAtomBucketMasks,
    const int* ruleArgNestedConstraintMasks,
    const int* ruleArgNestedTopLevelReferenceMasks,
    int totalOutputSize,
    int classCount,
    int* scores)
{
    int tid = blockIdx.x * blockDim.x + threadIdx.x;
    if (tid >= totalOutputSize) return;

    int low = 0;
    int high = classCount - 1;
    int classIdx = 0;
    while (low <= high) {
        int mid = (low + high) / 2;
        if (classOutputOffsets[mid] <= tid) {
            classIdx = mid;
            low = mid + 1;
        } else {
            high = mid - 1;
        }
    }

    int localOffset = tid - classOutputOffsets[classIdx];
    int nRules = classRuleCounts[classIdx];
    int nodeIdxInClass = localOffset / nRules;
    int ruleIdxInClass = localOffset % nRules;

    int nodeIdx = classNodeOffsets[classIdx] + nodeIdxInClass;
    int ruleIdx = classRuleOffsets[classIdx] + ruleIdxInClass;

    int wildcard = wildcardFlags[ruleIdx];
    int nodeHead = nodeHeadCodes[nodeIdx];
    int ruleHead = ruleHeadCodes[ruleIdx];
    int nodeArity = nodeArities[nodeIdx];
    int ruleArity = ruleArities[ruleIdx];

    int compatible = 0;
    if (wildcard != 0) {
        compatible = 1;
    } else if (nodeHead == (1 << 30) || ruleHead == (1 << 30)) {
        compatible = 1;
    } else if (nodeHead == ruleHead && nodeArity == ruleArity) {
        compatible = 1;
    }

    if (compatible != 0 && directWildcardFlags[ruleIdx] != 0) {
        int nodeStart = nodeChildStarts[nodeIdx];
        int ruleStart = ruleArgStarts[ruleIdx];
        for (int i = 0; i < ruleArity; i++) {
            int childClassId = nodeChildIds[nodeStart + i];
            int argKind = ruleArgKinds[ruleStart + i];
            if (argKind == 1) {
                int exactHeadMask = ruleArgExactHeadMasks[ruleStart + i];
                int nestedRepeatMask = ruleArgNestedRepeatMasks[ruleStart + i];
                if (exactHeadMask != 0) {
                    if ((classExactHeadMasks[childClassId] & exactHeadMask) == 0) {
                        compatible = 0;
                        break;
                    }
                    if (nestedRepeatMask != 0 && (classChildEqualityMasks[childClassId] & nestedRepeatMask) != nestedRepeatMask) {
                        compatible = 0;
                        break;
                    }
                    for (int childIndex = 0; childIndex < 4; childIndex++) {
                        int requiredBucketMask = ruleArgNestedAtomBucketMasks[((ruleStart + i) * 4) + childIndex];
                        if (requiredBucketMask != 0 && (classChildAtomBucketMasks[(childClassId * 4) + childIndex] & requiredBucketMask) == 0) {
                            compatible = 0;
                            break;
                        }
                        int requiredConstraintMask = ruleArgNestedConstraintMasks[((ruleStart + i) * 4) + childIndex];
                        if (requiredConstraintMask != 0 && (classChildConstraintMasks[(childClassId * 4) + childIndex] & requiredConstraintMask) == 0) {
                            compatible = 0;
                            break;
                        }
                        int topLevelRefIndex = ruleArgNestedTopLevelReferenceMasks[((ruleStart + i) * 4) + childIndex];
                        if (topLevelRefIndex >= 0) {
                            int requiredChildClassId = nodeChildIds[nodeStart + topLevelRefIndex];
                            int requiredBloomBit = 1 << (abs(requiredChildClassId % 30));
                            if ((classChildReferenceBloomMasks[(childClassId * 4) + childIndex] & requiredBloomBit) == 0) {
                                compatible = 0;
                                break;
                            }
                        }
                    }
                    if (compatible == 0) break;
                    continue;
                }
                int headBucket = ruleArgHeadBuckets[ruleStart + i];
                if ((classHeadBucketMasks[childClassId] & (1 << headBucket)) == 0) {
                    compatible = 0;
                    break;
                }
                continue;
            }
            int requiredMask = ruleArgConstraintMasks[ruleStart + i];
            if (requiredMask != 0 && (classConstraintMasks[childClassId] & requiredMask) == 0) {
                compatible = 0;
                break;
            }
            int groupId = ruleArgGroupIds[ruleStart + i];
            if (groupId < i) {
                int leftChild = childClassId;
                int rightChild = nodeChildIds[nodeStart + groupId];
                if (leftChild != rightChild) {
                    compatible = 0;
                    break;
                }
            }
        }
    }
    scores[tid] = compatible;
}

extern "C" __declspec(dllexport) int cobra_score_node_rule_candidates_batch_v4(
    const int* classNodeOffsets,
    const int* classRuleOffsets,
    const int* classOutputOffsets,
    const int* classNodeCounts,
    const int* classRuleCounts,
    const int* nodeHeadCodes,
    const int* nodeArities,
    const int* nodeChildStarts,
    const int* nodeChildIds,
    const int* classConstraintMasks,
    const int* classHeadBucketMasks,
    const int* classExactHeadMasks,
    const int* classChildEqualityMasks,
    const int* classChildAtomBucketMasks,
    const int* classChildConstraintMasks,
    const int* classChildReferenceBloomMasks,
    const int* ruleHeadCodes,
    const int* ruleArities,
    const int* wildcardFlags,
    const int* directWildcardFlags,
    const int* ruleArgStarts,
    const int* ruleArgGroupIds,
    const int* ruleArgConstraintMasks,
    const int* ruleArgKinds,
    const int* ruleArgHeadBuckets,
    const int* ruleArgExactHeadMasks,
    const int* ruleArgNestedRepeatMasks,
    const int* ruleArgNestedAtomBucketMasks,
    const int* ruleArgNestedConstraintMasks,
    const int* ruleArgNestedTopLevelReferenceMasks,
    int totalOutputSize,
    int totalNodes,
    int totalRules,
    int totalNodeChildren,
    int totalRuleArgs,
    int classCount,
    int graphClassCount,
    int* hostScores)
{
    if (totalOutputSize <= 0 || classCount <= 0 || graphClassCount <= 0) return 9001;

    int* d_classNodeOffsets;
    int* d_classRuleOffsets;
    int* d_classOutputOffsets;
    int* d_classNodeCounts;
    int* d_classRuleCounts;
    int* d_nodeHeadCodes;
    int* d_nodeArities;
    int* d_nodeChildStarts;
    int* d_nodeChildIds;
    int* d_classConstraintMasks;
    int* d_classHeadBucketMasks;
    int* d_classExactHeadMasks;
    int* d_classChildEqualityMasks;
    int* d_classChildAtomBucketMasks;
    int* d_classChildConstraintMasks;
    int* d_classChildReferenceBloomMasks;
    int* d_ruleHeadCodes;
    int* d_ruleArities;
    int* d_wildcardFlags;
    int* d_directWildcardFlags;
    int* d_ruleArgStarts;
    int* d_ruleArgGroupIds;
    int* d_ruleArgConstraintMasks;
    int* d_ruleArgKinds;
    int* d_ruleArgHeadBuckets;
    int* d_ruleArgExactHeadMasks;
    int* d_ruleArgNestedRepeatMasks;
    int* d_ruleArgNestedAtomBucketMasks;
    int* d_ruleArgNestedConstraintMasks;
    int* d_ruleArgNestedTopLevelReferenceMasks;
    int* d_scores;

    cudaMalloc(&d_classNodeOffsets, classCount * sizeof(int));
    cudaMalloc(&d_classRuleOffsets, classCount * sizeof(int));
    cudaMalloc(&d_classOutputOffsets, classCount * sizeof(int));
    cudaMalloc(&d_classNodeCounts, classCount * sizeof(int));
    cudaMalloc(&d_classRuleCounts, classCount * sizeof(int));
    cudaMalloc(&d_nodeHeadCodes, totalNodes * sizeof(int));
    cudaMalloc(&d_nodeArities, totalNodes * sizeof(int));
    cudaMalloc(&d_nodeChildStarts, totalNodes * sizeof(int));
    cudaMalloc(&d_nodeChildIds, totalNodeChildren * sizeof(int));
    cudaMalloc(&d_classConstraintMasks, graphClassCount * sizeof(int));
    cudaMalloc(&d_classHeadBucketMasks, graphClassCount * sizeof(int));
    cudaMalloc(&d_classExactHeadMasks, graphClassCount * sizeof(int));
    cudaMalloc(&d_classChildEqualityMasks, graphClassCount * sizeof(int));
    cudaMalloc(&d_classChildAtomBucketMasks, graphClassCount * 4 * sizeof(int));
    cudaMalloc(&d_classChildConstraintMasks, graphClassCount * 4 * sizeof(int));
    cudaMalloc(&d_classChildReferenceBloomMasks, graphClassCount * 4 * sizeof(int));
    cudaMalloc(&d_ruleHeadCodes, totalRules * sizeof(int));
    cudaMalloc(&d_ruleArities, totalRules * sizeof(int));
    cudaMalloc(&d_wildcardFlags, totalRules * sizeof(int));
    cudaMalloc(&d_directWildcardFlags, totalRules * sizeof(int));
    cudaMalloc(&d_ruleArgStarts, totalRules * sizeof(int));
    cudaMalloc(&d_ruleArgGroupIds, totalRuleArgs * sizeof(int));
    cudaMalloc(&d_ruleArgConstraintMasks, totalRuleArgs * sizeof(int));
    cudaMalloc(&d_ruleArgKinds, totalRuleArgs * sizeof(int));
    cudaMalloc(&d_ruleArgHeadBuckets, totalRuleArgs * sizeof(int));
    cudaMalloc(&d_ruleArgExactHeadMasks, totalRuleArgs * sizeof(int));
    cudaMalloc(&d_ruleArgNestedRepeatMasks, totalRuleArgs * sizeof(int));
    cudaMalloc(&d_ruleArgNestedAtomBucketMasks, totalRuleArgs * 4 * sizeof(int));
    cudaMalloc(&d_ruleArgNestedConstraintMasks, totalRuleArgs * 4 * sizeof(int));
    cudaMalloc(&d_ruleArgNestedTopLevelReferenceMasks, totalRuleArgs * 4 * sizeof(int));
    cudaMalloc(&d_scores, totalOutputSize * sizeof(int));

    cudaMemcpy(d_classNodeOffsets, classNodeOffsets, classCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classRuleOffsets, classRuleOffsets, classCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classOutputOffsets, classOutputOffsets, classCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classNodeCounts, classNodeCounts, classCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classRuleCounts, classRuleCounts, classCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodeHeadCodes, nodeHeadCodes, totalNodes * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodeArities, nodeArities, totalNodes * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodeChildStarts, nodeChildStarts, totalNodes * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodeChildIds, nodeChildIds, totalNodeChildren * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classConstraintMasks, classConstraintMasks, graphClassCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classHeadBucketMasks, classHeadBucketMasks, graphClassCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classExactHeadMasks, classExactHeadMasks, graphClassCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classChildEqualityMasks, classChildEqualityMasks, graphClassCount * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classChildAtomBucketMasks, classChildAtomBucketMasks, graphClassCount * 4 * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classChildConstraintMasks, classChildConstraintMasks, graphClassCount * 4 * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_classChildReferenceBloomMasks, classChildReferenceBloomMasks, graphClassCount * 4 * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleHeadCodes, ruleHeadCodes, totalRules * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArities, ruleArities, totalRules * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_wildcardFlags, wildcardFlags, totalRules * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_directWildcardFlags, directWildcardFlags, totalRules * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgStarts, ruleArgStarts, totalRules * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgGroupIds, ruleArgGroupIds, totalRuleArgs * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgConstraintMasks, ruleArgConstraintMasks, totalRuleArgs * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgKinds, ruleArgKinds, totalRuleArgs * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgHeadBuckets, ruleArgHeadBuckets, totalRuleArgs * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgExactHeadMasks, ruleArgExactHeadMasks, totalRuleArgs * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgNestedRepeatMasks, ruleArgNestedRepeatMasks, totalRuleArgs * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgNestedAtomBucketMasks, ruleArgNestedAtomBucketMasks, totalRuleArgs * 4 * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgNestedConstraintMasks, ruleArgNestedConstraintMasks, totalRuleArgs * 4 * sizeof(int), cudaMemcpyHostToDevice);
    cudaMemcpy(d_ruleArgNestedTopLevelReferenceMasks, ruleArgNestedTopLevelReferenceMasks, totalRuleArgs * 4 * sizeof(int), cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        int gridSize = (totalOutputSize + blockSize - 1) / blockSize;
        scoreNodeRuleCandidatesBatchV4Kernel<<<gridSize, blockSize>>>(
            d_classNodeOffsets, d_classRuleOffsets, d_classOutputOffsets, d_classNodeCounts, d_classRuleCounts,
            d_nodeHeadCodes, d_nodeArities, d_nodeChildStarts, d_nodeChildIds,
            d_classConstraintMasks, d_classHeadBucketMasks, d_classExactHeadMasks, d_classChildEqualityMasks,
            d_classChildAtomBucketMasks, d_classChildConstraintMasks, d_classChildReferenceBloomMasks,
            d_ruleHeadCodes, d_ruleArities, d_wildcardFlags, d_directWildcardFlags, d_ruleArgStarts,
            d_ruleArgGroupIds, d_ruleArgConstraintMasks, d_ruleArgKinds, d_ruleArgHeadBuckets, d_ruleArgExactHeadMasks,
            d_ruleArgNestedRepeatMasks, d_ruleArgNestedAtomBucketMasks, d_ruleArgNestedConstraintMasks, d_ruleArgNestedTopLevelReferenceMasks,
            totalOutputSize, classCount, d_scores);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostScores, d_scores, totalOutputSize * sizeof(int), cudaMemcpyDeviceToHost);

    cudaFree(d_classNodeOffsets); cudaFree(d_classRuleOffsets); cudaFree(d_classOutputOffsets); cudaFree(d_classNodeCounts); cudaFree(d_classRuleCounts);
    cudaFree(d_nodeHeadCodes); cudaFree(d_nodeArities); cudaFree(d_nodeChildStarts); cudaFree(d_nodeChildIds);
    cudaFree(d_classConstraintMasks); cudaFree(d_classHeadBucketMasks); cudaFree(d_classExactHeadMasks); cudaFree(d_classChildEqualityMasks);
    cudaFree(d_classChildAtomBucketMasks); cudaFree(d_classChildConstraintMasks); cudaFree(d_classChildReferenceBloomMasks);
    cudaFree(d_ruleHeadCodes); cudaFree(d_ruleArities); cudaFree(d_wildcardFlags); cudaFree(d_directWildcardFlags); cudaFree(d_ruleArgStarts);
    cudaFree(d_ruleArgGroupIds); cudaFree(d_ruleArgConstraintMasks); cudaFree(d_ruleArgKinds); cudaFree(d_ruleArgHeadBuckets); cudaFree(d_ruleArgExactHeadMasks);
    cudaFree(d_ruleArgNestedRepeatMasks); cudaFree(d_ruleArgNestedAtomBucketMasks); cudaFree(d_ruleArgNestedConstraintMasks); cudaFree(d_ruleArgNestedTopLevelReferenceMasks);
    cudaFree(d_scores);

    return 0;
}

// Region Detection Constants (Bucket-based)
#define BUCKET_ADD 0
#define BUCKET_MUL 1
#define BUCKET_MATMUL 2
#define BUCKET_TRANSPOSE 3
#define BUCKET_RELU 4
#define BUCKET_EQUALITY 5
#define BUCKET_VECTOR 6

// Exact Head Masks (Matching CobraNodeMatchEncoding)
#define EXACT_MATMUL_MASK (1 << 5)
#define EXACT_FUSED_MATMUL_ADD_MASK (1 << 6)
#define EXACT_FUSED_MATMUL_ADD_RELU_MASK (1 << 7)
#define EXACT_TRANSPOSE_MASK (1 << 8)
#define EXACT_RELU_MASK (1 << 9)
#define EXACT_ADD_MASK (1 << 0)
#define EXACT_TENSOR_ADD_MASK (1 << 1)
#define EXACT_VECTOR_MASK (1 << 11)

// Region Families
#define REGION_UNKNOWN 0
#define REGION_SHARED_SINK 1
#define REGION_LEFT_FACTOR_PACK 2
#define REGION_RIGHT_FACTOR_PACK 3
#define REGION_BILINEAR_OVERLAP 4
#define REGION_RESIDUAL_CORE_BUNDLE 5
#define REGION_TRANSPOSE_BOUNDARY_CORE 6

__global__ void detectRegionsBatchKernel(
    const int* classHeadBucketMasks,
    const int* classExactHeadMasks,
    int classCount,
    int* familyCodes)
{
    int classId = blockIdx.x * blockDim.x + threadIdx.x;
    if (classId >= classCount) return;

    int bucketMask = classHeadBucketMasks[classId];
    int exactMask = classExactHeadMasks[classId];

    // Detection logic from CobraRegionDetector.cs
    bool hasMatMul = (exactMask & (EXACT_MATMUL_MASK | EXACT_FUSED_MATMUL_ADD_MASK | EXACT_FUSED_MATMUL_ADD_RELU_MASK)) != 0;
    bool hasTranspose = (exactMask & EXACT_TRANSPOSE_MASK) != 0;
    bool hasTensorAdd = (exactMask & (EXACT_ADD_MASK | EXACT_TENSOR_ADD_MASK)) != 0;
    bool hasResidual = hasTensorAdd || (exactMask & EXACT_VECTOR_MASK) != 0;
    bool hasFused = (exactMask & (EXACT_FUSED_MATMUL_ADD_MASK | EXACT_FUSED_MATMUL_ADD_RELU_MASK)) != 0;

    int family = REGION_UNKNOWN;

    if (hasTranspose && hasMatMul) {
        family = REGION_TRANSPOSE_BOUNDARY_CORE;
    } else if (hasMatMul && hasFused) {
        family = REGION_SHARED_SINK;
    } else if (hasMatMul && hasTensorAdd) {
        family = REGION_BILINEAR_OVERLAP;
    } else if (hasResidual && hasMatMul) {
        family = REGION_RESIDUAL_CORE_BUNDLE;
    } else if (hasMatMul) {
        family = REGION_LEFT_FACTOR_PACK;
    }

    familyCodes[classId] = family;
}

extern "C" __declspec(dllexport) int cobra_detect_regions_batch(
    const int* classHeadBucketMasks,
    const int* classExactHeadMasks,
    int classCount,
    int* hostFamilyCodes)
{
    if (classHeadBucketMasks == nullptr || classExactHeadMasks == nullptr || hostFamilyCodes == nullptr) return 10001;
    if (classCount <= 0) return 10002;

    int* d_bucketMasks;
    int* d_exactMasks;
    int* d_familyCodes;
    size_t bytes = (size_t)classCount * sizeof(int);

    cudaMalloc(&d_bucketMasks, bytes);
    cudaMalloc(&d_exactMasks, bytes);
    cudaMalloc(&d_familyCodes, bytes);

    cudaMemcpy(d_bucketMasks, classHeadBucketMasks, bytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_exactMasks, classExactHeadMasks, bytes, cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        detectRegionsBatchKernel<<<gridSize, blockSize>>>(d_bucketMasks, d_exactMasks, classCount, d_familyCodes);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostFamilyCodes, d_familyCodes, bytes, cudaMemcpyDeviceToHost);

    cudaFree(d_bucketMasks);
    cudaFree(d_exactMasks);
    cudaFree(d_familyCodes);

    return 0;
}

extern "C" __declspec(dllexport) int cobra_cache_region_detection_masks(
    const int* classHeadBucketMasks,
    const int* classExactHeadMasks,
    int classCount)
{
    if (classHeadBucketMasks == nullptr || classExactHeadMasks == nullptr || classCount <= 0)
    {
        return 10013;
    }

    if (g_cachedRegionClassCount != classCount)
    {
        freeRegionDetectionCache();
        cudaError_t err = cudaMalloc(&g_cachedRegionHeadBucketMasks, static_cast<size_t>(classCount) * sizeof(int));
        if (err != cudaSuccess) { freeRegionDetectionCache(); return 10014; }
        err = cudaMalloc(&g_cachedRegionExactHeadMasks, static_cast<size_t>(classCount) * sizeof(int));
        if (err != cudaSuccess) { freeRegionDetectionCache(); return 10015; }
        g_cachedRegionClassCount = classCount;
    }

    cudaError_t err = cudaMemcpy(g_cachedRegionHeadBucketMasks, classHeadBucketMasks, static_cast<size_t>(classCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeRegionDetectionCache(); return 10016; }
    err = cudaMemcpy(g_cachedRegionExactHeadMasks, classExactHeadMasks, static_cast<size_t>(classCount) * sizeof(int), cudaMemcpyHostToDevice);
    if (err != cudaSuccess) { freeRegionDetectionCache(); return 10017; }

    return 0;
}

extern "C" __declspec(dllexport) int cobra_detect_regions_batch_cached(
    int classCount,
    int* hostFamilyCodes)
{
    if (hostFamilyCodes == nullptr) return 10010;
    if (classCount <= 0) return 10011;
    if (g_cachedRegionHeadBucketMasks == nullptr || g_cachedRegionExactHeadMasks == nullptr || g_cachedRegionClassCount != classCount) return 10012;

    int* d_familyCodes;
    size_t bytes = (size_t)classCount * sizeof(int);
    cudaMalloc(&d_familyCodes, bytes);

    {
        int blockSize = 256;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        detectRegionsBatchKernel<<<gridSize, blockSize>>>(g_cachedRegionHeadBucketMasks, g_cachedRegionExactHeadMasks, classCount, d_familyCodes);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostFamilyCodes, d_familyCodes, bytes, cudaMemcpyDeviceToHost);
    cudaFree(d_familyCodes);

    return 0;
}

// Region Families (Matching CobraRegionFamily.cs)
#define REG_UNKNOWN 0
#define REG_SHARED_SINK 1
#define REG_LEFT_FACTOR_PACK 2
#define REG_RIGHT_FACTOR_PACK 3
#define REG_BILINEAR_OVERLAP 4
#define REG_RESIDUAL_CORE_BUNDLE 5
#define REG_TRANSPOSE_BOUNDARY_CORE 6

__global__ void scoreRegionsV2Kernel(
    const int* familyCodes,
    const int* nodeCounts,
    const int* boundaryCounts,
    int regionCount,
    double* benefitScores,
    double* conflictScores)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= regionCount) return;

    int family = familyCodes[i];
    int nNodes = nodeCounts[i];
    int nBoundary = boundaryCounts[i];

    double benefit = 0.0;
    double conflict = 0.0;

    switch (family) {
        case REG_TRANSPOSE_BOUNDARY_CORE: benefit = 6.0; conflict = 2.0; break;
        case REG_SHARED_SINK:             benefit = 8.0; conflict = 3.0; break;
        case REG_BILINEAR_OVERLAP:        benefit = 7.0; conflict = 5.0; break;
        case REG_RESIDUAL_CORE_BUNDLE:    benefit = 6.0; conflict = 4.0; break;
        case REG_LEFT_FACTOR_PACK:        benefit = 4.0; conflict = 2.0; break;
        case REG_RIGHT_FACTOR_PACK:       benefit = 4.0; conflict = 2.0; break;
        default:                          benefit = 0.0; conflict = 0.0; break;
    }

    // Apply scaling heuristics from CPU side
    benefit += (nNodes < 4 ? (double)nNodes : 4.0);
    conflict += (nBoundary > 2 ? (double)(nBoundary - 2) : 0.0);

    benefitScores[i] = benefit;
    conflictScores[i] = conflict;
}

extern "C" __declspec(dllexport) int cobra_score_regions_v2(
    const int* familyCodes,
    const int* nodeCounts,
    const int* boundaryCounts,
    int regionCount,
    double* hostBenefitScores,
    double* hostConflictScores)
{
    if (familyCodes == nullptr || nodeCounts == nullptr || boundaryCounts == nullptr || hostBenefitScores == nullptr || hostConflictScores == nullptr) return 11001;
    if (regionCount <= 0) return 11002;

    int *d_family, *d_nodes, *d_boundary;
    double *d_benefit, *d_conflict;
    size_t intBytes = (size_t)regionCount * sizeof(int);
    size_t doubleBytes = (size_t)regionCount * sizeof(double);

    cudaMalloc(&d_family, intBytes);
    cudaMalloc(&d_nodes, intBytes);
    cudaMalloc(&d_boundary, intBytes);
    cudaMalloc(&d_benefit, doubleBytes);
    cudaMalloc(&d_conflict, doubleBytes);

    cudaMemcpy(d_family, familyCodes, intBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_nodes, nodeCounts, intBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_boundary, boundaryCounts, intBytes, cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        int gridSize = (regionCount + blockSize - 1) / blockSize;
        scoreRegionsV2Kernel<<<gridSize, blockSize>>>(d_family, d_nodes, d_boundary, regionCount, d_benefit, d_conflict);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostBenefitScores, d_benefit, doubleBytes, cudaMemcpyDeviceToHost);
    cudaMemcpy(hostConflictScores, d_conflict, doubleBytes, cudaMemcpyDeviceToHost);

    cudaFree(d_family); cudaFree(d_nodes); cudaFree(d_boundary);
    cudaFree(d_benefit); cudaFree(d_conflict);

    return 0;
}

__global__ void initLabelsKernel(int* labels, int classCount) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < classCount) labels[i] = i;
}

__global__ void propagateLabelsKernel(const int* leftIds, const int* rightIds, int pairCount, int* labels, bool* changed) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= pairCount) return;

    int u = leftIds[i];
    int v = rightIds[i];
    int labelU = labels[u];
    int labelV = labels[v];

    if (labelU < labelV) {
        atomicMin(&labels[v], labelU);
        *changed = true;
    } else if (labelV < labelU) {
        atomicMin(&labels[u], labelV);
        *changed = true;
    }
}

__global__ void flattenLabelsKernel(int* labels, int classCount) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < classCount) {
        int root = i;
        while (labels[root] != root) {
            root = labels[root];
        }
        labels[i] = root;
    }
}

__global__ void mapLabelsToKeysKernel(const int* leftIds, const int* labels, int pairCount, int* groupKeys) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < pairCount) {
        groupKeys[i] = labels[leftIds[i]];
    }
}

extern "C" __declspec(dllexport) int cobra_group_unions_v2(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int maxClassId,
    int* hostGroupKeys)
{
    if (leftIds == nullptr || rightIds == nullptr || hostGroupKeys == nullptr) return 12001;
    if (pairCount <= 0 || maxClassId < 0) return 12002;

    int classCount = maxClassId + 1;
    int *d_left, *d_right, *d_labels, *d_keys;
    bool *d_changed, h_changed;
    size_t pairBytes = (size_t)pairCount * sizeof(int);
    size_t labelBytes = (size_t)classCount * sizeof(int);

    cudaMalloc(&d_left, pairBytes);
    cudaMalloc(&d_right, pairBytes);
    cudaMalloc(&d_labels, labelBytes);
    cudaMalloc(&d_keys, pairBytes);
    cudaMalloc(&d_changed, sizeof(bool));

    cudaMemcpy(d_left, leftIds, pairBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_right, rightIds, pairBytes, cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        initLabelsKernel<<<(classCount + blockSize - 1) / blockSize, blockSize>>>(d_labels, classCount);
    }

    for (int iter = 0; iter < 100; iter++) {
        h_changed = false;
        cudaMemcpy(d_changed, &h_changed, sizeof(bool), cudaMemcpyHostToDevice);
        int blockSize = 256;
        propagateLabelsKernel<<<(pairCount + blockSize - 1) / blockSize, blockSize>>>(d_left, d_right, pairCount, d_labels, d_changed);
        cudaMemcpy(&h_changed, d_changed, sizeof(bool), cudaMemcpyDeviceToHost);
        if (!h_changed) break;
    }

    {
        int blockSize = 256;
        flattenLabelsKernel<<<(classCount + blockSize - 1) / blockSize, blockSize>>>(d_labels, classCount);
        mapLabelsToKeysKernel<<<(pairCount + blockSize - 1) / blockSize, blockSize>>>(d_left, d_labels, pairCount, d_keys);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostGroupKeys, d_keys, pairBytes, cudaMemcpyDeviceToHost);

    cudaFree(d_left); cudaFree(d_right); cudaFree(d_labels); cudaFree(d_keys); cudaFree(d_changed);

    return 0;
}

__device__ int find_root(int* parents, int id) {
    int root = id;
    while (parents[root] != root) {
        root = parents[root];
    }
    // Path compression
    while (parents[id] != root) {
        int next = parents[id];
        parents[id] = root;
        id = next;
    }
    return root;
}

__global__ void unionBatchKernel(
    int* parents,
    int parentCount,
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* changedFlags)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= pairCount) return;

    int u = leftIds[i];
    int v = rightIds[i];

    if (u < 0 || u >= parentCount || v < 0 || v >= parentCount) return;

    int rootU = find_root(parents, u);
    int rootV = find_root(parents, v);

    if (rootU != rootV) {
        // Simple union by smaller ID to avoid cycles in parallel
        if (rootU < rootV) {
            atomicMin(&parents[rootV], rootU);
        } else {
            atomicMin(&parents[rootU], rootV);
        }
        *changedFlags = 1;
    }
}

extern "C" __declspec(dllexport) int cobra_union_batch_gpu_cached(
    const int* leftIds,
    const int* rightIds,
    int pairCount,
    int* hostChanged)
{
    if (g_cachedParents == nullptr || g_cachedParentCount <= 0 || leftIds == nullptr || rightIds == nullptr || hostChanged == nullptr) {
        return 13001;
    }
    if (pairCount <= 0) return 0;

    int *d_left, *d_right, *d_changed;
    size_t pairBytes = (size_t)pairCount * sizeof(int);

    cudaMalloc(&d_left, pairBytes);
    cudaMalloc(&d_right, pairBytes);
    cudaMalloc(&d_changed, sizeof(int));

    cudaMemcpy(d_left, leftIds, pairBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_right, rightIds, pairBytes, cudaMemcpyHostToDevice);
    int zero = 0;
    cudaMemcpy(d_changed, &zero, sizeof(int), cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        int gridSize = (pairCount + blockSize - 1) / blockSize;
        unionBatchKernel<<<gridSize, blockSize>>>(g_cachedParents, g_cachedParentCount, d_left, d_right, pairCount, d_changed);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostChanged, d_changed, sizeof(int), cudaMemcpyDeviceToHost);

    cudaFree(d_left);
    cudaFree(d_right);
    cudaFree(d_changed);

    return 0;
}

extern "C" __declspec(dllexport) int cobra_get_parent_snapshot(
    int* hostParents,
    int parentCount)
{
    if (g_cachedParents == nullptr || g_cachedParentCount <= 0 || hostParents == nullptr)
    {
        return 1003;
    }

    if (parentCount != g_cachedParentCount)
    {
        return 1004;
    }

    cudaError_t err = cudaMemcpy(
        hostParents,
        g_cachedParents,
        static_cast<size_t>(g_cachedParentCount) * sizeof(int),
        cudaMemcpyDeviceToHost);

    if (err != cudaSuccess)
    {
        return 1005;
    }

    return 0;
}

__device__ int find_root_readonly(const int* parents, int id) {
    while (parents[id] != id) {
        id = parents[id];
    }
    return id;
}

__global__ void canonicalizeAndHashNodesKernel(
    const int* nodeHeadHashes,
    const int* nodeChildStarts,
    const int* nodeChildCounts,
    const int* nodeChildIds,
    const int* parents,
    int nodeCount,
    int* nodeHashes)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= nodeCount) return;

    unsigned int hash = static_cast<unsigned int>(nodeHeadHashes[index]) ^ 2166136261u;
    int start = nodeChildStarts[index];
    int count = nodeChildCounts[index];

    for (int i = 0; i < count; i++) {
        int childId = nodeChildIds[start + i];
        int canonicalId = find_root_readonly(parents, childId);
        
        hash ^= static_cast<unsigned int>(canonicalId) + 0x9e3779b9u + (hash << 6) + (hash >> 2);
        hash *= 16777619u;
    }

    nodeHashes[index] = static_cast<int>(hash & 0x7fffffff);
}

extern "C" __declspec(dllexport) int cobra_canonicalize_and_hash_nodes_cached(
    const int* nodeHeadHashes,
    const int* nodeChildStarts,
    const int* nodeChildCounts,
    const int* nodeChildIds,
    int nodeCount,
    int totalChildCount,
    int* hostHashes)
{
    if (g_cachedParents == nullptr || g_cachedParentCount <= 0 || nodeHeadHashes == nullptr || 
        nodeChildStarts == nullptr || nodeChildCounts == nullptr || nodeChildIds == nullptr || hostHashes == nullptr) {
        return 14001;
    }
    if (nodeCount <= 0) return 0;

    int *d_head, *d_starts, *d_counts, *d_ids, *d_hashes;
    size_t nodeIntBytes = (size_t)nodeCount * sizeof(int);
    size_t childBytes = (size_t)totalChildCount * sizeof(int);

    cudaMalloc(&d_head, nodeIntBytes);
    cudaMalloc(&d_starts, nodeIntBytes);
    cudaMalloc(&d_counts, nodeIntBytes);
    cudaMalloc(&d_ids, childBytes);
    cudaMalloc(&d_hashes, nodeIntBytes);

    cudaMemcpy(d_head, nodeHeadHashes, nodeIntBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_starts, nodeChildStarts, nodeIntBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_counts, nodeChildCounts, nodeIntBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_ids, nodeChildIds, childBytes, cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        int gridSize = (nodeCount + blockSize - 1) / blockSize;
        canonicalizeAndHashNodesKernel<<<gridSize, blockSize>>>(d_head, d_starts, d_counts, d_ids, g_cachedParents, nodeCount, d_hashes);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostHashes, d_hashes, nodeIntBytes, cudaMemcpyDeviceToHost);

    cudaFree(d_head); cudaFree(d_starts); cudaFree(d_counts); cudaFree(d_ids); cudaFree(d_hashes);

    return 0;
}

// GPU Hash Table Constants
#define HASH_TABLE_SIZE 1048576 
#define MAX_ARITY 8
#define EMPTY_ENTRY -1

struct GpuHashConsEntry {
    int headHash;
    int arity;
    int childIds[MAX_ARITY];
    int classId;
};

static GpuHashConsEntry* g_hashTable = nullptr;
static int g_hashTableSize = 0;

__device__ unsigned int compute_node_hash(int headHash, int arity, const int* childIds) {
    unsigned int h = static_cast<unsigned int>(headHash) ^ 2166136261u;
    for (int i = 0; i < arity; i++) {
        h ^= static_cast<unsigned int>(childIds[i]) + 0x9e3779b9u + (h << 6) + (h >> 2);
        h *= 16777619u;
    }
    return h;
}

__global__ void initHashTableKernel(GpuHashConsEntry* table, int size) {
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i < size) {
        table[i].classId = EMPTY_ENTRY;
    }
}

__device__ bool nodes_equal(const GpuHashConsEntry& entry, int headHash, int arity, const int* childIds) {
    if (entry.headHash != headHash || entry.arity != arity) return false;
    for (int i = 0; i < arity; i++) {
        if (entry.childIds[i] != childIds[i]) return false;
    }
    return true;
}

__global__ void lookupOrInsertNodesKernel(
    const int* headHashes,
    const int* arities,
    const int* childStarts,
    const int* childIds,
    int nodeCount,
    int* resultClassIds,
    int* nextAvailableClassId,
    GpuHashConsEntry* table,
    int tableSize)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    if (i >= nodeCount) return;

    int hHash = headHashes[i];
    int nArity = arities[i];
    int start = childStarts[i];
    
    int localChildIds[MAX_ARITY];
    for (int j = 0; j < nArity && j < MAX_ARITY; j++) {
        localChildIds[j] = childIds[start + j];
    }

    unsigned int hash = compute_node_hash(hHash, nArity, localChildIds);
    int pos = hash % tableSize;

    // Linear probing
    while (true) {
        int existingClassId = table[pos].classId;
        if (existingClassId == EMPTY_ENTRY) {
            int oldClassId = atomicCAS(&table[pos].classId, EMPTY_ENTRY, -2);
            if (oldClassId == EMPTY_ENTRY) {
                table[pos].headHash = hHash;
                table[pos].arity = nArity;
                for (int j = 0; j < nArity && j < MAX_ARITY; j++) {
                    table[pos].childIds[j] = localChildIds[j];
                }
                int newId = atomicAdd(nextAvailableClassId, 1);
                table[pos].classId = newId;
                resultClassIds[i] = newId;
                return;
            }
            existingClassId = oldClassId;
        }

        if (existingClassId >= 0) {
            if (nodes_equal(table[pos], hHash, nArity, localChildIds)) {
                resultClassIds[i] = existingClassId;
                return;
            }
        }

        pos = (pos + 1) % tableSize;
    }
}

extern "C" __declspec(dllexport) int cobra_init_gpu_hash_table(int size) {
    if (g_hashTable != nullptr) {
        cudaFree(g_hashTable);
    }
    g_hashTableSize = size;
    cudaError_t err = cudaMalloc(&g_hashTable, (size_t)size * sizeof(GpuHashConsEntry));
    if (err != cudaSuccess) return 15001;

    int blockSize = 256;
    initHashTableKernel<<<(size + blockSize - 1) / blockSize, blockSize>>>(g_hashTable, size);
    cudaDeviceSynchronize();
    return 0;
}

extern "C" __declspec(dllexport) int cobra_lookup_or_insert_nodes(
    const int* headHashes,
    const int* arities,
    const int* childStarts,
    const int* childIds,
    int nodeCount,
    int totalChildCount,
    int* nextClassId,
    int* hostResultClassIds)
{
    if (g_hashTable == nullptr || headHashes == nullptr || arities == nullptr || 
        childStarts == nullptr || childIds == nullptr || hostResultClassIds == nullptr || nextClassId == nullptr) {
        return 15002;
    }
    if (nodeCount <= 0) return 0;

    int *d_head, *d_arity, *d_starts, *d_ids, *d_results, *d_nextId;
    size_t nodeBytes = (size_t)nodeCount * sizeof(int);
    size_t childBytes = (size_t)totalChildCount * sizeof(int);

    cudaMalloc(&d_head, nodeBytes);
    cudaMalloc(&d_arity, nodeBytes);
    cudaMalloc(&d_starts, nodeBytes);
    cudaMalloc(&d_ids, childBytes);
    cudaMalloc(&d_results, nodeBytes);
    cudaMalloc(&d_nextId, sizeof(int));

    cudaMemcpy(d_head, headHashes, nodeBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_arity, arities, nodeBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_starts, childStarts, nodeBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_ids, childIds, childBytes, cudaMemcpyHostToDevice);
    cudaMemcpy(d_nextId, nextClassId, sizeof(int), cudaMemcpyHostToDevice);

    {
        int blockSize = 256;
        int gridSize = (nodeCount + blockSize - 1) / blockSize;
        lookupOrInsertNodesKernel<<<gridSize, blockSize>>>(
            d_head, d_arity, d_starts, d_ids, nodeCount, d_results, d_nextId, g_hashTable, g_hashTableSize);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostResultClassIds, d_results, nodeBytes, cudaMemcpyDeviceToHost);
    cudaMemcpy(nextClassId, d_nextId, sizeof(int), cudaMemcpyDeviceToHost);

    cudaFree(d_head); cudaFree(d_arity); cudaFree(d_starts); cudaFree(d_ids); cudaFree(d_results); cudaFree(d_nextId);

    return 0;
}
