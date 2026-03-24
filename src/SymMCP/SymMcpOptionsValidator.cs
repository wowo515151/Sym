using Microsoft.Extensions.Options;

namespace SymMCP;

public sealed class SymMcpOptionsValidator : IValidateOptions<SymMcpOptions>
{
    public ValidateOptionsResult Validate(string? name, SymMcpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.McpPath) || !options.McpPath.StartsWith('/'))
        {
            return ValidateOptionsResult.Fail("SymMcp:McpPath must start with '/'.");
        }

        if (options.DefaultSolveTimeoutSeconds <= 0)
        {
            return ValidateOptionsResult.Fail("SymMcp:DefaultSolveTimeoutSeconds must be positive.");
        }

        if (options.MaxSolveTimeoutSeconds < options.DefaultSolveTimeoutSeconds)
        {
            return ValidateOptionsResult.Fail("SymMcp:MaxSolveTimeoutSeconds must be greater than or equal to DefaultSolveTimeoutSeconds.");
        }

        if (options.ApiKeys.Count == 0)
        {
            return ValidateOptionsResult.Fail("SymMcp:ApiKeys must contain at least one configured API key.");
        }

        var seenKeyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var apiKey in options.ApiKeys)
        {
            if (string.IsNullOrWhiteSpace(apiKey.KeyId) ||
                string.IsNullOrWhiteSpace(apiKey.CustomerId) ||
                string.IsNullOrWhiteSpace(apiKey.Secret))
            {
                return ValidateOptionsResult.Fail("Each SymMcp:ApiKeys entry must define KeyId, CustomerId, and Secret.");
            }

            if (!seenKeyIds.Add(apiKey.KeyId))
            {
                return ValidateOptionsResult.Fail($"Duplicate SymMcp API key id '{apiKey.KeyId}'.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
