// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace Sym.Tests.Operations
{
    [TestClass]
    public class DivideSubtractTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Divide_BasicNumeric_Success()
        {
            var div = new Divide(new Number(10), new Number(2));
            var result = div.Canonicalize();
            Assert.IsInstanceOfType(result, typeof(Number));
            Assert.AreEqual(5m, ((Number)result).Value);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Divide_ByZero_ReturnsSymbolic()
        {
            var div = new Divide(new Number(10), new Number(0));
            var result = div.Canonicalize();
            Assert.IsInstanceOfType(result, typeof(Divide));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Divide_Identity_ReturnsOne()
        {
            var x = new Symbol("x");
            var div = new Divide(x, x);
            var result = div.Canonicalize();
            Assert.IsInstanceOfType(result, typeof(Number));
            Assert.AreEqual(1m, ((Number)result).Value);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Divide_Symbolic_ConvertsToMultiply()
        {
            var x = new Symbol("x");
            var y = new Symbol("y");
            var div = new Divide(x, y);
            var result = div.Canonicalize();
            
            // x / y -> x * y^-1
            Assert.IsInstanceOfType(result, typeof(Multiply));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Subtract_BasicNumeric_Success()
        {
            var sub = new Subtract(new Number(10), new Number(3));
            var result = sub.Canonicalize();
            Assert.IsInstanceOfType(result, typeof(Number));
            Assert.AreEqual(7m, ((Number)result).Value);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Subtract_Symbolic_ConvertsToAdd()
        {
            var x = new Symbol("x");
            var y = new Symbol("y");
            var sub = new Subtract(x, y);
            var result = sub.Canonicalize();
            
            // x - y -> x + (-1 * y)
            Assert.IsInstanceOfType(result, typeof(Add));
        }
        
        [TestMethod]
        [Timeout(10000)]
        public void Subtract_Identity_ReturnsZero()
        {
            var x = new Symbol("x");
            var sub = new Subtract(x, x);
            var result = sub.Canonicalize();
            
            // x - x -> x + (-1 * x) -> 0
            Assert.IsInstanceOfType(result, typeof(Number));
            Assert.AreEqual(0m, ((Number)result).Value);
        }
    }
}
