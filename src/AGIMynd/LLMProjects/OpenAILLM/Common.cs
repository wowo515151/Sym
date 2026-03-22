//Copyright Warren Harding 2025.
namespace OpenAILLM;

internal static class Common
{
    public static string WrapInTags(string body, string tagName)
    {
        if (string.IsNullOrEmpty(body))
        {
            body = "No data available.";
        }

        return $"<{tagName}>\n{body}\n</{tagName}>";
    }

    public static async Task<string> GetPrompt(string name)
    {
        return await GetPrompt("Prompts", name);
    }

    public static async Task<string> GetPrompt(string folder, string name)
    {
        var promptName = name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? name : name + ".txt";

        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var fullPath = Path.Combine(baseDirectory, folder, promptName);

        if (!File.Exists(fullPath))
        {
            return $"Prompt not found: {fullPath}";
        }

        return await File.ReadAllTextAsync(fullPath);
    }
}
