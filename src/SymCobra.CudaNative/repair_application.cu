
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
