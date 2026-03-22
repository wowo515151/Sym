// Copyright Warren Harding 2026
using System;
using System.IO;

namespace ConsoleHelmsman;

public static class RepoPathService
{
    public static string? FindRepoRoot()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var gitPath = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitPath))
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
