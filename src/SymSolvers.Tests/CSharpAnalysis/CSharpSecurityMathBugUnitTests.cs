#nullable enable
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class CSharpSecurityMathBugUnitTests
    {
        private CSharpMathBugAnalyzer _analyzer = new CSharpMathBugAnalyzer();

        private void AssertHasFinding(CSharpMathBugAnalysisResult result, string[] bugIds, string description)
        {
            if (!result.Findings.Any(f => bugIds.Contains(f.BugId)))
            {
                var diags = string.Join(Environment.NewLine, result.Diagnostics.Take(10));
                var findings = string.Join(", ", result.Findings.Select(f => f.BugId));
                Assert.Fail($"Should find one of [{string.Join(", ", bugIds)}] ({description}). Findings found: [{findings}]. Lowered: {result.LoweredExpressionCount}. Diagnostics: {diags}");
            }
        }

        private void AssertHasFinding(CSharpMathBugAnalysisResult result, string bugId, string description)
        {
            AssertHasFinding(result, new[] { bugId }, description);
        }

        private static CSharpMathBugFinding? GetFinding(CSharpMathBugAnalysisResult result, string bugId)
        {
            return result.Findings.FirstOrDefault(f => f.BugId == bugId);
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_AllocationOverflow_FindsBug()
        {
            var source = @"
using System;
public class Test {
    public void Run(int a, int b) {
        var arr = new byte[a * b];
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSMATH014", "Allocation Overflow");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_IndexOverflow_FindsBug()
        {
            var source = @"
using System;
public class Test {
    public void Run(int[] arr, int a, int b, int c) {
        var val = arr[a * b + c];
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, new[] { "CSMATH015", "CSMATH018" }, "Index Overflow");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NegativeIndexModulo_FindsBug()
        {
            var source = @"
using System;
public class Test {
    public void Run(int[] arr, int i, int n) {
        var val = arr[i % n];
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC011", "Negative Index Modulo");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_PrecisionLossTicks_FindsBug()
        {
            var source = @"
using System;
public class Test {
    public void Run() {
        long ticks = DateTime.Now.Ticks;
        float f = (float)ticks;
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC018", "Sensitive Precision Loss");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_WeakRNG_FindsBug()
        {
            var source = @"
using System;
public class Test {
    public void Run() {
        var rnd = new Random();
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC001", "Weak RNG");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ExpirationOverflow_FindsBug()
        {
            var source = @"
using System;
public class Test {
    public void Run(int days, int secondsPerDay) {
        var dt = DateTime.Now.AddSeconds(days * secondsPerDay);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC020", "Expiration Overflow");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_UnsignedUnderflow_FindsBug()
        {
            var source = @"
public class Test {
    public void Run(uint len, uint offset) {
        uint remaining = len - offset;
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC021", "Unsigned Underflow");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_SeekOverflow_FindsBug()
        {
            var source = @"
using System.IO;
public class Test {
    public void Run(Stream s, long offset, int count) {
        s.Seek(offset * count, SeekOrigin.Begin);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC023", "Seek Overflow");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_SignedToUnsignedWrapAfterSubtraction_FindsBug()
        {
            var source = @"
public class Test {
    public uint Run(int length, int consumed) {
        return (uint)(length - consumed);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC024", "Signed-to-unsigned wrap after subtraction");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NarrowingBoundaryConversion_FindsBug()
        {
            var source = @"
public class Test {
    public int Run(long payloadLength) {
        return unchecked((int)payloadLength);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC025", "Narrowing boundary conversion");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_BoundsArithmeticInRangeSink_FindsBug()
        {
            var source = @"
using System;
public class Test {
    public void Run(byte[] src, byte[] dst, int headerLen, int payloadLen) {
        Array.Copy(src, 0, dst, 0, headerLen + payloadLen);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC026", "Bounds arithmetic overflow in range sink");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_SignedUnsignedGuardBypass_FindsBug()
        {
            var source = @"
public class Test {
    public bool Run(int index) {
        return ((uint)index) < 0;
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC027", "Signed/unsigned guard bypass");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_FloatingBoundaryNarrowing_FindsBug()
        {
            var source = @"
public class Test {
    public int Run(double payloadLength) {
        return unchecked((int)payloadLength);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC028", "Floating boundary narrowing");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ShiftMaskAllocationHazard_FindsBug()
        {
            var source = @"
public class Test {
    public byte[] Run(int bits) {
        return new byte[1 << bits];
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC029", "Shift/mask allocation hazard");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NegativeModuloApiIndex_FindsBug()
        {
            var source = @"
public class Test {
    public string Run(string s, int i, int n) {
        return s.Substring(i % n, 1);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC030", "Negative-modulo index in API");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_BoundsArithmeticInRangeSink_ThroughLocal_FindsBug()
        {
            var source = @"
using System;
public class Test {
    public void Run(byte[] src, byte[] dst, int headerLen, int payloadLen) {
        int bytesToCopy = headerLen + payloadLen;
        Array.Copy(src, 0, dst, 0, bytesToCopy);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC026", "Bounds arithmetic overflow through local value");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_SignedUnsignedGuardBypass_ThroughLocalCast_FindsBug()
        {
            var source = @"
public class Test {
    public bool Run(int index) {
        uint normalized = (uint)index;
        return normalized >= 0;
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC027", "Signed/unsigned guard bypass through local cast");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_FloatingBoundaryNarrowing_Decimal_FindsBug()
        {
            var source = @"
public class Test {
    public int Run(decimal payloadLength) {
        return unchecked((int)payloadLength);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC028", "Decimal boundary narrowing");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ShiftMaskAllocationHazard_AddMaskPattern_FindsBug()
        {
            var source = @"
public class Test {
    public byte[] Run(int headerLen, int payloadLen, int mask) {
        return new byte[(headerLen + payloadLen) & mask];
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC029", "Mask-based allocation hazard");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ShiftMaskAllocationHazard_ThroughLocal_FindsBug()
        {
            var source = @"
public class Test {
    public byte[] Run(int bits) {
        int size = 1 << bits;
        return new byte[size];
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC029", "Shift allocation hazard through local value");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NegativeModuloApiIndex_SecondArgument_FindsBug()
        {
            var source = @"
public class Test {
    public string Run(string s, int i, int n) {
        return s.Substring(0, i % n);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC030", "Negative-modulo API index in second argument");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NegativeModuloApiIndex_ThroughLocal_FindsBug()
        {
            var source = @"
public class Test {
    public string Run(string s, int i, int n) {
        int start = i % n;
        return s.Substring(start, 1);
    }
}";
            var result = _analyzer.AnalyzeText(source);
            AssertHasFinding(result, "CSSEC030", "Negative-modulo API index through local value");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_CommandInjectionFlow_ProducesEvidence()
        {
            var source = @"
using System.Diagnostics;
public class Test {
    public void Run(string commandText) {
        var cmd = commandText;
        Process.Start(cmd);
    }
}";

            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                ConfidenceThreshold: 0,
                SecurityFlowMode: CSharpSecurityFlowMode.IntraProcedural));

            var finding = GetFinding(result, "CSSEC003");
            Assert.IsNotNull(finding, "Expected CSSEC003 from source/sink flow.");
            Assert.IsTrue(finding!.Evidence.Count > 0, "Flow findings should include evidence.");
            Assert.IsTrue(finding.Evidence.Any(e => e.Contains("source parameter", StringComparison.OrdinalIgnoreCase)),
                "Expected source evidence to mention parameter origin.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_CommandInjectionFlow_WithSanitizer_IsSuppressed()
        {
            var source = @"
using System.Diagnostics;
public class Test {
    private static string SanitizeInput(string value) => value.Trim();
    public void Run(string commandText) {
        var safe = SanitizeInput(commandText);
        Process.Start(safe);
    }
}";

            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                ConfidenceThreshold: 0,
                SecurityFlowMode: CSharpSecurityFlowMode.IntraProcedural));

            var finding = GetFinding(result, "CSSEC003");
            Assert.IsNull(finding, "Sanitized source should not trigger command-injection finding in flow mode.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_PathTraversalFlow_ProducesEvidence()
        {
            var source = @"
using System.IO;
public class Test {
    public void Run(string filePath) {
        var candidate = filePath;
        File.ReadAllText(candidate);
    }
}";

            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                ConfidenceThreshold: 0,
                SecurityFlowMode: CSharpSecurityFlowMode.IntraProcedural));

            var finding = GetFinding(result, "CSSEC004");
            Assert.IsNotNull(finding, "Expected CSSEC004 from source/sink flow.");
            Assert.IsTrue(finding!.Evidence.Count > 0, "Flow findings should include evidence.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_PathTraversal_IntraFlow_DoesNotFlagConstantPath()
        {
            var source = @"
using System.IO;
public class Test {
    public void Run() {
        File.ReadAllText(""C:\\temp\\config.txt"");
    }
}";

            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                ConfidenceThreshold: 0,
                SecurityFlowMode: CSharpSecurityFlowMode.IntraProcedural));

            var finding = GetFinding(result, "CSSEC004");
            Assert.IsNull(finding, "Constant path should not trigger flow-based CSSEC004.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_PathTraversal_SinkOnlyMode_UsesLegacySinkRules()
        {
            var source = @"
using System.IO;
public class Test {
    public void Run() {
        File.ReadAllText(""C:\\temp\\config.txt"");
    }
}";

            var result = _analyzer.AnalyzeText(source, new CSharpMathBugAnalyzerOptions(
                ConfidenceThreshold: 0,
                SecurityFlowMode: CSharpSecurityFlowMode.SinkOnly));

            var finding = GetFinding(result, "CSSEC004");
            Assert.IsNotNull(finding, "Sink-only mode should preserve legacy sink pattern detections.");
        }
    }
}
