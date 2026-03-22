
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
