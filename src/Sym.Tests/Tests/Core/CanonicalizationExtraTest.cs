// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Collections.Immutable;

namespace SymTest.Tests.Core
{
    [TestClass]
    public sealed class CanonicalizationExtraTest
    {
        [TestMethod]
        [Timeout(10000)]
        public void SimpleCanonicalization_Sanity()
        {
            Number one = new Number(1m);
            Number two = new Number(2m);
            Add a = new Add(ImmutableList.Create<IExpression>(one, two)); // 1 + 2
            Add b = new Add(ImmutableList.Create<IExpression>(two, one)); // 2 + 1

            Assert.IsTrue(a.Equals(b));
        }
    }
}
