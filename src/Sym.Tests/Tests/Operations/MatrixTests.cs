using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Operations;
using Sym.Core;
using System.Collections.Immutable;

namespace Sym.Test.Operations
{
    [TestClass]
    public class MatrixTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Matrix_Canonicalize_SimplifiesEntries()
        {
            // [1+1, 2*3] -> [2, 6]
            var dims = ImmutableArray.Create(1, 2);
            var components = ImmutableList.Create<IExpression>(
                new Add(new Number(1), new Number(1)),
                new Multiply(new Number(2), new Number(3))
            );
            var matrix = new Matrix(dims, components);
            var result = (Matrix)matrix.Canonicalize();

            Assert.AreEqual(new Number(2), result.Arguments[0]);
            Assert.AreEqual(new Number(6), result.Arguments[1]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void MatrixMultiply_Symbolic_Works()
        {
            // [x, y] * [z, w]^T
            // Result is [x*z + y*w] (1x1 matrix)
            var x = new Symbol("x");
            var y = new Symbol("y");
            var z = new Symbol("z");
            var w = new Symbol("w");

            var m1 = new Matrix(ImmutableArray.Create(1, 2), ImmutableList.Create<IExpression>(x, y));
            var m2 = new Matrix(ImmutableArray.Create(2, 1), ImmutableList.Create<IExpression>(z, w));

            var product = new MatrixMultiply(m1, m2);
            var result = (Matrix)product.Canonicalize();

            Assert.AreEqual(1, result.MatrixDimensions[0]);
            Assert.AreEqual(1, result.MatrixDimensions[1]);
            
            // Expected: x * z + y * w
            // Due to canonicalization and distribution: x * z + y * w
            Assert.IsInstanceOfType(result.Arguments[0], typeof(Add));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Matrix_ToDisplayString_Formatted()
        {
            var dims = ImmutableArray.Create(2, 2);
            var components = ImmutableList.Create<IExpression>(
                new Number(1), new Number(2),
                new Number(3), new Number(4)
            );
            var matrix = new Matrix(dims, components);
            var display = matrix.ToDisplayString();
            Assert.AreEqual("Matrix(2x2)<[1, 2]; [3, 4]>", display);
        }
    }
}
