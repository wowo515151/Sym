//Copyright Warren Harding 2025.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Sym.Atoms;
using Sym.Core;
using Sym.Operations;
using SymSolvers.Numerics;
using SymSolvers.Stability;

namespace SymSolvers.Tests
{
    [TestClass]
    public class NumericalStabilityTests
    {
        [TestMethod]
        [Timeout(30000)]
        public void TestPrecisionEvaluator_CancellationError()
        {
            // (1 + x) - 1  where x is small
            var x = new Symbol("x");
            var one = new Number(1);
            var expr = new Subtract(new Add(one, x), one);

            var assignments = new Dictionary<string, double> { ["x"] = 1e-10 };
            
            // FP64 should handle 1e-10 fine (epsilon is ~2e-16)
            var fp64 = new Float64Model();
            Assert.IsTrue(PrecisionExpressionEvaluator.TryEvaluate(expr, assignments, fp64, out double val64, out _));
            Assert.AreEqual(1e-10, val64, 1e-17);

            // FP16 has much larger epsilon (~1e-3)
            var fp16 = new Float16Model();
            Assert.IsTrue(PrecisionExpressionEvaluator.TryEvaluate(expr, assignments, fp16, out double val16, out _));
            // (1 + 1e-10) in FP16 is exactly 1.0 because 1e-10 < 1e-3 * 1.0
            Assert.AreEqual(0.0, val16, 1e-17);
        }

        [TestMethod]
        [Timeout(30000)]
        public void TestStabilityScorer_DetectsInstability()
        {
            // x / x  should be 1, but if x is very small it might be NaN or unstable
            var x = new Symbol("x");
            var expr = new Divide(x, x);
            
            var models = new IFloatingPointModel[] { new Float64Model(), new Float32Model(), new Float16Model() };
            var scorer = new StabilityScorer(models, sampleCount: 20);
            
            var result = scorer.Score(expr);
            
            Assert.IsNotNull(result);
            foreach (var m in result.Metrics)
            {
                // x/x is generally stable as long as x != 0
                Assert.IsTrue(m.MaxAbsErrorVsFp64 < 1e-5, $"Model {m.Model} should be stable for x/x");
            }
        }
        
        [TestMethod]
        [Timeout(30000)]
        public void TestPrecisionEvaluator_Functions()
        {
            var x = new Symbol("x");
            var sinX = new Function("sin", ImmutableList.Create<IExpression>(x));
            
            var fp64 = new Float64Model();
            var assignments = new Dictionary<string, double> { ["x"] = Math.PI / 2 };
            
            Assert.IsTrue(PrecisionExpressionEvaluator.TryEvaluate(sinX, assignments, fp64, out double val, out _));
            Assert.AreEqual(1.0, val, 1e-10);
        }
        
        [TestMethod]
        [Timeout(30000)]
        public void TestLogSumExp_Stability()
        {
            // log(exp(x) + exp(x)) = log(2*exp(x)) = x + log(2)
            var x = new Symbol("x");
            var lse = new Function("logsumexp", ImmutableList.Create<IExpression>(x, x));
            
            var fp64 = new Float64Model();
            var assignments = new Dictionary<string, double> { ["x"] = 100.0 };
            
            // Standard exp(100) would overflow if not using LSE trick
            Assert.IsTrue(PrecisionExpressionEvaluator.TryEvaluate(lse, assignments, fp64, out double val, out _));
            Assert.AreEqual(100.0 + Math.Log(2), val, 1e-10);
        }
    }
}
