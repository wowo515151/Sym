//Copyright Warren Harding 2025.
namespace ConsoleHelmsman;

public sealed class ConsoleAppBase
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public bool OneShot { get; set; }
    // Indicates whether this console app is available for user selection and for AI use.
    public bool Selected { get; set; }
}
