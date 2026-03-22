//Copyright Warren Harding 2025.
using System.Xml.Serialization;

namespace ConsoleHelmsman.Models
{
    [XmlRoot("ToolCommand")]
    public sealed class ToolCommand
    {
        public string ToolName { get; set; } = string.Empty;
        public string ToolInput { get; set; } = string.Empty;
        // Optional path for commands that operate on files (CreateConcept/DeleteConcept/MoveToSavedFromTools)
        public string Path { get; set; } = string.Empty;
    }
}
