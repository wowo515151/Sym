
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
