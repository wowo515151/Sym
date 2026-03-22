using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

const string AppName = "UnzipRepos";

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var (repositories, invalidEntries) = args.Length > 0
    ? ParseRepositories(args)
    : ReadRepositoriesFromConsole();

if (invalidEntries.Count > 0)
{
    Console.WriteLine("Ignored invalid entries:");
    foreach (var invalidEntry in invalidEntries)
    {
        Console.WriteLine($"  - {invalidEntry}");
    }
}

if (repositories.Count == 0)
{
    Console.Error.WriteLine("No valid repositories were provided.");
    return 1;
}

var downloadsDirectory = GetDownloadsDirectory();
Directory.CreateDirectory(downloadsDirectory);

Console.WriteLine($"Target downloads folder: {downloadsDirectory}");
Console.WriteLine($"Repositories to process: {repositories.Count}");

using var httpClient = CreateHttpClient();

var results = new List<DownloadResult>(repositories.Count);
for (var index = 0; index < repositories.Count; index++)
{
    var repository = repositories[index];
    Console.WriteLine();
    Console.WriteLine($"[{index + 1}/{repositories.Count}] Processing {repository}");

    var result = await DownloadAndExtractAsync(httpClient, repository, downloadsDirectory, cancellation.Token);
    results.Add(result);

    if (result.Success)
    {
        Console.WriteLine($"  OK: {result.Message}");
        continue;
    }

    Console.WriteLine($"  FAIL: {result.Message}");
}

var succeeded = results.Count(result => result.Success);
var failed = results.Count - succeeded;

Console.WriteLine();
Console.WriteLine($"Finished. Success: {succeeded}, Failed: {failed}");
return failed == 0 ? 0 : 1;

static (List<string> Repositories, List<string> InvalidEntries) ReadRepositoriesFromConsole()
{
    Console.WriteLine($"Enter GitHub repositories in owner/repo format for {AppName}.");
    Console.WriteLine("You can separate values with spaces, commas, semicolons, or new lines.");
    Console.WriteLine("Press Enter on an empty line to start.");

    var lines = new List<string>();
    while (true)
    {
        Console.Write("> ");
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            break;
        }

        lines.Add(line);
    }

    return ParseRepositories(lines);
}

static (List<string> Repositories, List<string> InvalidEntries) ParseRepositories(IEnumerable<string> rawValues)
{
    var repositories = new List<string>();
    var invalidEntries = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var separators = new[] { ',', ';', ' ', '\t', '\r', '\n' };

    foreach (var rawValue in rawValues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            continue;
        }

        var pieces = rawValue.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var piece in pieces)
        {
            var normalized = NormalizeRepositoryInput(piece);
            if (!IsValidRepositoryName(normalized))
            {
                invalidEntries.Add(piece);
                continue;
            }

            if (seen.Add(normalized))
            {
                repositories.Add(normalized);
            }
        }
    }

    return (repositories, invalidEntries);
}

static string NormalizeRepositoryInput(string value)
{
    var trimmed = value.Trim().Trim('"', '\'');

    if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
        uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
    {
        var pathParts = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathParts.Length >= 2)
        {
            trimmed = $"{pathParts[0]}/{pathParts[1]}";
        }
    }

    if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
    {
        trimmed = trimmed[..^4];
    }

    return trimmed;
}

static bool IsValidRepositoryName(string repository)
{
    if (string.IsNullOrWhiteSpace(repository))
    {
        return false;
    }

    var parts = repository.Split('/', StringSplitOptions.TrimEntries);
    return parts.Length == 2 && IsValidRepositoryPart(parts[0]) && IsValidRepositoryPart(parts[1]);
}

static bool IsValidRepositoryPart(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    foreach (var c in value)
    {
        if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
        {
            continue;
        }

        return false;
    }

    return true;
}

static string GetDownloadsDirectory()
{
    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (string.IsNullOrWhiteSpace(userProfile))
    {
        throw new InvalidOperationException("Could not resolve the current user profile directory.");
    }

    return Path.Combine(userProfile, "Downloads");
}

static HttpClient CreateHttpClient()
{
    var client = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppName}/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    return client;
}

static async Task<DownloadResult> DownloadAndExtractAsync(
    HttpClient httpClient,
    string repository,
    string downloadsDirectory,
    CancellationToken cancellationToken)
{
    string? zipPath = null;
    try
    {
        var split = repository.Split('/', 2, StringSplitOptions.TrimEntries);
        var owner = split[0];
        var repo = split[1];

        var defaultBranch = await GetDefaultBranchAsync(httpClient, owner, repo, cancellationToken);
        var archiveUrl = $"https://github.com/{owner}/{repo}/archive/refs/heads/{Uri.EscapeDataString(defaultBranch)}.zip";

        var safeRepoName = ToSafeFileName(repository.Replace('/', '-'));
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        zipPath = Path.Combine(downloadsDirectory, $"{safeRepoName}-{stamp}.zip");
        var extractPath = Path.Combine(downloadsDirectory, $"{safeRepoName}-{stamp}");

        using (var response = await httpClient.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new DownloadResult(repository, false, "Archive was not found on GitHub.");
            }

            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = File.Create(zipPath);
            await source.CopyToAsync(destination, cancellationToken);
        }

        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

        return new DownloadResult(repository, true, $"Extracted to {extractPath}");
    }
    catch (OperationCanceledException)
    {
        return new DownloadResult(repository, false, "Operation canceled.");
    }
    catch (Exception ex)
    {
        return new DownloadResult(repository, false, ex.Message);
    }
    finally
    {
        if (zipPath is not null && File.Exists(zipPath))
        {
            try
            {
                File.Delete(zipPath);
            }
            catch
            {
                // Keep processing if cleanup fails.
            }
        }
    }
}

static async Task<string> GetDefaultBranchAsync(
    HttpClient httpClient,
    string owner,
    string repo,
    CancellationToken cancellationToken)
{
    var metadataUrl = $"https://api.github.com/repos/{owner}/{repo}";
    using var response = await httpClient.GetAsync(metadataUrl, cancellationToken);

    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        throw new InvalidOperationException("Repository not found.");
    }

    if (response.StatusCode == HttpStatusCode.Forbidden)
    {
        throw new InvalidOperationException("GitHub API request forbidden (rate limit or access denied).");
    }

    response.EnsureSuccessStatusCode();

    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    var data = await JsonSerializer.DeserializeAsync<GitHubRepositoryMetadata>(
        stream,
        cancellationToken: cancellationToken);

    if (string.IsNullOrWhiteSpace(data?.DefaultBranch))
    {
        throw new InvalidOperationException("Could not determine the default branch.");
    }

    return data.DefaultBranch;
}

static string ToSafeFileName(string value)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    var sanitized = value.ToCharArray();
    for (var i = 0; i < sanitized.Length; i++)
    {
        if (invalidChars.Contains(sanitized[i]))
        {
            sanitized[i] = '-';
        }
    }

    return new string(sanitized);
}

file sealed record DownloadResult(string Repository, bool Success, string Message);

file sealed class GitHubRepositoryMetadata
{
    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; init; }
}
