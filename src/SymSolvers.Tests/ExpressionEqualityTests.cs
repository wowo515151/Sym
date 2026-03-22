// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using System.Collections.Immutable;

namespace SymSolvers.Tests
{
    [TestClass]
    public class ExpressionEqualityTests
    {
        private class DummyAtom : Atom
        {
            private readonly string _value;
            public DummyAtom(string value) { _value = value; }

            public override string Head => "Dummy";
            public override Shape Shape => Shape.Scalar;

            public override IExpression Canonicalize() => this;

            public override string ToDisplayString() => _value;

            public override bool InternalEquals(IExpression other)
            {
                return other is DummyAtom d && _value == d._value;
            }

            public override int InternalGetHashCode() => _value?.GetHashCode() ?? 0;
        }

        [TestMethod]
        [Timeout(10000)]
        public void Equals_SameContent_AreEqual()
        {
            var a = new DummyAtom("x");
            var b = new DummyAtom("x");

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a.InternalEquals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [TestMethod]
        [Timeout(10000)]
        public void Equals_DifferentContent_NotEqual()
        {
            var a = new DummyAtom("x");
            var b = new DummyAtom("y");

            Assert.IsFalse(a.Equals(b));
            Assert.IsFalse(a.InternalEquals(b));
        }
    }
}
