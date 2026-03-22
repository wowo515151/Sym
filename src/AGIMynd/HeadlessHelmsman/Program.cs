// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConsoleHelmsman;

namespace HeadlessHelmsman
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("HeadlessHelmsman Starting...");

            string repoRoot = Directory.GetCurrentDirectory();
            while (!Directory.Exists(Path.Combine(repoRoot, "External")) && Directory.GetParent(repoRoot) != null)
            {
                repoRoot = Directory.GetParent(repoRoot)!.FullName;
            }

            Console.WriteLine($"Repo Root: {repoRoot}");

            var copilotPath = @"C:\Users\wowod\AppData\Local\Microsoft\WinGet\Packages\GitHub.Copilot_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe";
            var copilotArgs = "-p {prompt} --model \"gpt-5-mini\"";

            ConsoleAppBase? GetDefinition(string name)
            {
                if (string.Equals(name, "copilot", StringComparison.OrdinalIgnoreCase))
                {
                    return new ConsoleAppBase
                    {
                        Name = "copilot",
                        Path = copilotPath,
                        Arguments = copilotArgs,
                        OneShot = true
                    };
                }
                if (string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase))
                {
                     return new ConsoleAppBase
                    {
                        Name = "powershell",
                        Path = "powershell.exe",
                        Arguments = "-NoLogo -Command {prompt}",
                        OneShot = true
                    };
                }
                return null;
            }

            var bridge = new PlanBridgeService(
                repoRoot,
                GetDefinition,
                () => "copilot", 
                () => repoRoot, 
                log => Console.WriteLine($"[Bridge] {log}")
            );

            bridge.Start();
            Console.WriteLine("Bridge Started. Press Ctrl+C to exit.");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                Task.Delay(-1, cts.Token).Wait();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Stopping...");
            }

            bridge.Dispose();
        }
    }
}
