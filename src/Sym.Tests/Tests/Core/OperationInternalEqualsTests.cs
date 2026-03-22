// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;

namespace SymTest
{
    [TestClass]
    public sealed class OperationInternalEqualsTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Add_InternalEquals_SameSequence()
        {
            Symbol a = new Symbol("a");
            Symbol b = new Symbol("b");

            Add add1 = new Add(ImmutableList.Create<IExpression>(a, b));
            Add add2 = new Add(ImmutableList.Create<IExpression>(a, b));

            Assert.IsTrue(add1.InternalEquals(add2));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Add_InternalEquals_DifferentSequence_NotEqual()
        {
            Symbol a = new Symbol("a");
            Symbol b = new Symbol("b");

            Add add1 = new Add(ImmutableList.Create<IExpression>(a, b));
            Add add2 = new Add(ImmutableList.Create<IExpression>(b, a));

            Assert.IsFalse(add1.InternalEquals(add2));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Multiply_InternalEquals_SameSequence()
        {
            Symbol x = new Symbol("x");
            Symbol y = new Symbol("y");

            Multiply m1 = new Multiply(ImmutableList.Create<IExpression>(x, y));
            Multiply m2 = new Multiply(ImmutableList.Create<IExpression>(x, y));

            Assert.IsTrue(m1.InternalEquals(m2));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Multiply_InternalEquals_DifferentSequence_NotEqual()
        {
            Symbol x = new Symbol("x");
            Symbol y = new Symbol("y");

            Multiply m1 = new Multiply(ImmutableList.Create<IExpression>(x, y));
            Multiply m2 = new Multiply(ImmutableList.Create<IExpression>(y, x));

            Assert.IsFalse(m1.InternalEquals(m2));
        }
    }
}
