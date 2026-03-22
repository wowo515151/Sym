//Copyright Warren Harding 2025.
using System.Text;

namespace ConsoleHelmsman;

public sealed class OneShotRunner
{
    private readonly ConsoleProcessService _processService;

    public OneShotRunner(ConsoleProcessService processService)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
    }

    public OneShotResult Run(ConsoleAppBase definition, ConsoleLog log, string input)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (log == null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            return OneShotResult.Failed("Empty input.");
        }

        if (_processService.CurrentInstance is { IsRunning: true })
        {
            return OneShotResult.Failed("Process already running.");
        }

        var arguments = BuildOneShotArguments(definition, input, out var usesPromptArgument);

        if (!_processService.StartWithArguments(definition, log, arguments, out var error))
        {
            return OneShotResult.Failed(error ?? "Failed to start.");
        }

        if (usesPromptArgument)
        {
            log.AppendInput(NormalizeInputForLog(input));
            _processService.TryCloseInput(out _);
            return OneShotResult.CreateStarted(true, null);
        }

        if (_processService.TrySendInput(input, out var sentText, out var sendError, closeAfterSend: true))
        {
            log.AppendInput(sentText);
            return OneShotResult.CreateStarted(false, null);
        }

        return OneShotResult.CreateStarted(false, sendError ?? "Failed to send input.");
    }

    public static string BuildOneShotArguments(ConsoleAppBase definition, string prompt, out bool usesPromptArgument)
    {
        var arguments = definition.Arguments ?? string.Empty;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            usesPromptArgument = false;
            return string.Empty;
        }

        if (!arguments.Contains("{prompt}", StringComparison.OrdinalIgnoreCase))
        {
            usesPromptArgument = false;
            return arguments;
        }

        // Strip tool name prefix if present to avoid redundancy (e.g. "gh gh search")
        if (!string.IsNullOrEmpty(definition.Name) && prompt.StartsWith(definition.Name + " ", StringComparison.OrdinalIgnoreCase))
        {
            prompt = prompt.Substring(definition.Name.Length + 1);
        }
        else if (!string.IsNullOrEmpty(definition.Name) && string.Equals(prompt, definition.Name, StringComparison.OrdinalIgnoreCase))
        {
             prompt = string.Empty;
        }

        // If the arguments are exactly the prompt token (e.g. "{prompt}"),
        // pass the prompt through unquoted so it can contain multiple
        // whitespace-separated arguments (useful for commands like `gh search repos Sym`).
        if (string.Equals(arguments.Trim(), "{prompt}", StringComparison.OrdinalIgnoreCase))
        {
            usesPromptArgument = true;
            return prompt ?? string.Empty;
        }

        usesPromptArgument = true;
        var quotedPrompt = QuoteCommandLineArgument(prompt);
        return arguments.Replace("{prompt}", quotedPrompt, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInputForLog(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return input.EndsWith("\n", StringComparison.Ordinal) ? input : input + Environment.NewLine;
    }

    private static string QuoteCommandLineArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var needsQuotes = value.IndexOfAny(new[] { ' ', '\t', '"', '\n', '\r' }) >= 0;
        if (!needsQuotes)
        {
            return value;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashCount = 0;

        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(ch);
        }

        if (backslashCount > 0)
        {
            builder.Append('\\', backslashCount * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }
}
