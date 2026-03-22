//Copyright Warren Harding 2025.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AGIMynd;
using ConsoleHelmsman;

#nullable enable

namespace AGIMynd.CLI
{
    class Program
    {
        static async Task Main(string[] args)
        {
            SwitchLLM.LLM.DefaultModelDescription = "Local,openai/gpt-oss-20b";

            string memoryRoot = MemoryConfig.GetDefaultMemoryRoot();
            string repoRoot = MemoryConfig.FindRepoRoot() ?? AppContext.BaseDirectory;
            
            int maxEpochs = 0;
            TimeSpan delay = TimeSpan.FromSeconds(5);
            var goalArgs = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--epochs" && i + 1 < args.Length && int.TryParse(args[i + 1], out int epochs))
                {
                    maxEpochs = epochs;
                    i++;
                }
                else if (args[i] == "--delay" && i + 1 < args.Length && int.TryParse(args[i + 1], out int seconds))
                {
                    delay = TimeSpan.FromSeconds(seconds);
                    i++;
                }
                else
                {
                    goalArgs.Add(args[i]);
                }
            }

            string defaultGoal = goalArgs.Count > 0 ? string.Join(" ", goalArgs) : "Maintain internal consistency and optimize memory structure.";

            Console.WriteLine("AGIMynd CLI (Direct Mode) Starting...");
            Console.WriteLine($"Memory Root: {memoryRoot}");
            Console.WriteLine($"Repo Root: {repoRoot}");
            Console.WriteLine($"Max Epochs: {(maxEpochs > 0 ? maxEpochs.ToString() : "Unlimited")}");
            Console.WriteLine($"Delay: {delay.TotalSeconds}s");

            var bridge = new DirectBridge(repoRoot);

            // Attempt to load External/ActiveTools.json exported by the ConsoleHelmsman UI
            IEnumerable<ToolDescription>? externalTools = null;
            try
            {
                var externalDir = Path.Combine(repoRoot, "External");
                var toolsPath = Path.Combine(externalDir, "ActiveTools.json");
                if (File.Exists(toolsPath))
                {
                    var json = File.ReadAllText(toolsPath);
                    var list = System.Text.Json.JsonSerializer.Deserialize<ToolDescriptionList>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list != null && list.Tools?.Count > 0)
                    {
                        externalTools = list.Tools;
                        Console.WriteLine($"Loaded {list.Tools.Count} external tool description(s) from {toolsPath}.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load External/ActiveTools.json: {ex.Message}");
            }

            // Inject bridge as IToolRunner and provide tool descriptions (prefer external file if present)
            var agent = new MyndAgent(memoryRoot, toolRunner: bridge, externalTools: externalTools ?? bridge.ToolDescriptions, repoRoot: repoRoot);
            agent.MaxEpochs = maxEpochs;

            // Attach logging
            agent.TraceNotificationAsync = async (msg) => await Task.Run(() => Console.WriteLine(msg));
            agent.DeletionNotificationAsync = async (msg) => await Task.Run(() => Console.WriteLine($"[DELETE] {msg}"));

            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("Shutting down...");
                e.Cancel = true;
                cts.Cancel();
            };

            // Start the Agent loop
            Console.WriteLine("Agent running. Press Ctrl+C to stop.");
            Console.WriteLine($"Goal: {defaultGoal}");

            await agent.UpdateGoalAsync(defaultGoal);

            try
            {
                await agent.StartAsync(delay, cts.Token);
            }
            finally
            {
                Console.WriteLine("Saving HAMM memory...");
                agent.SaveHAMM();
                Console.WriteLine("Done.");
            }
        }

                }

            }

            