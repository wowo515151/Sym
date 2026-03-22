using System.Text;

namespace DriveCLI;

public static class DriveCliProgram
{
    public static Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Out.WriteLine(DriveCliHelp.Text);
            return Task.FromResult(0);
        }

        var command = args[0].Trim();
        return command.ToLowerInvariant() switch
        {
            "help" or "--help" or "-h" => PrintHelpAsync(),
            "read" => ReadAsync(args),
            "write" => WriteAsync(args),
            "append" => AppendAsync(args),
            "exists" => ExistsAsync(args),
            "delete" => DeleteAsync(args),
            "mkdir" => MkdirAsync(args),
            "list" => ListAsync(args),
            "search" => SearchAsync(args),
            _ => UnknownAsync(command)
        };
    }

    private static Task<int> PrintHelpAsync()
    {
        Console.Out.WriteLine(DriveCliHelp.Text);
        return Task.FromResult(0);
    }

    private static Task<int> UnknownAsync(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Run: DriveCLI help");
        return Task.FromResult(2);
    }

    private static async Task<int> ReadAsync(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: DriveCLI read <path>");
            return 2;
        }

        var path = args[1];
        var text = await DriveFunctions.ReadAllTextAsync(path).ConfigureAwait(false);
        Console.Out.Write(text);
        return 0;
    }

    private static async Task<int> WriteAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: DriveCLI write <path> <text...>");
            return 2;
        }

        var path = args[1];
        var text = string.Join(' ', args.Skip(2));
        await DriveFunctions.WriteAllTextAsync(path, text, overwrite: true).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> AppendAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: DriveCLI append <path> <text...>");
            return 2;
        }

        var path = args[1];
        var text = string.Join(' ', args.Skip(2));
        await DriveFunctions.AppendAllTextAsync(path, text).ConfigureAwait(false);
        return 0;
    }

    private static Task<int> ExistsAsync(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: DriveCLI exists <path>");
            return Task.FromResult(2);
        }

        var path = args[1];
        var exists = DriveFunctions.FileExists(path) || DriveFunctions.DirectoryExists(path);
        Console.Out.WriteLine(exists ? "true" : "false");
        return Task.FromResult(exists ? 0 : 1);
    }

    private static Task<int> DeleteAsync(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: DriveCLI delete <path>");
            return Task.FromResult(2);
        }

        DriveFunctions.DeleteFileIfExists(args[1]);
        return Task.FromResult(0);
    }

    private static Task<int> MkdirAsync(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: DriveCLI mkdir <path>");
            return Task.FromResult(2);
        }

        DriveFunctions.CreateDirectory(args[1]);
        return Task.FromResult(0);
    }

    private static Task<int> ListAsync(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: DriveCLI list <directoryPath>");
            return Task.FromResult(2);
        }

        foreach (var entry in DriveFunctions.EnumerateFileSystemEntries(args[1]))
        {
            Console.Out.WriteLine(entry);
        }

        return Task.FromResult(0);
    }

    private static Task<int> SearchAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: DriveCLI search <directoryPath> <pattern> [extension]");
            return Task.FromResult(2);
        }

        var directoryPath = args[1];
        var pattern = args[2];
        var extension = args.Length > 3 ? args[3] : "*.*";
        if (!extension.StartsWith("*"))
        {
            if (extension.StartsWith(".")) extension = "*" + extension;
            else extension = "*." + extension;
        }

        foreach (var file in DriveFunctions.SearchFiles(directoryPath, pattern, extension))
        {
            Console.Out.WriteLine(file);
        }

        return Task.FromResult(0);
    }
}
