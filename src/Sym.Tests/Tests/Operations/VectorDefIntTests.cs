using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace Sym.Tests.Operations
{
    [TestClass]
    public class VectorDefIntTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Vector_Shape_Success()
        {
            var v = new Vector(new Number(1), new Number(2), new Number(3));
            Assert.IsTrue(v.Shape.IsVector);
            Assert.AreEqual(3, v.Shape.Dimensions[0]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Vector_Equality()
        {
            var v1 = new Vector(new Number(1), new Number(2));
            var v2 = new Vector(new Number(1), new Number(2));
            var v3 = new Vector(new Number(1), new Number(3));
            
            Assert.IsTrue(v1.InternalEquals(v2));
            Assert.IsFalse(v1.InternalEquals(v3));
        }

        [TestMethod]
        [Timeout(10000)]
        public void DefiniteIntegral_Properties()
        {
            var x = new Symbol("x");
            var f = new Power(x, new Number(2));
            var di = new DefiniteIntegral(f, x, new Number(0), new Number(1));
            
            Assert.AreEqual(f, di.TargetExpression);
            Assert.AreEqual(x, di.Variable);
            Assert.AreEqual(0m, ((Number)di.LowerBound).Value);
            Assert.AreEqual(1m, ((Number)di.UpperBound).Value);
        }

        [TestMethod]
        [Timeout(10000)]
        public void DefiniteIntegral_DisplayString()
        {
            var x = new Symbol("x");
            var di = new DefiniteIntegral(x, x, new Number(0), new Number(1));
            Assert.AreEqual("DefiniteIntegral(x, x, 0, 1)", di.ToDisplayString());
        }
    }
}
