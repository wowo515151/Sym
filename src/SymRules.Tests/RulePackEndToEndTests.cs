using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Sym.Algebra;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.Rewriters;
using Sym.Operations;
using Xunit;

using CoreRule = Sym.Core.Rule;

namespace SymRules.Tests
{
    public class RulePackEndToEndTests
    {
        [Fact]
        public void VectorAndMatrixPacksRewriteCompositeExpression()
        {
            var packs = RulePackLibrary.GetRulePacks();
            var vectorPack = packs.FirstOrDefault(p => p.Name.Equals("Vector", StringComparison.OrdinalIgnoreCase));
            var matrixPack = packs.FirstOrDefault(p => p.Name.Equals("MatrixStrategy", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(vectorPack);
            Assert.NotNull(matrixPack);

            var coreRules = new List<CoreRule>();
            coreRules.AddRange(vectorPack.Rules);
            coreRules.AddRange(matrixPack.Rules);
            coreRules.AddRange(AlgebraicSimplificationRules.SimplificationRules);

            var vecShape = new Shape(ImmutableArray.Create(3));
            var matShape = new Shape(ImmutableArray.Create(3, 3));

            var a = new Symbol("a", vecShape);
            var b = new Symbol("b", vecShape);
            var c = new Symbol("c", vecShape);
            var d = new Symbol("d", vecShape);
            var m = new Symbol("M", matShape);
            var identity = new Symbol("Identity", matShape);

            var dotSection = new Add(
                new DotProduct(new Add(a, b).Canonicalize(), new Add(c, d).Canonicalize()),
                new DotProduct(new Number(0m), b)).Canonicalize();

            var matrixSection = new MatrixMultiply(identity, new MatrixMultiply(m, a)).Canonicalize();

            var composed = new Add(dotSection, matrixSection).Canonicalize();
            var rewritten = Rewriter.RewriteFully(composed, coreRules.ToImmutableList(), 32);

            Assert.True(rewritten.Changed, "Composite expression should be rewritten by pack rules.");

            var expectedDot = new Add(
                new DotProduct(a, c),
                new Add(new DotProduct(a, d), new Add(new DotProduct(b, c), new DotProduct(b, d)))).Canonicalize();
            var expected = new Add(expectedDot, new MatrixMultiply(m, a)).Canonicalize();

            Assert.True(rewritten.RewrittenExpression.InternalEquals(expected),
                $"Rewritten expression mismatch: {rewritten.RewrittenExpression.ToDisplayString()}");
        }
    }
}
