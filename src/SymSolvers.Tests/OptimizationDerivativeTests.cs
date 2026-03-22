// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.CSharpIO;
using Sym.Core;
using Sym.Operations;
using System.Collections.Immutable;
using System.Linq;

namespace SymSolvers.Tests;

[TestClass]
public class OptimizationDerivativeTests
{
    [TestMethod]
        [Timeout(10000)]
    public void TestMatrixExtraction()
    {
        // v2_x = 0.6 * v0_x + 0.2 * v0_y
        // v2_y = 0.6 * v0_x + 0.2 * v0_y
        // Extract matrix M from v2 = M * v0
        
        var v0_x = new Symbol("v0_x");
        var v0_y = new Symbol("v0_y");
        var variables = new Vector(ImmutableList.Create<IExpression>(v0_x, v0_y));
        
        var comp1 = new Add(new Multiply(new Number(0.6m), v0_x), new Multiply(new Number(0.2m), v0_y)).Canonicalize();
        var comp2 = new Add(new Multiply(new Number(0.6m), v0_x), new Multiply(new Number(0.2m), v0_y)).Canonicalize();
        var vector = new Vector(ImmutableList.Create(comp1, comp2));
        
        var matrix = LinearSolveHelper.TryExtractMatrix(vector, variables);
        
        Assert.IsNotNull(matrix);
        Assert.AreEqual(2, matrix.MatrixDimensions[0]);
        Assert.AreEqual(2, matrix.MatrixDimensions[1]);
        
        Assert.AreEqual(0.6m, ((Number)matrix.Arguments[0]).Value);
        Assert.AreEqual(0.2m, ((Number)matrix.Arguments[1]).Value);
        Assert.AreEqual(0.6m, ((Number)matrix.Arguments[2]).Value);
        Assert.AreEqual(0.2m, ((Number)matrix.Arguments[3]).Value);
    }
}
