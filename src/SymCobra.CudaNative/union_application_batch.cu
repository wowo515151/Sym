
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
