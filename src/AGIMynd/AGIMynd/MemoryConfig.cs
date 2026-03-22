// Copyright Warren Harding 2026
using System;
using System.IO;

namespace AGIMynd
{
    public static class MemoryConfig
    {
        // Programmatic override for tests and runtime: stored per async context to avoid
        // cross-thread/test interference when the application is used in parallel.
        // Use `SetMemoryRoot` to set a temporary override for the current async flow.
        private static readonly System.Threading.AsyncLocal<string?> _memoryRootOverride = new System.Threading.AsyncLocal<string?>();

        /// <summary>
        /// Returns the default memory root path.
        /// Priority order:
        /// 1. Programmatic override set via <see cref="SetMemoryRoot(string?)"/> (per async context).
        /// 2. Repository root (a folder containing <c>src/AGIMynd.slnx</c>) + <c>Memory</c>.
        /// 3. Application base directory + <c>Memory</c>.
        ///
        /// Notes:
        /// - The programmatic override is stored in an <see cref="System.Threading.AsyncLocal{T}"/>
        ///   so that parallel tests or asynchronous flows do not interfere with each other's overrides.
        /// - Environment variables are intentionally not used; prefer explicit programmatic control.
        /// </summary>
        public static string GetDefaultMemoryRoot()
        {
            if (!string.IsNullOrWhiteSpace(_memoryRootOverride.Value)) return _memoryRootOverride.Value!;

            var repoRoot = FindRepoRoot();
            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                return Path.Combine(repoRoot, "Memory");
            }
            return Path.Combine(AppContext.BaseDirectory, "Memory");
        }

        // If true, AgentErrors.log will be deleted once on startup to avoid replaying large logs
        // into UI viewers. Default: true (opt-in behavior to keep fresh runs clean).
        public static bool DeleteLogOnStartup { get; set; } = true;

        // Internal guard to ensure deletion happens only once per process run.
        private static int _deleteLogPerformed = 0;

        public static void EnsureDeleteLogOnStartup(string logDir)
        {
            try
            {
                if (!DeleteLogOnStartup) return;

                // Only perform deletion once even if called multiple times
                if (System.Threading.Interlocked.Exchange(ref _deleteLogPerformed, 1) != 0) return;

                if (string.IsNullOrWhiteSpace(logDir)) return;

                var logPath = Path.Combine(logDir, "AgentErrors.log");
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Programmatically sets the memory root for the current async context. Pass <c>null</c>
        /// to clear the override and fall back to repository or app-base defaults.
        /// </summary>
        /// <param name="path">The path to use as the memory root for the current async context, or null to clear.</param>
        public static void SetMemoryRoot(string? path)
        {
            _memoryRootOverride.Value = path;
        }

        public static string GetDefaultPinnedSource()
        {
            try
            {
                var repoRoot = FindRepoRoot();
                if (string.IsNullOrWhiteSpace(repoRoot))
                {
                    return string.Empty;
                }

                string pinned = Path.Combine(repoRoot, "src", "AGIMynd", "Pinned");
                return Directory.Exists(pinned) ? pinned : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string? FindRepoRoot()
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    var slnxPath = Path.Combine(dir.FullName, "src", "AGIMynd", "AGIMynd.slnx");
                    if (File.Exists(slnxPath))
                    {
                        return dir.FullName;
                    }

                    dir = dir.Parent;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
