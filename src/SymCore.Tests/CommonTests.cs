// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymCore;
using System;

namespace SymCore.Tests;

[TestClass]
public class CommonTests
{
    [TestMethod]
        [Timeout(10000)]
    public void WrapInTags_ValidBody_WrapsCorrectly()
    {
        string body = "hello";
        string tagName = "test";
        string result = Common.WrapInTags(body, tagName);
        Assert.AreEqual("<test>\nhello\n</test>", result);
    }

    [TestMethod]
        [Timeout(10000)]
    public void WrapInTags_NullBody_UsesDefaultMessage()
    {
        string result = Common.WrapInTags(null!, "test");
        Assert.AreEqual("<test>\nNo data available.\n</test>", result);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ExpandSelfClosingXmlTags_SelfClosingTag_Expands()
    {
        string xml = "<tag />";
        string result = Common.ExpandSelfClosingXmlTags(xml);
        Assert.AreEqual("<tag></tag>", result);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ExpandSelfClosingXmlTags_SelfClosingTagWithAttributes_Expands()
    {
        string xml = "<tag attr=\"val\" />";
        string result = Common.ExpandSelfClosingXmlTags(xml);
        Assert.AreEqual("<tag attr=\"val\"></tag>", result);
    }

    [TestClass]
    public class XmlTestObject
    {
        public string? Name { get; set; }
        public string? Content { get; set; }
    }

    [TestMethod]
        [Timeout(10000)]
    public void ToXml_PlainObject_SerializesToXml()
    {
        var obj = new XmlTestObject { Name = "Test", Content = "Normal content" };
        string xml = Common.ToXml(obj);
        Assert.IsTrue(xml.Contains("<Name>Test</Name>"));
        Assert.IsTrue(xml.Contains("<Content>Normal content</Content>"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void ToXml_ObjectWithSpecialChars_WrapsInCData()
    {
        var obj = new XmlTestObject { Name = "Test", Content = "Content with <special> characters & symbols" };
        string xml = Common.ToXml(obj);
        Assert.IsTrue(xml.Contains("<![CDATA[Content with <special> characters & symbols]]>"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void FromXmlOnly_ValidXml_DeserializesCorrectly()
    {
        string xml = "<XmlTestObject><Name>Alice</Name><Content>Some content</Content></XmlTestObject>";
        var result = Common.FromXmlOnly<XmlTestObject>(xml);
        Assert.IsNotNull(result);
        Assert.AreEqual("Alice", result.Name);
        Assert.AreEqual("Some content", result.Content);
    }

    [TestMethod]
        [Timeout(10000)]
    public void FromXml_XmlWithinText_ExtractsAndDeserializes()
    {
        string text = "Some text before <XmlTestObject><Name>Bob</Name></XmlTestObject> some text after";
        var result = Common.FromXml<XmlTestObject>(text);
        Assert.IsNotNull(result);
        Assert.AreEqual("Bob", result.Name);
    }

    [TestMethod]
        [Timeout(10000)]
    public void RemoveXmlDeclaration_WithDeclaration_RemovesIt()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><root></root>";
        string result = Common.RemoveXmlDeclaration(xml);
        Assert.AreEqual("<root></root>", result);
    }

    [TestMethod]
        [Timeout(10000)]
    public void RemoveXmlDeclaration_WithoutDeclaration_ReturnsOriginal()
    {
        string xml = "<root></root>";
        string result = Common.RemoveXmlDeclaration(xml);
        Assert.AreEqual("<root></root>", result);
    }
}
