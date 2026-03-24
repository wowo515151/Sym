using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace SymMCP.Auth;

public sealed class ApiKeyAuthenticationMiddleware
{
    public const string HeaderName = "X-API-Key";
    public const string ItemKeyId = "SymMcp.ApiKeyId";
    public const string ItemCustomerId = "SymMcp.CustomerId";

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<SymMcpOptions> _optionsMonitor;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        IOptionsMonitor<SymMcpOptions> optionsMonitor,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _optionsMonitor.CurrentValue;
        var path = context.Request.Path;

        if (!path.StartsWithSegments(options.McpPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedValues))
        {
            await RejectAsync(context);
            return;
        }

        var provided = providedValues.ToString();
        if (string.IsNullOrEmpty(provided))
        {
            await RejectAsync(context);
            return;
        }

        foreach (var configuredKey in options.ApiKeys)
        {
            if (SecretsEqual(provided, configuredKey.Secret))
            {
                context.Items[ItemKeyId] = configuredKey.KeyId;
                context.Items[ItemCustomerId] = configuredKey.CustomerId;
                using (_logger.BeginScope(new Dictionary<string, object?>
                {
                    ["apiKeyId"] = configuredKey.KeyId,
                    ["customerId"] = configuredKey.CustomerId
                }))
                {
                    await _next(context);
                }

                return;
            }
        }

        await RejectAsync(context);
    }

    private static bool SecretsEqual(string provided, string expected)
    {
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
    }
}
