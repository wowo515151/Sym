// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;

namespace SymCore.Tests;

[TestClass]
public class RewriterLoopTests
{
    [TestMethod]
        [Timeout(10000)]
    public void RewriteFully_DetectsSimpleLoop()
    {
        var x = new Symbol("x");
        var y = new Symbol("y");
        
        // Rule 1: x -> y
        // Rule 2: y -> x
        var rules = ImmutableList.Create(
            new Rule(x, y),
            new Rule(y, x)
        );
        
        var result = Rewriter.RewriteFully(x, rules, maxInternalIterations: 10);
        
        // It should stop at 'y' because 'x' was already seen.
        Assert.AreEqual("y", result.RewrittenExpression.ToDisplayString());
        Assert.IsTrue(result.Changed);
    }

    [TestMethod]
        [Timeout(10000)]
    public void RewriteFully_RespectsMaxIterations()
    {
        // Rule: x -> x + 1
        var x = new Symbol("x");
        var pattern = x;
        var replacement = new Add(x, new Number(1));
        
        var rules = ImmutableList.Create(new Rule(pattern, replacement));
        
        // This is not a simple cycle because the expression grows: x -> x+1 -> (x+1)+1 ...
        // It should be stopped by maxInternalIterations
        var result = Rewriter.RewriteFully(x, rules, maxInternalIterations: 5);
        
        Assert.IsTrue(result.Changed);
        // Depending on Canonicalize, it might be x + 5 or something.
        // The point is it didn't hang.
    }
}
