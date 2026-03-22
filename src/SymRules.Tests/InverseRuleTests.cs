using System;
using SymRules.Matrix;
using Xunit;

namespace SymRules.Tests
{
    public class InverseRuleTests
    {
        [Fact]
        public void Invert_Identity2x2_ReturnsIdentity()
        {
            var input = new double[,] { { 1, 0 }, { 0, 1 } };

            var result = InverseRule.Invert(input);

            Assert.IsType<double[,]>(result);
            var matrix = (double[,])result;
            Assert.Equal(1d, matrix[0, 0], 12);
            Assert.Equal(0d, matrix[0, 1], 12);
            Assert.Equal(0d, matrix[1, 0], 12);
            Assert.Equal(1d, matrix[1, 1], 12);
        }

        [Fact]
        public void Invert_Diagonal2x2_InvertsDiagonal()
        {
            var input = new double[,] { { 2, 0 }, { 0, 4 } };

            var result = InverseRule.Invert(input);

            Assert.IsType<double[,]>(result);
            var matrix = (double[,])result;
            Assert.Equal(0.5d, matrix[0, 0], 12);
            Assert.Equal(0d, matrix[0, 1], 12);
            Assert.Equal(0d, matrix[1, 0], 12);
            Assert.Equal(0.25d, matrix[1, 1], 12);
        }

        [Fact]
        public void Invert_NullMatrix_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => InverseRule.Invert(null!));
        }
    }
}
