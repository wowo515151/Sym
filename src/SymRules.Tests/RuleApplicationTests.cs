// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using Sym.CSharpIO;
using Sym.Core.Rewriters;
using SymRules;
using Xunit;

namespace SymRules.Tests;

public class RuleApplicationTests
{
    [Fact]
    public void AlgebraicPack_Applies_DifferenceOfSquares()
    {
        var rules = LoadPack("Algebraic");
        var expr = CSharpIO.ParseExpressions("Pow(a,2) - Pow(b,2)").First().Canonicalize();
        var expected = CSharpIO.ParseExpressions("(a - b)*(a + b)").First().Canonicalize();

        var result = Rewriter.RewriteFully(expr, rules, 16);

        Assert.True(result.RewrittenExpression.InternalEquals(expected), $"Got {result.RewrittenExpression.ToDisplayString()}");
    }

    [Fact]
    public void TrigPack_Applies_PythagoreanIdentity()
    {
        var rules = LoadPack("Trigonometry");
        var expr = CSharpIO.ParseExpressions("Pow(sin(x),2) + Pow(cos(x),2)").First().Canonicalize();
        var expected = CSharpIO.ParseExpressions("1").First().Canonicalize();

        var result = Rewriter.RewriteFully(expr, rules, 8);

        Assert.True(result.RewrittenExpression.InternalEquals(expected), $"Got {result.RewrittenExpression.ToDisplayString()}");
    }

    [Fact]
    public void VectorPack_Distributes_DotProduct()
    {
        var rules = LoadPack("Vector");
        var expr = CSharpIO.ParseExpressions("(u + v) . w").First().Canonicalize();
        var expected = CSharpIO.ParseExpressions("u . w + v . w").First().Canonicalize();

        var result = Rewriter.RewriteFully(expr, rules, 8);

        Assert.True(result.RewrittenExpression.InternalEquals(expected), $"Got {result.RewrittenExpression.ToDisplayString()}");
    }

    private static ImmutableList<Sym.Core.Rule> LoadPack(string packName)
    {
        var path = FindPack(packName);
        var parsed = RuleLoader.LoadRules(path)
            .Where(r => string.IsNullOrWhiteSpace(r.Diagnostics))
            .Select(r => r.ToCoreRule())
            .ToImmutableList();
        if (parsed.Count == 0)
        {
            throw new InvalidOperationException($"No rules loaded for pack {packName}");
        }
        return parsed;
    }

    private static string FindPack(string packName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "..", "..", "..", "..", "src", "SymRules", packName);
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Unable to locate rule pack {packName}");
    }
}
