// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymCore;
using System.Xml.Serialization;

namespace SymCore.Tests;

[TestClass]
public class AttributeHelpersTests
{
    public class TestClassNoAttribute { }

    [XmlRoot("CustomName")]
    public class TestClassWithAttribute { }

    [TestMethod]
        [Timeout(10000)]
    public void GetXmlRootName_NoAttribute_ReturnsTypeName()
    {
        string name = AttributeHelpers.GetXmlRootName(typeof(TestClassNoAttribute));
        Assert.AreEqual("TestClassNoAttribute", name);
    }

    [TestMethod]
        [Timeout(10000)]
    public void GetXmlRootName_WithAttribute_ReturnsElementName()
    {
        string name = AttributeHelpers.GetXmlRootName(typeof(TestClassWithAttribute));
        Assert.AreEqual("CustomName", name);
    }
}
