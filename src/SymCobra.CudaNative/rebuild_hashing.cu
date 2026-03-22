
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
