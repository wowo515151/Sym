// Copyright Warren Harding 2026
#nullable enable
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymRules;

namespace SymRules.Tests;

[TestClass]
public class AdditionalRuleTextParserTests
{
    [TestMethod]
        [Timeout(10000)]
    public void TryGenerateCoreSource_WithSimpleName_StripsName()
    {
        string text = "AddZero: a + 0 -> a";
        bool success = RuleTextParser.TryGenerateCoreSource(text, out string core, out string? diag);
        
        Assert.IsTrue(success, diag);
        Assert.AreEqual("Rule(Wild(\"a\") + 0, Wild(\"a\"))", core);
    }

    [TestMethod]
        [Timeout(10000)]
    public void TryGenerateCoreSource_WithComplexPattern_DoesNotStripInternalColon()
    {
        // log(b, x) contains a comma, but if we had log(b:base, x), we want to make sure we don't trip.
        // The fix was checking if arrowIdx > colonIdx and no spaces/( in prefix.
        string text = "LogExpand: log(b, x) -> log(x) / log(b)";
        bool success = RuleTextParser.TryGenerateCoreSource(text, out string core, out string? diag);
        
        Assert.IsTrue(success, diag);
        Assert.AreEqual("Rule(log(Wild(\"b\"), Wild(\"x\")), log(Wild(\"x\")) / log(Wild(\"b\")))", core);
    }

    [TestMethod]
        [Timeout(10000)]
    public void TryGenerateCoreSource_NoName_Works()
    {
        string text = "a + 0 -> a";
        bool success = RuleTextParser.TryGenerateCoreSource(text, out string core, out string? diag);
        
        Assert.IsTrue(success, diag);
        Assert.AreEqual("Rule(Wild(\"a\") + 0, Wild(\"a\"))", core);
    }

    [TestMethod]
        [Timeout(10000)]
    public void TryGenerateCoreSource_TypedWildcards_ReplacedCorrectly()
    {
        string text = "?x:Constant + 0 -> ?x";
        bool success = RuleTextParser.TryGenerateCoreSource(text, out string core, out string? diag);
        
        Assert.IsTrue(success, diag);
        Assert.AreEqual("Rule(Wild(\"x\", Constant) + 0, Wild(\"x\"))", core);
    }
}
