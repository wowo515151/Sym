using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.ProblemStructure;
using WordsToSym;

namespace SymSolvers.Tests.ProblemStructure;

[TestClass]
public sealed class ProblemStructParsingTests
{
    [TestMethod]
        [Timeout(10000)]
    public void ProblemScriptToProblemStruct_ParsesTagsAndNotesAndStripsFromConstraints()
    {
        var ps = ProblemStruct.ProblemScriptToProblemStruct(@"
int x;

<Tags>LinearSystem, Circle</Tags>
<Notes>First line.</Notes>
<Notes>Second line.</Notes>

x == 2;");

        CollectionAssert.AreEquivalent(new[] { "LinearSystem", "Circle" }, ps.Tags);
        Assert.AreEqual("First line." + Environment.NewLine + "Second line.", ps.Notes);

        // Notes/Tags are removed; only the actual constraint remains.
        CollectionAssert.AreEqual(new[] { "x == 2;" }, ps.Constraints);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ProblemScriptToProblemStruct_ConvertsDeclarationsWithInitializersIntoEqualityConstraints()
    {
        var ps = ProblemStruct.ProblemScriptToProblemStruct(@"
int x = 2;
decimal y = 3;");

        // Initializers become equality constraints.
        CollectionAssert.AreEquivalent(new[] { "x == 2;", "y == 3;" }, ps.Constraints);

        // Types are captured when declared as int/decimal.
        Assert.AreEqual("int", ps.Variables.Single(v => v.VariableName == "x").VariableType);
        Assert.AreEqual("decimal", ps.Variables.Single(v => v.VariableName == "y").VariableType);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ProblemScriptToProblemStruct_ConvertsAssignmentsIntoEqualityConstraints()
    {
        var ps = ProblemStruct.ProblemScriptToProblemStruct(@"
x = 2;");

        CollectionAssert.AreEqual(new[] { "x == 2;" }, ps.Constraints);
        Assert.AreEqual(1, ps.Variables.Count);
        Assert.AreEqual("x", ps.Variables[0].VariableName);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ProblemScriptToProblemStruct_IgnoresCommentsAndMarkdownFences()
    {
        var ps = ProblemStruct.ProblemScriptToProblemStruct(@"
```csharp
// comment
int x;
x == 2; // trailing comment
```");

        CollectionAssert.AreEqual(new[] { "x == 2;" }, ps.Constraints);
        CollectionAssert.AreEqual(new[] { "x" }, ps.Variables.Select(v => v.VariableName).ToArray());
    }

    [TestMethod]
        [Timeout(10000)]
    public void ProblemScriptToProblemStruct_HarvestsIdentifiersAndExcludesInvocationTargetsAndMemberNames()
    {
        var ps = ProblemStruct.ProblemScriptToProblemStruct(@"
var v = Vector(a, obj.Prop);
x == a + b;");

        var names = ps.Variables.Select(v => v.VariableName).ToHashSet(StringComparer.Ordinal);

        // Declared variable name always included.
        Assert.IsTrue(names.Contains("v"));

        // Invocation target "Vector" excluded.
        Assert.IsFalse(names.Contains("Vector"));

        // Member name "Prop" excluded, but the receiver "obj" is a variable.
        Assert.IsTrue(names.Contains("obj"));
        Assert.IsFalse(names.Contains("Prop"));

        // Identifiers in expressions are included.
        Assert.IsTrue(names.Contains("a"));
        Assert.IsTrue(names.Contains("b"));
        Assert.IsTrue(names.Contains("x"));
    }

    [TestMethod]
        [Timeout(10000)]
    public void ProblemScriptToProblemStruct_DedupesConstraintsAndVariables()
    {
        var ps = ProblemStruct.ProblemScriptToProblemStruct(@"
int x;
int x;
x == 2;
x == 2;");

        Assert.AreEqual(1, ps.Variables.Count(v => v.VariableName == "x"));
        CollectionAssert.AreEqual(new[] { "x == 2;" }, ps.Constraints);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ProblemScriptToProblemStruct_IgnoresMarkdownAndGarbage()
    {
        var ps = ProblemStruct.ProblemScriptToProblemStruct(@"
# Problem Script Header
<ProblemScript>
int x;
x == 10;
</ProblemScript>

- **Variables**: x is an integer
* Another bullet
**Bold text**

<Tags>Test</Tags>
");

        // Should only contain "x == 10;"
        CollectionAssert.AreEqual(new[] { "x == 10;" }, ps.Constraints, $"Constraints mismatch. Got: {string.Join(", ", ps.Constraints)}");
        Assert.AreEqual(1, ps.Variables.Count, $"Variables count mismatch. Got: {string.Join(", ", ps.Variables.Select(v => v.VariableName))}");
        Assert.AreEqual("x", ps.Variables[0].VariableName);
        CollectionAssert.Contains(ps.Tags, "Test");
    }

    [TestMethod]
        [Timeout(10000)]
    public void ProblemScriptToProblemStruct_ParsesFunctionDefinitionAndExcludesParameter()
    {
        var ps = ProblemStruct.ProblemScriptToProblemStruct(@"
decimal a;
decimal f(decimal x) = a * x + 1;
decimal result = f(5);");

        Assert.IsTrue(ps.Constraints.Contains("f(x) == a * x + 1;"));
        Assert.IsTrue(ps.Constraints.Contains("result == f(5);"));

        var names = ps.Variables.Select(v => v.VariableName).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(names.Contains("a"));
        Assert.IsTrue(names.Contains("result"));
        Assert.IsFalse(names.Contains("x"));
    }
}
