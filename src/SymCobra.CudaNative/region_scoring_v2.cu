
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
