
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
