// Copyright Warren Harding 2026
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WordsToSym;
using SymSolvers.ProblemStructure;

namespace SymSolvers.Tests.ProblemStructure;

[TestClass]
public class TargetInferenceTests
{
    [TestMethod]
        [Timeout(10000)]
    public void ResolveTarget_ShouldMatchCentsToCent()
    {
        // Arrange
        var problem = new ProblemStruct
        {
            WordProblem = "I have 5 cents.",
            Variables = new List<ProblemStruct.Variable> { new ProblemStruct.Variable { VariableName = "cent", VariableType = "decimal" } }
        };
        var constraints = new List<string> { "cent == 5" };

        // Act
        var result = TargetInference.ResolveTarget(problem, constraints);

        // Assert
        Assert.AreEqual("cent", result.TargetName);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ResolveTarget_ShouldPreferVariableInLastSentence_WithHighBonus()
    {
        // Arrange
        var problem = new ProblemStruct
        {
            WordProblem = "John has apples. Mary has oranges. How many apples does John have?",
            Variables = new List<ProblemStruct.Variable> 
            { 
                new ProblemStruct.Variable { VariableName = "apples", VariableType = "int" }, 
                new ProblemStruct.Variable { VariableName = "oranges", VariableType = "int" } 
            }
        };
        var constraints = new List<string> { "apples = 5", "oranges = 3" };

        // Act
        var result = TargetInference.ResolveTarget(problem, constraints);

        // Assert
        // Both are mentioned in the problem, but 'apples' is in the last sentence.
        Assert.AreEqual("apples", result.TargetName);
    }

    [TestMethod]
        [Timeout(10000)]
    public void ResolveTarget_ShouldHandleVerticesToVertex()
    {
        // Arrange
        var problem = new ProblemStruct
        {
            WordProblem = "A triangle has 3 vertices.",
            Variables = new List<ProblemStruct.Variable> { new ProblemStruct.Variable { VariableName = "vertex", VariableType = "int" } }
        };
        var constraints = new List<string> { "vertex == 3" };

        // Act
        var result = TargetInference.ResolveTarget(problem, constraints);

        // Assert
        Assert.AreEqual("vertex", result.TargetName);
    }
}
