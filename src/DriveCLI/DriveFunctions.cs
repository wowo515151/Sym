// Copyright Warren Harding 2026
using System.Text;

namespace DriveCLI;

public static class DriveFunctions
{
    public static bool FileExists(string path) => File.Exists(path);

    public static bool DirectoryExists(string path) => Directory.Exists(path);

    public static void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static IEnumerable<string> EnumerateFileSystemEntries(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFileSystemEntries(directoryPath);
    }

    public static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllTextAsync(path, cancellationToken);

    public static async Task WriteAllTextAsync(
        string path,
        string contents,
        bool overwrite,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        if (!overwrite && File.Exists(path))
        {
            throw new IOException($"File already exists: {path}");
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (encoding is null)
        {
            await File.WriteAllTextAsync(path, contents, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await File.WriteAllTextAsync(path, contents, encoding, cancellationToken).ConfigureAwait(false);
        }
    }

    public static Task AppendAllTextAsync(
        string path,
        string contents,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return encoding is null
            ? File.AppendAllTextAsync(path, contents, cancellationToken)
            : File.AppendAllTextAsync(path, contents, encoding, cancellationToken);
    }

    public static IEnumerable<string> SearchFiles(string directoryPath, string pattern, string? extension = null)
    {
        if (!Directory.Exists(directoryPath))
        {
            yield break;
        }

        var files = Directory.EnumerateFiles(directoryPath, extension ?? "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Memory" + Path.DirectorySeparatorChar) && 
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
        foreach (var file in files)
        {
            string[]? lines = null;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch { }

            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return $"{file}({i + 1}): {lines[i].Trim()}";
                    }
                }
            }
        }
    }
}
