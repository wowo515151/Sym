using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;

namespace Sym.Tests.Core
{
    [TestClass]
    public class ExpressionExtensionTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void ContainsSymbol_SymbolFound()
        {
            var x = new Symbol("x");
            var expr = new Add(x, new Number(1));
            Assert.IsTrue(expr.ContainsSymbol(x));
        }

        [TestMethod]
        [Timeout(10000)]
        public void ContainsSymbol_SymbolNotFound()
        {
            var x = new Symbol("x");
            var y = new Symbol("y");
            var expr = new Add(x, new Number(1));
            Assert.IsFalse(expr.ContainsSymbol(y));
        }

        [TestMethod]
        [Timeout(10000)]
        public void ContainsSymbol_Predicate_Success()
        {
            var x = new Symbol("x_1");
            var expr = new Add(x, new Number(1));
            Assert.IsTrue(expr.ContainsSymbol(s => s.Name.StartsWith("x")));
        }

        [TestMethod]
        [Timeout(10000)]
        public void FlattenArguments_DeeplyNested_FlattensCorrectly()
        {
            var x = new Symbol("x");
            // Add(1, Add(2, Add(3, 4)))
            var inner = new Add(new Number(3), new Number(4));
            var mid = new Add(new Number(2), inner);
            var outer = new Add(new Number(1), mid);
            
            var flattened = ExpressionHelpers.FlattenArguments<Add>(outer.Arguments);
            Assert.AreEqual(4, flattened.Count);
        }

        [TestMethod]
        [Timeout(10000)]
        public void SortArguments_ConsistentOrdering()
        {
            var x = new Symbol("x");
            var one = new Number(1);
            var two = new Number(2);
            
            var list1 = ImmutableList.Create<IExpression>(x, two, one);
            var list2 = ImmutableList.Create<IExpression>(one, x, two);
            
            var sorted1 = ExpressionHelpers.SortArguments(list1);
            var sorted2 = ExpressionHelpers.SortArguments(list2);
            
            Assert.IsTrue(ExpressionHelpers.SequencesInternalEquals(sorted1, sorted2));
        }
    }
}
