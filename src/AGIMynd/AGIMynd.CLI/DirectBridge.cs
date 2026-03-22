using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AGIMynd;
using ConsoleHelmsman;

#nullable enable

namespace AGIMynd.CLI
{
    public class DirectBridge : IToolRunner
    {
        private readonly ConsoleProcessService _processService;
        private readonly OneShotRunner _oneShotRunner;
        private readonly List<ConsoleAppBase> _tools;

        public DirectBridge(string repoRoot)
        {
            _processService = new ConsoleProcessService();
            _processService.WorkingDirectory = repoRoot;
            _oneShotRunner = new OneShotRunner(_processService);

            // Load tools from ConsoleConfigService
            var configService = new ConsoleConfigService();
            var config = configService.LoadOrCreate();
            _tools = (config.Items ?? new List<ConsoleAppBase>())
                .Where(t => t.Selected)
                .ToList();

            Console.WriteLine($"[DirectBridge] Loaded tools: {string.Join(", ", _tools.Select(t => t.Name))}");

            // Apply robust path overrides for known tools if they aren't fully rooted or missing
            foreach (var tool in _tools)
            {
                if (string.Equals(tool.Name, "copilot", StringComparison.OrdinalIgnoreCase))
                {
                    var robustPath = @"C:\Users\wowod\AppData\Local\Microsoft\WinGet\Packages\GitHub.Copilot_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe";
                    if (!File.Exists(tool.Path) && File.Exists(robustPath))
                    {
                        tool.Path = robustPath;
                    }
                }
            }
        }

        public IEnumerable<ToolDescription> ToolDescriptions => _tools.Select(t => new ToolDescription
        {
            ToolName = t.Name,
            ToolInputRequirements = (t.Name switch
            {
                "pwsh" => "One-shot PowerShell command string. DO NOT use for security scanning.",
                "copilot" => "PRIMARY AGENTIC CODING TOOL. Provide concise English instructions for code generation, research, or complex refactoring in the repo.",
                "powershell" => "Interactive PowerShell command. DO NOT use for security scanning logic.",
                "cmd" => "Interactive CMD command.",
                "git" => "Git command string.",
                "gh" => "GitHub CLI. Use for GitHub-specific tasks. RETURNS TEXT ONLY. Prefer drivecli for local searching and reporting.",
                "codex" => "Codex instructions.",
                "gemini" => "Gemini instructions.",
                "drivecli" => "PRIMARY FILE SYSTEM UTILITY for searching and writing reports. Commands: read <path>, write <path> <text>, append <path> <text>, exists <path>, delete <path>, mkdir <path>, list <dir>, search <dir> <pattern> [ext]. Use 'write' for reports.",
                "SearchVerify" => "INTERNAL SCANNING TOOL. Usage: <directory> <pattern>. Automatically searches and reads up to 20 files, recording results in HAMM Observations.",
                "MultiSearchVerify" => "BEST SCANNING TOOL for security scans. Usage: <directory> <pattern1> <pattern2> ... <patternN>. Searches and reads multiple patterns at once.",
                "TraceSource" => "HIGH PRECISION TOOL. Usage: <directory> <methodName>. Finds all callers of a method to trace data flow from sources to sinks. Reduces false positives.",
                "SearchGuards" => "VALIDATION TOOL. Usage: <directory>. Scans for common sanitization/validation logic (guards) like Regex, Replace, Whitelist. Mandatory before verifying a vulnerability.",
                "SecurityPolicy" => "POLICY TOOL. Usage: none. Returns the mandatory security audit rules for high-precision scanning.",
                "VerifiedSecurityFinding" => "REPORTING TOOL. Usage: Sink: <sink> | Source: <source> | Guards: <guards_or_none> | Description: <desc>. Use ONLY for highly verified vulnerabilities.",
                "SafeObservation" => "NEGATIVE LOGGING TOOL. Usage: <reason_why_safe>. Use to record that a potential sink has been verified safe. Prevents false positives.",
                _ => t.OneShot ? "One-shot CLI command." : "Interactive CLI command."
            }) + " Use '$path' for the first line of the previous tool output (best for search results) or '$output' for the full result."
        });

        public async Task<string> RunToolAsync(string toolName, string args)
        {
            var def = _tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
            
            if (def == null)
            {
                return $"[DirectBridge] Unknown tool: {toolName}. Available: {string.Join(", ", _tools.Select(t => t.Name))}";
            }

            // Create a temporary log to capture output
            var log = new ConsoleLog(100000);
            
            // Run
            var arguments = OneShotRunner.BuildOneShotArguments(def, args, out _);
            Console.WriteLine($"[DirectTool] Executing: {def.Path} {arguments}");
            var result = await Task.Run(() => _oneShotRunner.Run(def, log, args));
            
            if (!result.Started)
            {
                return $"[DirectBridge] Execution failed: {result.Error}";
            }

            // Wait for it to finish
            var instance = _processService.CurrentInstance;
            if (instance != null)
            {
                // Wait loop
                for(int i=0; i<6000; i++) // 600s timeout
                {
                    if (!instance.IsRunning) break;
                    await Task.Delay(100);
                }
                
                if (instance.IsRunning)
                {
                    await _processService.StopAsync();
                    log.AppendSystemMessage("[DirectBridge] Timeout: Process killed after 600s.");
                }
            }

            // Collect output from log
            var output = log.GetTail(int.MaxValue) ?? "";

            // CLEANUP: Remove echoed command and system messages to leave only the actual stdout/stderr.
            // ConsoleLog often contains the echoed input line at the top.
            var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
            if (lines.Count > 0 && lines[0].Contains(args))
            {
                lines.RemoveAt(0);
            }
            // Remove "[Process exited: code 0]" or similar system messages if present
            lines = lines.Where(l => !l.StartsWith("[Process exited:") && !l.StartsWith("[DirectBridge]")).ToList();
            output = string.Join(Environment.NewLine, lines).Trim();

            Console.WriteLine($"[DirectTool] {toolName} Output ({output.Length} chars):");
            if (output.Length > 0)
            {
                var preview = output.Length > 500 ? output.Substring(0, 500) + "..." : output;
                Console.WriteLine(preview);
            }
            return output; 
        }
    }
}
