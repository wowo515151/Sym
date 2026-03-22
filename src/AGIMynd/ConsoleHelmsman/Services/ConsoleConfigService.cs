//Copyright Warren Harding 2025.
using System.IO;
using System.Xml.Serialization;

namespace ConsoleHelmsman;

public sealed class ConsoleConfigService
{
    private const string ConfigFileName = "consoles.config";
    private readonly string _configPath;

    public ConsoleConfigService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }

    public string ConfigPath => _configPath;

    public ConsoleAppBases LoadOrCreate()
    {
        // Always recreate the config file with the built-in defaults on startup.
        var config = CreateDefault();
        try
        {
            Save(config);
        }
        catch
        {
            // Ignore save failures at startup.
        }

        return config;
    }

    private ConsoleAppBases Load()
    {
        var serializer = new XmlSerializer(typeof(ConsoleAppBases));
        using var stream = File.OpenRead(_configPath);
        if (serializer.Deserialize(stream) is ConsoleAppBases config)
        {
            return config;
        }

        return new ConsoleAppBases();
    }

    private static void EnsureDefaultsSelected(ConsoleAppBases config)
    {
        if (config == null) return;
        try
        {
            if (config.Items == null || config.Items.Count == 0) return;

            var any = config.Items.Any(i => i.Selected);
            if (any) return;

            // Prefer powershell, then cmd, else first item.
            var pw = config.Items.FirstOrDefault(i => string.Equals(i.Name, "powershell", StringComparison.OrdinalIgnoreCase));
            if (pw != null)
            {
                pw.Selected = true;
                return;
            }

            var cmd = config.Items.FirstOrDefault(i => string.Equals(i.Name, "cmd", StringComparison.OrdinalIgnoreCase));
            if (cmd != null)
            {
                cmd.Selected = true;
                return;
            }

            config.Items[0].Selected = true;
        }
        catch
        {
        }
    }

    public void Save(ConsoleAppBases config)
    {
        var serializer = new XmlSerializer(typeof(ConsoleAppBases));
        using var stream = File.Create(_configPath);
        serializer.Serialize(stream, config);
    }

    private static ConsoleAppBases CreateDefault()
    {
        // NOTE: Keep defaults minimal. ConsoleHelmsman uses these definitions for user selection and AI tool routing.
        // If you add a new CLI here, ensure it exists in the repo and is runnable from the configured working directory.
        var (driveCliPath, driveCliArgs) = TryResolveDriveCliConfig();

        return new ConsoleAppBases
        {
            Items = new List<ConsoleAppBase>
            {
                new()
                {
                    Name = "powershell",
                    Path = "powershell.exe",
                    Arguments = "-NoLogo",
                    Selected = false
                },
                new()
                {
                    Name = "pwsh",
                    Path = "powershell.exe",
                    Arguments = "-NoLogo -NoProfile",
                    OneShot = true,
                    Selected = false
                },
                new()
                {
                    Name = "cmd",
                    Path = "cmd.exe",
                    Arguments = "/Q",
                    Selected = false
                },
                new()
                {
                    Name = "codex",
                    Path = "codex.cmd",
                    Arguments = "exec --skip-git-repo-check",
                    OneShot = true,
                    Selected = false
                },
                new()
                {
                    Name = "git",
                    Path = "git.exe",
                    Arguments = string.Empty,
                    OneShot = false,
                    Selected = false,
                },
                new()
                {
                    Name = "gh",
                    Path = "gh.exe",
                    Arguments = "{prompt}",
                    OneShot = true,
                    Selected = true,
                },
                new()
                {
                    Name = "gemini",
                    Path = "gemini.cmd",
                    Arguments = string.Empty,
                    OneShot = true,
                    Selected = false
                },
                new()
                {
                    Name = "copilot",
                    Path = "copilot.exe",
                    // Enable all permissions and disable user questions for full autonomy.
                    Arguments = "-p {prompt} --yolo --no-ask-user --model gpt-5-mini",
                    OneShot = true,
                    Selected = false
                },
                new()
                {
                    Name = "drivecli",
                    Path = driveCliPath,
                    Arguments = driveCliArgs,
                    OneShot = true,
                    Selected = true
                }
            }
        };
    }

    private static (string Path, string Arguments) TryResolveDriveCliConfig()
    {
        try
        {
            var repoRoot = RepoPathService.FindRepoRoot();
            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                var exe = Path.Combine(repoRoot, "src", "DriveCLI", "bin", "Debug", "net10.0", "DriveCLI.exe");
                if (File.Exists(exe))
                {
                    return (exe, "{prompt}");
                }

                var dll = Path.Combine(repoRoot, "src", "DriveCLI", "bin", "Debug", "net10.0", "DriveCLI.dll");
                if (File.Exists(dll))
                {
                    return ("dotnet", $"\"{dll}\" {{prompt}}");
                }
            }
        }
        catch
        {
        }

        return ("DriveCLI.exe", "{prompt}");
    }
}
