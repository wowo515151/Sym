//Copyright Warren Harding 2025.
using System.Xml.Serialization;

namespace ConsoleHelmsman;

[XmlRoot("ConsoleAppBases")]
public sealed class ConsoleAppBases
{
    public List<ConsoleAppBase> Items { get; set; } = new();

    // Optional working directory to use when starting console apps.
    public string CurrentDirectory { get; set; } = string.Empty;
}
