using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using SymMCP;
using SymMCP.Auth;
using SymMCP.Services;
using SymMCP.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<SymMcpOptions>()
    .Bind(builder.Configuration.GetSection(SymMcpOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SymMcpOptions>, SymMcpOptionsValidator>();

builder.Services.AddSingleton<ISymSolveService, SymSolveService>();
builder.Services.AddSingleton<ICSharpMathAnalysisService, CSharpMathAnalysisService>();
builder.Services.AddSingleton<SymMcpTools>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<SymMcpTools>();

var app = builder.Build();

app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/version", () => Results.Ok(new
{
    service = "SymMCP",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
}));

var options = app.Services.GetRequiredService<IOptions<SymMcpOptions>>().Value;
app.MapMcp(options.McpPath);

app.Run();

public partial class Program;
