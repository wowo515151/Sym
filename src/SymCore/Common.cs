// Copyright Warren Harding 2026
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace SymCore;

public static class Common
{
    public static string WrapInTags(string body, string tagName)
    {
        if (string.IsNullOrEmpty(body))
        {
            body = "No data available.";
        }

        return $"<{tagName}>\n{body}\n</{tagName}>";
    }

    public static async Task<string> GetPrompt(string name)
    {
        return await GetPrompt("Prompts", name);
    }

    public static async Task<string> GetPrompt(string folder, string name)
    {
        var promptName = name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? name : name + ".txt";

        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var fullPath = Path.Combine(baseDirectory, folder, promptName);

        if (!File.Exists(fullPath))
        {
            Logging.LogError("CommonGetPromptNotFound", $"Prompt not found: {fullPath}");
            return $"Prompt not found: {fullPath}";
        }

        return await File.ReadAllTextAsync(fullPath);
    }

    public static string ToXml<T>(T obj)
    {
        if (obj == null)
            return "The object to serialize cannot be null.";

        try
        {

            // 1. Serialize the object normally.
            XmlSerializer serializer = new XmlSerializer(typeof(T));
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add("", "");

            var settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = false,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = true,
            };

            string serializedXml;
            using (var stringWriter = new StringWriter())
            using (var xmlWriter = XmlWriter.Create(stringWriter, settings))
            {
                serializer.Serialize(xmlWriter, obj, namespaces);
                serializedXml = stringWriter.ToString();
            }

            // 2. Load the serialized XML into an XDocument.
            XDocument xDoc = XDocument.Parse(serializedXml);
            if (xDoc.Root == null)
                return "ToXml failed. Null xDoc.Root.";

            // 3. Traverse the XML and wrap text nodes in CDATA as needed.
            WrapTextNodesInCData(xDoc.Root);

            // 4. Save the modified XML with indentation.
            var finalSettings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = false,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = true
            };

            using (var sw = new StringWriter())
            using (var xw = XmlWriter.Create(sw, finalSettings))
            {
                xDoc.WriteTo(xw);
                xw.Flush();
                return sw.ToString();
            }
        }
        catch (Exception ex)
        {
            Logging.LogError("CommonSerializeXml", ex.Message, ex.StackTrace);
            return $"Serialization failed for type {typeof(T)}. {ex.Message}";
        }
    }

    /// <summary>
    /// Recursively traverses an XElement and replaces its text nodes with XCData nodes 
    /// if they contain special characters, while avoiding double-wrapping if already present.
    /// </summary>
    private static void WrapTextNodesInCData(XElement element)
    {
        foreach (var node in element.Nodes().ToList())
        {
            // Process only if the node is a plain text node (not already CDATA)
            if (node is XText textNode && !(node is XCData))
            {
                string value = textNode.Value;

                // If the text already includes CDATA markers, remove them.
                if (value.StartsWith("<![CDATA[") && value.EndsWith("]]>"))
                {
                    // Remove the CDATA markers: length of "<![CDATA[" is 9, "]]>" is 3.
                    value = value.Substring(9, value.Length - 12);
                }

                // If the text contains special characters that would be escaped, wrap it in CDATA.
                if (value.Contains("<") || value.Contains(">") || value.Contains("&"))
                {
                    XCData cdata = new XCData(value);
                    textNode.ReplaceWith(cdata);
                }
            }
            else if (node is XElement childElement)
            {
                WrapTextNodesInCData(childElement);
            }
        }
    }

    public static string ToExpandedXml<T>(T obj)
    {
        string xml = ToXml(obj);
        return ExpandSelfClosingXmlTags(xml);
    }

    public static string RemoveXmlDeclaration(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return "Input XML cannot be null or empty.";
        }

        // Regex pattern to match the XML declaration at the start of the string
        string pattern = @"^\s*<\?xml.*?\?>";
        return Regex.Replace(xml, pattern, string.Empty).TrimStart();
    }

    public static string ExpandSelfClosingXmlTags(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return "Input XML cannot be null or empty.";
        }

        // Regex pattern to match self-closing tags of form <TagName/>
        // We trim the second capture group to avoid trailing spaces from the original self-closing tag.
        string pattern = @"<(\w+)([^>]*)\s*/>";

        return Regex.Replace(xml, pattern, m =>
        {
            string tagName = m.Groups[1].Value;
            string attributes = m.Groups[2].Value.TrimEnd();
            return $"<{tagName}{attributes}></{tagName}>";
        });
    }

    /// <summary>
    /// Recursively processes XML nodes to format DateTime elements and wrap text with special characters in CDATA.
    /// Only leaf elements (elements without child elements) are considered for CDATA wrapping.
    /// </summary>
    /// <param name="node">The current XML node.</param>
    private static void FormatDatesAndAddCData(XElement node)
    {
        foreach (var child in node.Elements())
        {
            // Recursively process child elements
            FormatDatesAndAddCData(child);

            // Attempt to parse the element's value as DateTime
            if (DateTime.TryParse(child.Value, out DateTime dateValue))
            {
                // Format the date into a readable string, e.g., "yyyy-MM-dd"
                child.Value = dateValue.ToString("yyyy-MM-dd");
            }

            // Only process elements that do not have child elements (i.e., leaf nodes)
            if (!child.HasElements && ContainsSpecialCharacters(child.Value))
            {
                // Replace the text with a CDATA section
                var cdata = new XCData(child.Value);
                child.RemoveNodes(); // Remove existing text nodes
                child.Add(cdata);
            }
        }
    }

    /// <summary>
    /// Determines if the input text contains special XML characters that require CDATA.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns>True if special characters are present; otherwise, false.</returns>
    private static bool ContainsSpecialCharacters(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Define special XML characters
        char[] specialChars = { '<', '>', '&', '\'', '"' };

        return text.Any(c => specialChars.Contains(c));
    }

    public static T? FromXml<T>(string text)
    {
        // figure out what root element name T wants
        string rootName = GetRootName(typeof(T));

        int start = text.IndexOf("<" + rootName + ">");
        int end = text.IndexOf("</" + rootName + ">");
        if (start >= 0 && end > start)
        {
            // extract that block
            string xml = text.Substring(start, end + rootName.Length + 3 - start);
            T? t = FromXmlOnly<T>(xml);
            if (t != null)
            {
                return t;
            }
        }

        // if none of the blocks worked, return default
        return default;
    }

    // pattern that captures ANY element <something>...</something> (with optional xml decl)
    private const string XmlBlockPattern =
        @"(?s)(?:<\?xml.*?\?>\s*)?(?<xml><(?<tag>[a-zA-Z0-9_:\-]+)[^>]*>.*?</\k<tag>>)";

    private static string GetRootName(Type t)
    {
        // honor [XmlRoot(ElementName = "...")]
        return AttributeHelpers.GetXmlRootName(t);
    }

    public static T? FromXmlOnly<T>(string xmlContent)
    {
        try
        {
            var serializer = new XmlSerializer(typeof(T));
            using var reader = new StringReader(xmlContent);
            return (T?)serializer.Deserialize(reader);
        }
        catch
        {
            return default;
        }
    }
}
