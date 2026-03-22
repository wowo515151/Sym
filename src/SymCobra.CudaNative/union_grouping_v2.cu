
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
