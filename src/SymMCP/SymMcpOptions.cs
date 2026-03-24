namespace SymMCP;

public sealed class SymMcpOptions
{
    public const string SectionName = "SymMcp";

    public string McpPath { get; set; } = "/mcp";
    public int DefaultSolveTimeoutSeconds { get; set; } = 60;
    public int MaxSolveTimeoutSeconds { get; set; } = 180;
    public bool EnableDetailedErrors { get; set; }
    public List<ConfiguredApiKey> ApiKeys { get; set; } = new();
}

public sealed class ConfiguredApiKey
{
    public string KeyId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}
