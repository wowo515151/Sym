// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Xml.Serialization;

namespace AGIMynd
{
    [XmlRoot("MemoryFileList")]
    public class MemoryFileList
    {
        [XmlElement("MemoryFile")]
        public List<MemoryFile> Items { get; set; } = new List<MemoryFile>();
    }

    public class MemoryFile
    {
        public string FileName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }
}
