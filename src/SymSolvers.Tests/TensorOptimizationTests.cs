// Copyright Warren Harding 2026
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers;
using SymRules;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System;

namespace SymSolvers.Tests
{
    [TestClass]
    public class TensorOptimizationTests
    {
        private EGraphSolverStrategy _solver;
        private ImmutableList<Sym.Core.Rule> _rules;

        [TestInitialize]
        public void Setup()
        {
            _solver = new EGraphSolverStrategy();
            // Load Tensor rules
            var packs = RulePackLibrary.GetRulePacks();
            var tensorPack = packs.FirstOrDefault(p => p.Name == "Tensor");
            if (tensorPack != null)
            {
                _rules = tensorPack.Rules;
            }
            else
            {
                _rules = ImmutableList<Sym.Core.Rule>.Empty;
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestFusedMatMulAdd()
        {
            // A * B + C -> FusedMatMulAdd(A, B, C)
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 20)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(20, 30)));
            var C = new Symbol("C", new Shape(ImmutableArray.Create(10, 30)));

            var expr = new TensorAdd(new MatMul(A, B), C);

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType(result.ResultExpression, typeof(FusedMatMulAdd));
            var fused = (FusedMatMulAdd)result.ResultExpression;
            Assert.AreEqual(A, fused.Arguments[0]);
            Assert.AreEqual(B, fused.Arguments[1]);
            Assert.AreEqual(C, fused.Arguments[2]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestFusedMatMulAddRelu()
        {
            // Relu(A * B + C) -> FusedMatMulAddRelu(A, B, C)
            var A = new Symbol("A", new Shape(ImmutableArray.Create(64, 128)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(128, 64)));
            var C = new Symbol("C", new Shape(ImmutableArray.Create(64, 64)));

            // Construct: Relu(TensorAdd(MatMul(A, B), C))
            var expr = new Relu(new TensorAdd(new MatMul(A, B), C));

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType(result.ResultExpression, typeof(FusedMatMulAddRelu));
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTransposeProduct()
        {
            // Transpose(A * B) -> Transpose(B) * Transpose(A)
            // The expansion (B^T * A^T) incurs more memory traffic (intermediate transposes).
            // EGraph should prefer Transpose(MatMul(A, B)) (the original form).
            // Since the solver returns Failure if no *better* form is found, we expect !IsSuccess.
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 10)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(10, 10)));
            var expr = new Transpose(new MatMul(A, B));

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            // Solver should find that the input is already the best form.
            Assert.IsFalse(result.IsSuccess, "Solver should not find a better form than the optimal input.");
            Assert.AreEqual(expr, result.ResultExpression);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTransposeOptimization()
        {
            // MatMul(Transpose(A), Transpose(B)) -> Transpose(MatMul(B, A))
            // This reduces memory traffic.
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(20, 10)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(30, 20)));
            
            // Inefficient form: Transpose(A) * Transpose(B)
            var expr = new MatMul(new Transpose(A), new Transpose(B));

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Transpose));
            var trans = (Transpose)result.ResultExpression;
            Assert.IsInstanceOfType(trans.Arguments[0], typeof(MatMul));
            var innerMul = (MatMul)trans.Arguments[0];
            
            // Expected: Transpose(MatMul(B, A))
            Assert.AreEqual(B, innerMul.Arguments[0]);
            Assert.AreEqual(A, innerMul.Arguments[1]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTensorFactoring()
        {
            // Test Factoring: A*B + A*C -> A*(B + C)
            // This is a "medium complexity" search that relies on the new Factoring rule and the Cost Model preferring lower FLOPs.
            // A*(B+C) has 1 MatMul. A*B + A*C has 2 MatMuls.
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(32, 32)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(32, 32)));
            var C = new Symbol("C", new Shape(ImmutableArray.Create(32, 32)));

            // Inefficient start: A*B + A*C
            var expr = new TensorAdd(new MatMul(A, B), new MatMul(A, C));

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            Assert.IsInstanceOfType(result.ResultExpression, typeof(MatMul));
            
            var matMul = (MatMul)result.ResultExpression;
            Assert.AreEqual(A, matMul.Arguments[0]);
            Assert.IsInstanceOfType(matMul.Arguments[1], typeof(TensorAdd));
            
            var innerAdd = (TensorAdd)matMul.Arguments[1];
            // Arguments of Add might be reordered
            Assert.AreEqual(B, innerAdd.Arguments[0]);
            Assert.AreEqual(C, innerAdd.Arguments[1]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTransposeAddDistribution()
        {
            // Verify Transpose(Transpose(A) + Transpose(B)) -> A + B.
            // This is strictly cheaper (0 T vs 1 T + 2 T inputs).
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 20)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(10, 20)));
            
            var inner = new TensorAdd(new Transpose(A), new Transpose(B));
            var target = new Transpose(inner);
            
            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(target, context);
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType(result.ResultExpression, typeof(TensorAdd));
            var add = (TensorAdd)result.ResultExpression;
            Assert.AreEqual(A, add.Arguments[0]);
            Assert.AreEqual(B, add.Arguments[1]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTransposeFactoredOptimization()
        {
            // Verify: Transpose(A * (B^T + C^T)) -> (B + C) * A^T
            // This tests the chain: Transpose(MatMul) -> Transpose(Sum) -> Cancel Transpose.
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 20)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(10, 20)));
            var C = new Symbol("C", new Shape(ImmutableArray.Create(10, 20)));

            var BT = new Transpose(B);
            var CT = new Transpose(C);
            var innerSum = new TensorAdd(BT, CT);
            
            var expr = new Transpose(new MatMul(A, innerSum));

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            Assert.IsInstanceOfType(result.ResultExpression, typeof(MatMul));
            
            var matMul = (MatMul)result.ResultExpression;
            // Expected: (B + C) * A^T
            Assert.IsInstanceOfType(matMul.Arguments[0], typeof(TensorAdd));
            Assert.IsInstanceOfType(matMul.Arguments[1], typeof(Transpose));
            
            var sum = (TensorAdd)matMul.Arguments[0];
            Assert.AreEqual(B, sum.Arguments[0]);
            Assert.AreEqual(C, sum.Arguments[1]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestReluIdempotence()
        {
            // Verify: Relu(Relu(MatMul(A, B))) -> Relu(MatMul(A, B))
            // This tests the idempotence rule Relu(Relu(x)) -> Relu(x).
            // It simplifies the computation graph by removing redundant operations.
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 10)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(10, 10)));
            
            // Relu(Relu(A * B))
            var expr = new Relu(new Relu(new MatMul(A, B)));

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize Relu(Relu(...)).");
            
            // Expected: Relu(MatMul(A, B))
            // Or possibly FusedMatMulAddRelu(A, B, 0)? No, Relu(MatMul) is just Relu(MatMul) unless Fused logic matches.
            // Current rules map Relu(TensorAdd(MatMul, ?)) -> Fused.
            // Is there a rule Relu(MatMul) -> Fused? No.
            // So it should remain Relu(MatMul).
            
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Relu));
            var relu = (Relu)result.ResultExpression;
            Assert.IsInstanceOfType(relu.Arguments[0], typeof(MatMul));
            var mm = (MatMul)relu.Arguments[0];
            Assert.AreEqual(A, mm.Arguments[0]);
            Assert.AreEqual(B, mm.Arguments[1]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestIdentityElimination()
        {
            // Verify: Transpose(MatMul(MatMul(A, 1), B)) -> Transpose(MatMul(A, B))
            // This tests:
            // 1. MatMul(A, 1) -> A.
            // 2. Preservation of outer structure.
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 20)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(20, 10)));
            var one = new Sym.Atoms.Number(1);
            
            // Transpose((A * 1) * B)
            var expr = new Transpose(new MatMul(
                new MatMul(A, one),
                B
            ));

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            
            // Expected: Transpose(MatMul(A, B))
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Transpose));
            var trans = (Transpose)result.ResultExpression;
            
            Assert.IsInstanceOfType(trans.Arguments[0], typeof(MatMul));
            var mm = (MatMul)trans.Arguments[0];
            Assert.AreEqual(A, mm.Arguments[0]);
            Assert.AreEqual(B, mm.Arguments[1]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTensorGrouping()
        {
            // Verify: TensorAdd(A, TensorAdd(B, A)) -> TensorAdd(TensorMul(A, 2), B)
            // This tests:
            // 1. Commutativity: A + (B + A) -> A + (A + B)
            // 2. Associativity: A + (A + B) -> (A + A) + B
            // 3. Simplification: A + A -> A * 2
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 10)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(10, 10)));
            
            // A + (B + A)
            var expr = new TensorAdd(A, new TensorAdd(B, A));

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            
            // Expected: TensorAdd(TensorMul(A, 2), B)
            Assert.IsInstanceOfType(result.ResultExpression, typeof(TensorAdd));
            var add = (TensorAdd)result.ResultExpression;
            
            // Order depends on canonicalization of TensorAdd(Mul, Symbol)
            // "TensorMul" vs "B". "B" < "TensorMul"? Or B comes first?
            // Usually simpler terms come first.
            
            IExpression termMul = null;
            IExpression termB = null;
            
            foreach(var arg in add.Arguments)
            {
                if (arg.Equals(B)) termB = arg;
                else if (arg is TensorMul) termMul = arg;
            }
            
            Assert.IsNotNull(termB, "B should be present");
            Assert.IsNotNull(termMul, "TensorMul(A, 2) should be present");
            
            var mul = (TensorMul)termMul;
            bool hasA = mul.Arguments.Any(arg => arg.Equals(A));
            bool has2 = mul.Arguments.Any(arg => arg is Sym.Atoms.Number n && n.Value == 2);
            
            Assert.IsTrue(hasA, "Mul should contain A");
            Assert.IsTrue(has2, "Mul should contain 2");
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestRightFactoring()
        {
            // Verify: A*C + B*C -> (A + B)*C
            // Right factoring optimization.
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 20)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(10, 20)));
            var C = new Symbol("C", new Shape(ImmutableArray.Create(20, 10)));
            
            // A*C + B*C
            var expr = new TensorAdd(
                new MatMul(A, C),
                new MatMul(B, C)
            );

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            
            // Expected: MatMul(TensorAdd(A, B), C)
            Assert.IsInstanceOfType(result.ResultExpression, typeof(MatMul));
            var matMul = (MatMul)result.ResultExpression;
            
            Assert.IsInstanceOfType(matMul.Arguments[0], typeof(TensorAdd));
            var sum = (TensorAdd)matMul.Arguments[0];
            
            // Sum arguments are commutative, check presence
            bool hasA = sum.Arguments.Any(arg => arg.Equals(A));
            bool hasB = sum.Arguments.Any(arg => arg.Equals(B));
            Assert.IsTrue(hasA && hasB);
            
            Assert.AreEqual(C, matMul.Arguments[1]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestReluTransposeSimplification()
        {
            // Verify: Transpose(Relu(Transpose(A))) -> Relu(A)
            // This tests:
            // 1. Transpose(Relu(X)) -> Relu(Transpose(X))  (New rule)
            // 2. Transpose(Transpose(A)) -> A
            // Result is Relu(A).
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 20)));
            
            // Transpose(Relu(Transpose(A)))
            var expr = new Transpose(
                new Relu(
                    new Transpose(A)
                )
            );

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            
            // Expected: Relu(A)
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Relu));
            var relu = (Relu)result.ResultExpression;
            Assert.AreEqual(A, relu.Arguments[0]);
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTransposeTensorMulCancellation()
        {
            // Verify: Transpose(TensorMul(Transpose(A), Transpose(B))) -> TensorMul(A, B)
            // This tests:
            // 1. Transpose(TensorMul(X, Y)) -> TensorMul(Transpose(X), Transpose(Y))
            // 2. Transpose(Transpose(A)) -> A
            // Result is TensorMul(A, B).
            // This is a cost-effective simplification (removes 3 Transpose ops).
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 20)));
            var B = new Symbol("B", new Shape(ImmutableArray.Create(10, 20)));
            
            // Transpose(A^T * B^T)
            var expr = new Transpose(
                new TensorMul(
                    new Transpose(A),
                    new Transpose(B)
                )
            );

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            
            // Expected: TensorMul(A, B)
            Assert.IsInstanceOfType(result.ResultExpression, typeof(TensorMul));
            var mul = (TensorMul)result.ResultExpression;
            
            // Arguments might be sorted
            bool hasA = mul.Arguments.Any(arg => arg.Equals(A));
            bool hasB = mul.Arguments.Any(arg => arg.Equals(B));
            Assert.IsTrue(hasA, "Should contain A");
            Assert.IsTrue(hasB, "Should contain B");
        }

        [TestMethod]
        [Timeout(10000)]
        public void TestTensorMulIdentity()
        {
            // Verify: Transpose(A * 1) -> Transpose(A)
            // This tests:
            // 1. TensorMul(A, 1) -> A.
            // 2. Transpose preservation.
            
            var A = new Symbol("A", new Shape(ImmutableArray.Create(10, 20)));
            var one = new Sym.Atoms.Number(1);
            
            // Transpose(A * 1)
            var expr = new Transpose(
                new TensorMul(A, one)
            );

            var context = new SolveContext(null, _rules)
                .WithAdditionalData(new Dictionary<string, object> { { SolverOptionKeys.CostModel, "Tensor" } });

            var result = _solver.Solve(expr, context);

            Assert.IsTrue(result.IsSuccess, "Solver should optimize the expression.");
            
            // Expected: Transpose(A)
            Assert.IsInstanceOfType(result.ResultExpression, typeof(Transpose));
            var trans = (Transpose)result.ResultExpression;
            Assert.AreEqual(A, trans.Arguments[0]);
        }
    }
}