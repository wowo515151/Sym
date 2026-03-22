// Copyright Warren Harding 2026
using System;

namespace SymCobra.Runtime;

public sealed record CobraRuntimeInfo(
    bool IsCudaAvailable,
    string RuntimeKind,
    string StatusMessage)
{
    public static CobraRuntimeInfo Detect()
    {
        bool loaded = CobraCudaNative.TryLoad();
        int deviceCount = loaded ? CobraCudaNative.GetDeviceCount() : 0;
        bool cudaAvailable = loaded && deviceCount > 0;

        return new CobraRuntimeInfo(
            IsCudaAvailable: cudaAvailable,
            RuntimeKind: cudaAvailable ? "CUDA" : "ManagedCompatibility",
            StatusMessage: cudaAvailable
                ? $"CUDA runtime available with {deviceCount} device(s)."
                : "CUDA runtime unavailable; using compatibility-first COBRA execution.");
    }
}
