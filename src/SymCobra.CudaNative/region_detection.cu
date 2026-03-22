
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

extern "C" __declspec(dllexport) int cobra_detect_regions_batch_cached(
    int classCount,
    int* hostFamilyCodes)
{
    if (hostFamilyCodes == nullptr) return 10010;
    if (classCount <= 0) return 10011;
    if (g_cachedClassHeadBucketMasks == nullptr || g_cachedClassExactHeadMasks == nullptr) return 10012;

    int* d_familyCodes;
    size_t bytes = (size_t)classCount * sizeof(int);
    cudaMalloc(&d_familyCodes, bytes);

    {
        int blockSize = 256;
        int gridSize = (classCount + blockSize - 1) / blockSize;
        detectRegionsBatchKernel<<<gridSize, blockSize>>>(g_cachedClassHeadBucketMasks, g_cachedClassExactHeadMasks, classCount, d_familyCodes);
    }

    cudaDeviceSynchronize();
    cudaMemcpy(hostFamilyCodes, d_familyCodes, bytes, cudaMemcpyDeviceToHost);
    cudaFree(d_familyCodes);

    return 0;
}
