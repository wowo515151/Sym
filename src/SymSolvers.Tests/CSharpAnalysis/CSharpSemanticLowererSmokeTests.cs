#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class CSharpSemanticLowererSmokeTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Lower_UIntSubtraction_PrintsLoweredForms()
        {
            var source = @"
public class Test {
    public void Run(uint len, uint offset) {
        uint remaining = len - offset;
    }
}";

            var tree = CSharpSyntaxTree.ParseText(
                source,
                options: new CSharpParseOptions(kind: SourceCodeKind.Regular),
                path: "input.cs");

            var references = CreateDefaultReferencesForTest();
            var compilation = CSharpCompilation.Create("LoweringSmoke")
                .AddReferences(references)
                .AddSyntaxTrees(tree)
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, checkOverflow: false));

            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var lowerer = new CSharpSemanticLowerer();
            var lowered = lowerer.Lower(semanticModel, root);

            var loweredTexts = lowered
                .Select(p => p.Expression.ToDisplayString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Console.WriteLine("Lowered expressions (distinct):");
            foreach (var text in loweredTexts)
            {
                Console.WriteLine(text);
            }

            Assert.IsTrue(loweredTexts.Count > 0, "Expected at least one lowered expression.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void GuardProver_DebugAssert_ProducesNonNegativeForUnsignedSubtraction()
        {
            var source = @"
using System.Diagnostics;
class Test {
    public uint SafeSub(uint a, uint b) {
        Debug.Assert(a >= b);
        return a - b;
    }
}";

            var tree = CSharpSyntaxTree.ParseText(
                source,
                options: new CSharpParseOptions(kind: SourceCodeKind.Regular),
                path: "input.cs");

            var references = CreateDefaultReferencesForTest();
            var compilation = CSharpCompilation.Create("GuardSmoke")
                .AddReferences(references)
                .AddSyntaxTrees(tree)
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, checkOverflow: false));

            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var subSyntax = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                .First(n => n.IsKind(SyntaxKind.SubtractExpression));

            var subOp = semanticModel.GetOperation(subSyntax) as Microsoft.CodeAnalysis.Operations.IBinaryOperation;
            Assert.IsNotNull(subOp, "Expected Roslyn operation for subtraction.");

            var prover = new CSharpGuardProver();
            var facts = prover.DeriveExpressionFacts(subOp!, semanticModel);

            Console.WriteLine("Derived facts:");
            foreach (var fact in facts)
            {
                Console.WriteLine($"{fact.Kind} | {fact.Subject} | {fact.Evidence}");
            }

            Assert.IsTrue(
                facts.Any(f =>
                    f.Kind == CSharpGuardKind.NonNegative &&
                    f.Subject.Contains("cs_sub_u32_unchecked", StringComparison.Ordinal) &&
                    !f.Evidence.Contains("Type is unsigned", StringComparison.OrdinalIgnoreCase)),
                "Expected Debug.Assert(a >= b) to yield a NonNegative fact for the subtraction (not merely a type-based unsigned fact).");
        }

        private static List<MetadataReference> CreateDefaultReferencesForTest()
        {
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            };

            try
            {
                var assemblyDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
                if (assemblyDir is null)
                {
                    return references;
                }

                AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Runtime.dll"));
                AddReferenceIfExists(references, Path.Combine(assemblyDir, "netstandard.dll"));
                AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Collections.dll"));
                AddReferenceIfExists(references, Path.Combine(assemblyDir, "System.Diagnostics.Debug.dll"));
            }
            catch
            {
                // Best-effort: tests should still run even if optional refs are missing.
            }

            return references;
        }

        private static void AddReferenceIfExists(List<MetadataReference> references, string path)
        {
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }
    }
}
