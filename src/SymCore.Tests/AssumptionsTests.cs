// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymCore;
using System.Collections.Generic;

namespace SymCore.Tests;

[TestClass]
public class AssumptionsTests
{
    [TestMethod]
        [Timeout(10000)]
    public void Constructor_SetsDefaultDomain()
    {
        var assumptions = new Assumptions();
        Assert.IsFalse(assumptions.HasAny);
    }

    [TestMethod]
        [Timeout(10000)]
    public void IsPositive_WhenSet_ReturnsTrue()
    {
        var assumptions = new Assumptions(positive: new[] { "x" });
        Assert.IsTrue(assumptions.IsPositive("x"));
        Assert.IsTrue(assumptions.IsPositive(" x ")); // Normalization check
        Assert.IsFalse(assumptions.IsPositive("y"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IsReal_WhenSetAsPositive_ReturnsTrue()
    {
        var assumptions = new Assumptions(positive: new[] { "x" });
        Assert.IsTrue(assumptions.IsReal("x"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IsReal_WhenSetAsInteger_ReturnsTrue()
    {
        var assumptions = new Assumptions(integer: new[] { "n" });
        Assert.IsTrue(assumptions.IsReal("n"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IsReal_WhenInRealDomainAndNotComplex_ReturnsTrue()
    {
        var assumptions = new Assumptions(domain: "Real");
        Assert.IsTrue(assumptions.IsReal("anything"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IsReal_WhenInRealDomainAndExplicitlyComplex_ReturnsFalse()
    {
        var assumptions = new Assumptions(complex: new[] { "z" }, domain: "Real");
        Assert.IsFalse(assumptions.IsReal("z"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void FromAdditionalData_ParsesCorrectly()
    {
        var data = new Dictionary<string, object>
        {
            { "AssumePositive", "x" },
            { "AssumeInteger", new[] { "n", "m" } },
            { "Domain", "Complex" }
        };
        var assumptions = Assumptions.FromAdditionalData(data);
        Assert.IsTrue(assumptions.IsPositive("x"));
        Assert.IsTrue(assumptions.IsInteger("n"));
        Assert.IsTrue(assumptions.IsInteger("m"));
        Assert.IsTrue(assumptions.IsComplex("any")); // Because domain is Complex
    }

    [TestMethod]
        [Timeout(10000)]
    public void FromAdditionalData_HandlesEmptyData()
    {
        var assumptions = Assumptions.FromAdditionalData(null);
        Assert.IsTrue(assumptions.IsReal("x")); // Default is Real
        Assert.IsFalse(assumptions.IsPositive("x"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IsReal_WhenDomainIsComplex_OnlyTrueIfExplicitlyReal()
    {
        var assumptions = new Assumptions(real: new[] { "x" }, domain: "Complex");
        Assert.IsTrue(assumptions.IsReal("x"));
        Assert.IsFalse(assumptions.IsReal("y"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void IsComplex_WhenDomainIsReal_ReturnsTrueForRealSymbols()
    {
        var assumptions = new Assumptions(domain: "Real");
        Assert.IsTrue(assumptions.IsComplex("any")); // Real is a subset of Complex
    }
}
