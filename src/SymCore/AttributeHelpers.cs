// Copyright Warren Harding 2026
using System;
using System.Xml.Serialization;

namespace SymCore
{
    // Centralized helper for attribute-related operations to make future removal of reflection easier.
    public static class AttributeHelpers
    {
        public static string GetXmlRootName(Type t)
        {
            // honor [XmlRoot(ElementName = "...")]
            var attr = (XmlRootAttribute?)Attribute.GetCustomAttribute(t, typeof(XmlRootAttribute));
            return !string.IsNullOrWhiteSpace(attr?.ElementName) ? attr!.ElementName : t.Name;
        }
    }
}
