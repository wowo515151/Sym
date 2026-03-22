
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
        allGenerations[classId] * 4 +
        allNodeCounts[classId] +
        (hotFlags[index] != 0 ? 1000 : 0) +
        (boundaryFlags[index] != 0 ? 100 : 0) -
        (residualFlags[index] != 0 ? 25 : 0) -
        (suppressedFlags[index] != 0 ? 500 : 0) +
        (hotRegionCounts[index] * 48) +
        (boundaryRegionCounts[index] * 12);
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
    if (d_cachedClassNodeCountsSnapshot == nullptr || d_cachedClassGenerationsSnapshot == nullptr)
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

    int blockSize = 256;
    int gridSize = (classCount + blockSize - 1) / blockSize;

    scoreFrontierV3ByIdKernel<<<gridSize, blockSize>>>(
        deviceClassIds,
        d_cachedClassNodeCountsSnapshot,
        d_cachedClassGenerationsSnapshot,
        deviceHotFlags,
        deviceBoundaryFlags,
        deviceResidualFlags,
        deviceSuppressedFlags,
        deviceHotRegionCounts,
        deviceBoundaryRegionCounts,
        classCount,
        deviceScores);

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
