// Copyright Warren Harding 2026
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace Sym.Tests.Operations
{
    [TestClass]
    public class LimitSeriesTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Limit_Properties()
        {
            var x = new Symbol("x");
            var f = new Divide(new Number(1), x);
            var lim = new Limit(f, x, new Number(0));
            
            Assert.AreEqual(f, lim.TargetExpression);
            Assert.AreEqual(x, lim.Variable);
            Assert.AreEqual(0m, ((Number)lim.Approach).Value);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Limit_DisplayString()
        {
            var x = new Symbol("x");
            var lim = new Limit(x, x, new Number(0));
            Assert.AreEqual("Limit(x, x -> 0)", lim.ToDisplayString());
        }

        [TestMethod]
        [Timeout(10000)]
        public void SeriesExpansion_Properties()
        {
            var x = new Symbol("x");
            var f = new Function("sin", x);
            var ser = new SeriesExpansion(f, x, new Number(0), 5);
            
            Assert.AreEqual(f, ser.TargetExpression);
            Assert.AreEqual(x, ser.Variable);
            Assert.AreEqual(0m, ((Number)ser.Center).Value);
            Assert.AreEqual(5, ser.Order);
        }

        [TestMethod]
        [Timeout(10000)]
        public void SeriesExpansion_DisplayString()
        {
            var x = new Symbol("x");
            var ser = new SeriesExpansion(x, x, new Number(0), 3);
            Assert.AreEqual("Series(x, x around 0, order 3)", ser.ToDisplayString());
        }
    }
}
