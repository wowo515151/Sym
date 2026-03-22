// Copyright Warren Harding 2025.
using System.Collections.Generic;
using System.Xml.Serialization;


namespace AGIMynd
{
    public class ToolCommand
    {
        public string ToolName { get; set; } = string.Empty;
        public string ToolInput { get; set; } = string.Empty;
        // Optional comma-separated semantic tags for HAMM indexing
        public string Tags { get; set; } = string.Empty;
        // Canonical POSIX-like path (relative to memory root or absolute under memory root)
        public string Path { get; set; } = string.Empty;
    }

    public class ToolCommandList
    {
        [XmlElement("ToolCommand")]
        public List<ToolCommand> Commands { get; set; } = new List<ToolCommand>();
    }

    public class ToolDescription
    {
        public string ToolName { get; set; } = string.Empty;
        public string ToolInputRequirements { get; set; } = string.Empty;
    }

    public class ToolDescriptionList
    {
        public List<ToolDescription> Tools { get; set; } = new List<ToolDescription>();
    }

    public static class Tooling
    {
        public static string CreateToolCommandListXmlSchema()
        {
            // XML Schema (XSD) describing the ToolCommandList format the LLM must emit.
            return Common.ToXmlSchema<ToolCommandList>();
        }

        [Obsolete("Use CreateToolCommandListXmlSchema(). ToolCommandList output is XML (with CDATA for ToolInput when needed).")]
        public static string CreateToolCommandListJsonSchema()
        {
            // Back-compat wrapper: the agent now expects XML, but older callers may still invoke this.
            return CreateToolCommandListXmlSchema();
        }
    }
}
