using System.IO;
using System.Linq;
using SymRules;
using Xunit;

namespace SymRules.Tests;

public class RulePackLibraryTests
{
    [Fact]
    public void GetDefaultRulePacks_FindsCuratedFolders()
    {
        var packs = RulePackLibrary.GetRulePacks();
        Assert.NotEmpty(packs);
        Assert.Contains(packs, p => p.Path.Contains(Path.Combine("SymRules", "Algebraic")));
        Assert.Contains(packs, p => p.Path.Contains(Path.Combine("SymRules", "SpecialFunctions")));
    }
}
