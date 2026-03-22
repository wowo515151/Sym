using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;

namespace SymCore.Tests;

[TestClass]
public class RewriterCacheTests
{
    [TestMethod]
        [Timeout(10000)]
    public void RewriteSinglePass_SameRulesDifferentInstances_UsesCache()
    {
        var x = new Symbol("x");
        var pattern = new Add(x, new Number(0));
        var replacement = x;
        
        var rules1 = ImmutableList.Create(new Rule(pattern, replacement));
        var rules2 = ImmutableList.Create(new Rule(pattern, replacement));
        
        Assert.AreNotSame(rules1, rules2);
        
        var expr = new Add(new Symbol("a"), new Number(0));
        
        // Warm up cache
        var result1 = Rewriter.RewriteSinglePass(expr, rules1);
        
        // This should be a cache hit even with rules2 because they are logically equivalent
        var result2 = Rewriter.RewriteSinglePass(expr, rules2);
        
        Assert.AreSame(result1, result2);
    }
}
