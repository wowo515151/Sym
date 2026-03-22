// Copyright Warren Harding 2026
namespace Sym
{
    /// <summary>
    /// Small utility used by tests to assert coverage thresholds in CI/monitoring scenarios.
    /// Kept minimal per CodingGuidelines (no third-party libs).
    /// </summary>
    public static class CoverageMonitor
    {
        public static bool IsCoverageSufficient(double coveragePercent, double thresholdPercent = 80.0)
        {
            return coveragePercent >= thresholdPercent;
        }
    }
}
