// Copyright Warren Harding 2026
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace AGIMynd;

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
        string pattern = @"<(\w+)([^>]*?)\s*/>";

        // Replace with the expanded form <TagName></TagName>
        return Regex.Replace(xml, pattern, "<$1$2></$1>");
    }

    public static T? FromXml<T>(string text)
    {
        string rootName = GetRootName(typeof(T));
        
        // Match <TagName ...> ... </TagName> (Greedy match to handle nested tags of same name)
        string pattern = $@"(?s)<{rootName}[^>]*>.*</{rootName}>";
        var match = Regex.Match(text, pattern);

        if (match.Success)
        {
            return FromXmlOnly<T>(match.Value);
        }

        return default;
    }

    private static string GetRootName(Type t)
    {
        // honor [XmlRoot(ElementName = "...")]
        var attr = (XmlRootAttribute?)Attribute.GetCustomAttribute(t, typeof(XmlRootAttribute));
        return !string.IsNullOrWhiteSpace(attr?.ElementName)
            ? attr!.ElementName
            : t.Name; // fallback to type name
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

    public static string ToXmlSchema<TRoot>(
    XmlAttributeOverrides? overrides = null,
    bool indent = true)
    {
        // ----------------------------------------------------------
        // 1. Reflect & export
        // ----------------------------------------------------------
        var importer = overrides == null
            ? new XmlReflectionImporter()
            : new XmlReflectionImporter(overrides);

        var mapping = importer.ImportTypeMapping(typeof(TRoot));
        var schemas = new XmlSchemas();
        var exporter = new XmlSchemaExporter(schemas);

        exporter.ExportTypeMapping(mapping);
        schemas.Compile(null, true);                      // merge + validate

        // Only the first schema is needed after Compile().
        var schema = schemas[0];

        // ----------------------------------------------------------
        // 2. Add handy <xs:documentation> comments for lists/arrays
        // ----------------------------------------------------------
        AnnotateCollections(schema);

        // ----------------------------------------------------------
        // 3. Dump to string
        // ----------------------------------------------------------
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = indent,
            OmitXmlDeclaration = false
        };

        using var xw = XmlWriter.Create(sb, settings);
        schema.Write(xw);

        return sb.ToString();
    }

    /* ========== helpers ========== */

    private static void AnnotateCollections(XmlSchema schema)
    {
        foreach (XmlSchemaElement root in schema.Elements.Values)
            if (root.ElementSchemaType is XmlSchemaComplexType ct)
                AnnotateComplexType(ct, schema);
    }

    private static void AnnotateComplexType(
        XmlSchemaComplexType ct,
        XmlSchema schema)
    {
        if (ct.ContentTypeParticle is XmlSchemaSequence seq)
        {
            foreach (var item in seq.Items.OfType<XmlSchemaElement>())
            {
                // Determine if the element represents a list
                bool isList = item.MaxOccurs > 1 ||
                                item.MaxOccursString == "unbounded";

                if (isList)
                {
                    string itemTypeName = GetElementTypeName(item, schema);
                    AddDocumentation(item,
                        $"LIST OF {itemTypeName}");
                }

                if (item.ElementSchemaType is XmlSchemaComplexType nestedCt)
                    AnnotateComplexType(nestedCt, schema);
            }
        }
    }

    private static string GetElementTypeName(XmlSchemaElement el, XmlSchema schema)
    {
        // Try local type first
        if (!string.IsNullOrEmpty(el.SchemaTypeName.Name))
            return el.SchemaTypeName.Name;

        // Fall back to anonymous complex type name, if any
        return el.ElementSchemaType?.Name ?? "UNKNOWN";
    }

            private static void AddDocumentation(XmlSchemaAnnotated node, string text)
    {
        // Avoid duplicates
        bool already = node.Annotation?
            .Items.OfType<XmlSchemaDocumentation>()
            .Any(d => d.Markup?.Any(m => m!.InnerText == text) == true) == true;

        if (already) return;

        var doc = new XmlSchemaDocumentation();
        doc.Markup = new[] { new XmlDocument().CreateTextNode(text) };

        node.Annotation ??= new XmlSchemaAnnotation();
        node.Annotation.Items.Add(doc);
    }

}
