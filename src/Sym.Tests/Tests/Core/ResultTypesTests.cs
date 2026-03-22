using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.Rewriters;

namespace Sym.Tests.Core
{
    [TestClass]
    public class ResultTypesTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void MatchResult_Equality()
        {
            var x = new Symbol("x");
            var bindings1 = ImmutableDictionary.CreateBuilder<string, IExpression>();
            bindings1.Add("a", x);
            var mr1 = new MatchResult(true, bindings1.ToImmutable());

            var bindings2 = ImmutableDictionary.CreateBuilder<string, IExpression>();
            bindings2.Add("a", new Symbol("x"));
            var mr2 = new MatchResult(true, bindings2.ToImmutable());

            Assert.AreEqual(mr1, mr2);
            Assert.AreEqual(mr1.GetHashCode(), mr2.GetHashCode());
        }

        [TestMethod]
        [Timeout(10000)]
        public void RewriterResult_Properties()
        {
            var x = new Symbol("x");
            var res = new RewriterResult(x, true);
            Assert.AreEqual(x, res.RewrittenExpression);
            Assert.IsTrue(res.Changed);
        }

        [TestMethod]
        [Timeout(10000)]
        public void SolveResult_Properties()
        {
            var x = new Symbol("x");
            var trace = ImmutableList.Create<IExpression>(x);
            var res = SolveResult.Success(x, "Done", trace);
            
            Assert.IsTrue(res.IsSuccess);
            Assert.AreEqual(x, res.ResultExpression);
            Assert.AreEqual("Done", res.Message);
            Assert.IsNotNull(res.Trace);
            Assert.AreEqual(1, res.Trace.Count);
        }
    }
}
