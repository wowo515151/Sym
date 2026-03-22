// Copyright Warren Harding 2026
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Algebra;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.Operations;
using SymRules;

namespace SymRules.Tests;

[TestClass]
public class SpecialFunctionsRecurrenceTests
{
    [TestMethod]
        [Timeout(10000)]
    public void RecurrenceRules_ReduceNumericFactorialAndGamma()
    {
        var pack = RulePackLibrary.GetRulePacks()
            .FirstOrDefault(p => p.Name.Equals("SpecialFunctions", System.StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(pack, "SpecialFunctions pack not discovered.");

        var rules = AlgebraicSimplificationRules.SimplificationRules
            .AddRange(IdentityRuleLibrary.RecurrenceRules)
            .AddRange(IdentityRuleLibrary.PiecewiseRules)
            .AddRange(pack.Rules);

        var expr = Sym.CSharpIO.CSharpIO.ParseExpressions("factorial(4) + gamma(4)").First();
        var result = Rewriter.RewriteFully(expr, rules, 64);

        Assert.IsTrue(result.Changed);
        Assert.IsInstanceOfType(result.RewrittenExpression, typeof(Number));
        Assert.AreEqual(30m, ((Number)result.RewrittenExpression).Value);
    }

    [TestMethod]
        [Timeout(10000)]
    public void PiecewiseRules_ExpandAbsIntoPiecewise()
    {
        var rules = AlgebraicSimplificationRules.SimplificationRules
            .AddRange(IdentityRuleLibrary.PiecewiseRules);

        var x = new Symbol("x");
        var absExpr = new Function("abs", ImmutableList.Create<IExpression>(new Add(x, new Number(-3m)).Canonicalize()));
        var expandedAbs = Rewriter.RewriteFully(absExpr, rules, 16).RewrittenExpression;
        Assert.IsInstanceOfType(expandedAbs, typeof(Piecewise));

        var pw = (Piecewise)expandedAbs;
        Assert.AreEqual(4, pw.Arguments.Count);
    }
}
