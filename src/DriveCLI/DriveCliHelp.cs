namespace DriveCLI;

public static class DriveCliHelp
{
    public const string Text =
        "DriveCLI - simple drive/file operations\n" +
        "\n" +
        "Commands:\n" +
        "  help                       Shows this help\n" +
        "  read <path>                Reads a text file to stdout\n" +
        "  write <path> <text...>     Writes text (overwriting)\n" +
        "  append <path> <text...>    Appends text\n" +
        "  exists <path>              Prints true/false and returns 0/1\n" +
        "  delete <path>              Deletes a file if it exists\n" +
        "  mkdir <path>               Creates a directory\n" +
        "  list <directoryPath>       Lists directory entries\n" +
        "  search <path> <pat> [ext]  Searches files for content\n" +
        "\n" +
        "Tip: When creating files, avoiding characters that need to be escaped can help a lot.\n";
}
