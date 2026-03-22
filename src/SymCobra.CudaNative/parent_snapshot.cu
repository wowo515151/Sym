
extern "C" __declspec(dllexport) int cobra_get_parent_snapshot(
    int* hostParents,
    int parentCount)
{
    if (g_cachedParents == nullptr || g_cachedParentCount <= 0 || hostParents == nullptr)
    {
        return 1003;
    }

    if (parentCount != g_cachedParentCount)
    {
        return 1004;
    }

    cudaError_t err = cudaMemcpy(
        hostParents,
        g_cachedParents,
        static_cast<size_t>(g_cachedParentCount) * sizeof(int),
        cudaMemcpyDeviceToHost);

    if (err != cudaSuccess)
    {
        return 1005;
    }

    return 0;
}
