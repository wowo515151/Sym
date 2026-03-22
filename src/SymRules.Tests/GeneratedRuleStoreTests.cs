using System;
using System.IO;
using System.Linq;
using Sym.Atoms;
using Sym.Operations;
using Xunit;
using CoreRule = Sym.Core.Rule;

namespace SymRules.Tests;

public class GeneratedRuleStoreTests
{
    [Fact]
    public void AppendRules_PersistsAndDedupes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"genrules_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var store = new GeneratedRuleStore(root, maxRules: 4);
            var x = new Wild("x");

            // Avoid rules that canonicalize into identities (e.g., x+0 -> x), since
            // the rule pipeline stores canonicalized core rules.
            var rule1 = new CoreRule(new Add(x, new Number(1)), new Add(x, new Number(2)));
            var rule2 = new CoreRule(new Multiply(x, new Number(2)), new Multiply(x, new Number(3)));

            var added = store.AppendRules(new[] { rule1, rule2, rule1 });
            Assert.Equal(2, added);

            var path = Path.Combine(root, GeneratedRuleStore.DefaultFileName);
            Assert.True(File.Exists(path));

            var loaded = store.LoadCoreRules();
            Assert.Equal(2, loaded.Count);
            Assert.Contains(loaded, r => r.Pattern is Add);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadSnapshot_ReturnsCounts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gensnap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var store = new GeneratedRuleStore(root, maxRules: 4);
            var x = new Wild("x");
            var rule = new CoreRule(new Add(x, new Number(0)), x);

            store.AppendRules(new[] { rule });

            var snapshot = store.LoadSnapshot();
            Assert.Equal(1, snapshot.Count);
            Assert.Single(snapshot.CoreRules);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
