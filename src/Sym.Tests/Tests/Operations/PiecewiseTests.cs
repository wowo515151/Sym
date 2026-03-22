using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace Sym.Tests.Operations
{
    [TestClass]
    public class PiecewiseTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Piecewise_Basic_Success()
        {
            var x = new Symbol("x");
            var pw = new Piecewise(new Number(1), new Function("gt", x, new Number(0)), new Number(0));
            
            Assert.AreEqual(3, pw.Arguments.Count);
            Assert.AreEqual("1", pw.Arguments[0].ToDisplayString());
            Assert.AreEqual("gt(x, 0)", pw.Arguments[1].ToDisplayString());
            Assert.AreEqual("0", pw.Arguments[2].ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Piecewise_Canonicalize_TrueGuard_ReturnsValue()
        {
            var pw = new Piecewise(new Number(5), new Symbol("true"));
            var result = pw.Canonicalize();
            
            Assert.IsInstanceOfType(result, typeof(Number));
            Assert.AreEqual(5m, ((Number)result).Value);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Piecewise_Canonicalize_FalseGuard_SkipsBranch()
        {
            var x = new Symbol("x");
            var pw = new Piecewise(
                new Number(1), new Symbol("false"),
                new Number(2), new Function("gt", x, new Number(0)),
                new Number(0)
            );
            
            var result = pw.Canonicalize();
            Assert.IsInstanceOfType(result, typeof(Piecewise));
            var canonicalPw = (Piecewise)result;
            
            // Expected: Piecewise(2, gt(x, 0), 0)
            Assert.AreEqual(3, canonicalPw.Arguments.Count);
            Assert.AreEqual(2m, ((Number)canonicalPw.Arguments[0]).Value);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Piecewise_Canonicalize_Nested_DistributesGuard()
        {
            var x = new Symbol("x");
            var y = new Symbol("y");
            
            // Piecewise(Piecewise(1, y > 0, 0), x > 0, -1)
            var inner = new Piecewise(new Number(1), new Function("gt", y, new Number(0)), new Number(0));
            var outer = new Piecewise(inner, new Function("gt", x, new Number(0)), new Number(-1));
            
            var result = outer.Canonicalize();
            
            // Expected: Piecewise(1, and(gt(x, 0), gt(y, 0)), 0, gt(x, 0), -1)
            Assert.IsInstanceOfType(result, typeof(Piecewise));
            var canonicalPw = (Piecewise)result;
            
            // 1, and(gt(x,0), gt(y,0)), 0, gt(x,0), -1 -> 5 arguments
            Assert.AreEqual(5, canonicalPw.Arguments.Count);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Piecewise_DisplayString()
        {
            var x = new Symbol("x");
            var pw = new Piecewise(new Number(1), new Function("gt", x, new Number(0)), new Number(0));
            
            string s = pw.ToDisplayString();
            Assert.AreEqual("Piecewise(1 if gt(x, 0), 0)", s);
        }
    }
}
