using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class SecurityFalsePositivesTests
    {
        [TestMethod]
        [Timeout(30000)]
        public void Analyze_Modulo_WithBitwiseMask_IsSuppressed()
        {
            var source = @"
class Test {
    public int GetBucket(int hashCode, int[] buckets) {
        int masked = hashCode & 0x7FFFFFFF;
        return buckets[masked % buckets.Length];
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Modulo with 0x7FFFFFFF mask should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_CircularBuffer_Modulo_WithAssertIndex_IsSuppressed()
        {
            var source = @"
class Ring {
    private readonly int[] _buffer = new int[8];
    private int _head = 0;
    private int _maxSize;

    public Ring() { _maxSize = _buffer.Length; }

    private void AssertIndex(int index) {
        if (index < 0) throw new System.ArgumentOutOfRangeException(nameof(index));
        if (index >= _maxSize) throw new System.ArgumentOutOfRangeException(nameof(index));
    }

    public int Get(int index) {
        AssertIndex(index);
        return _buffer[(_head + index) % _maxSize];
    }

    public void Advance() {
        _head = (_head + 1) % _maxSize;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Circular-buffer modulo index guarded by AssertIndex should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_CircularBuffer_CopyLoop_FromZero_IsSuppressed()
        {
            var source = @"
class RingCopy {
    private int[] _items = new int[4];
    private int _head = 0;

    public int[] Copy(int count) {
        var dst = new int[count];
        for (int i = 0; i < count; i++) {
            dst[i] = _items[(_head + i) % _items.Length];
        }
        return dst;
    }

    public void Advance() {
        _head = (_head + 1) % _items.Length;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Circular-buffer copy loop with i starting at 0 should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_CircularBuffer_Modulo_WithNonNegativePropertyCounter_IsSuppressed_IntraMethod()
        {
            var source = @"
class Ring {
    private readonly int[] _items = new int[8];
    private int _head = 0;
    private int _count = 0;
    public int ItemCount => _count;

    public int GetTail() {
        if (_count == 0) throw new System.InvalidOperationException();
        _count--;
        int tail = (_head + ItemCount) % _items.Length;
        return _items[tail];
    }

    public void Enqueue() {
        _count++;
        _head = (_head + 1) % _items.Length;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Modulo dividend using a proven non-negative property counter should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_CircularBuffer_Modulo_WithNonNegativePropertyCounter_IsSuppressed_CrossMethod()
        {
            var source = @"
class Ring {
    private readonly int[] _items = new int[8];
    private int _head = 0;
    private int _count = 0;
    public int ItemCount { get { return _count; } }

    public void Enqueue() {
        _count++;
        _head = (_head + 1) % _items.Length;
    }

    public int PeekTail() {
        return _items[(_head + ItemCount) % _items.Length];
    }

    public void Dequeue() {
        if (_count == 0) throw new System.InvalidOperationException();
        _count--;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Modulo dividend using a property-backed non-negative counter should be suppressed across methods.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_CircularBuffer_Modulo_WithNonNegativePropertyCounter_IsSuppressed_CrossFile()
        {
            var files = new[]
            {
                (
@"
public partial class Ring {
    private readonly int[] _items = new int[8];
    private int _head = 0;
    private int _count = 0;
    public int ItemCount => _count;
}", "Ring.Part1.cs"),
                (
@"
public partial class Ring {
    public void Enqueue() {
        _count++;
        _head = (_head + 1) % _items.Length;
    }

    public void Dequeue() {
        if (_count == 0) throw new System.InvalidOperationException();
        _count--;
    }

    public int PeekTail() {
        return _items[(_head + ItemCount) % _items.Length];
    }
}", "Ring.Part2.cs")
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0,
                AnalysisTimeoutSeconds = 20,
                SaturationTimeoutSeconds = 8
            };

            var result = analyzer.AnalyzeProject(files, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Cross-file modulo dividend using property-backed non-negative counter should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_StaticMutableState_AssignmentInStaticCtor_IsSuppressed()
        {
            var source = @"
class P {
    static int x;
    static P() {
        x = 1;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC009");
            Assert.IsNull(finding, "Static assignments in a static constructor should be suppressed (type init is single-threaded)." );
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_StaticMutableState_StaticFieldInitializer_IsSuppressed()
        {
            var source = @"
class P {
    static int x = 1;
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC009");
            Assert.IsNull(finding, "Static field initializers run during type init and should be suppressed for CSSEC009.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_UnsignedSub_WithComparisonGuard_IsSuppressed()
        {
            var source = @"
class Test {
    public uint SafeSub(uint a, uint b) {
        if (a >= b) {
            return a - b;
        }
        return 0;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "Unsigned subtraction guarded by a >= b should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_Interprocedural_GuardedModulo_IsSuppressed()
        {
            var source = @"
public class Utf8Lookup {
    private int[] _buckets = new int[7];
    public int GetValue(int hashCode) {
        int index = hashCode & 0x7FFFFFFF;
        return InternalLookup(index);
    }
    private int InternalLookup(int maskedHashCode) {
        return _buckets[maskedHashCode % _buckets.Length];
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Interprocedural guarded modulo should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_CrossFile_GuardedUnsignedSub_IsSuppressed()
        {
            var files = new[]
            {
                (
@"
public static class Guard {
    public static void Run() {
        uint max = 100;
        uint current = 50;
        if (max >= current) {
            Helper.Process(max, current);
        }
    }
}", "Guard.cs"),
                (
@"
public static class Helper {
    public static uint Process(uint a, uint b) {
        return a - b;
    }
}", "Helper.cs")
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIde,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeProject(files, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "Cross-file guarded unsigned subtraction should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ModuloIndex_WithLocalAliasOfImplicitZeroInitField_IsSuppressed_IntraMethod()
        {
            var source = @"
class Ring {
    private readonly int[] _buffer = new int[4];
    private int _index; // implicit default(0)

    public int Next() {
        int i = _index;
        _index = (i + 1) % _buffer.Length;
        return _buffer[(i + 1) % _buffer.Length];
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Ring-buffer modulo index using a local alias of an implicitly-zero-initialized field should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ModuloIndex_WithLocalAliasFieldUpdateAcrossMethods_IsSuppressed()
        {
            var source = @"
class Ring {
    private readonly int[] _buffer = new int[4];
    private int _index; // implicit default(0)

    public void Advance() {
        int i = _index;
        _index = (i + 1) % _buffer.Length;
    }

    public int PeekNext() {
        int i = _index;
        return _buffer[(i + 1) % _buffer.Length];
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Ring-buffer modulo index should be suppressed even when the field update happens in a different method.");
        }

        [TestMethod]
        [Timeout(20000)]
        public void Analyze_ModuloIndex_WithLocalAliasFieldUpdateAcrossFiles_IsSuppressed()
        {
            var files = new[]
            {
                (
@"
public partial class Ring {
    private readonly int[] _buffer = new int[4];
    private int _index; // implicit default(0)

    public void Advance() {
        int i = _index;
        _index = (i + 1) % _buffer.Length;
    }
}", "Ring.Part1.cs"),
                (
@"
public partial class Ring {
    public int PeekNext() {
        int i = _index;
        return _buffer[(i + 1) % _buffer.Length];
    }
}", "Ring.Part2.cs")
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIde,
                ConfidenceThreshold = 0.0,
                AnalysisTimeoutSeconds = 20,
            };

            var result = analyzer.AnalyzeProject(files, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Ring-buffer modulo index should be suppressed across files when the field is inductively non-negative and aliased via a local.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_UintModulo_IsSuppressed_InherentNonNegative()
        {
            var source = @"
class Test {
    public uint GetBucket(uint hashCode, int[] buckets) {
        return (uint)buckets[hashCode % (uint)buckets.Length];
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Uint modulo should be suppressed due to inherent non-negativity.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_Interprocedural_UintModulo_IsSuppressed()
        {
            var source = @"
public class Utf8Lookup {
    private int[] _buckets = new int[7];
    public int GetValue(uint hashCode) {
        return InternalLookup(hashCode);
    }
    private int InternalLookup(uint maskedHashCode) {
        return _buckets[maskedHashCode % (uint)_buckets.Length];
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC011");
            Assert.IsNull(finding, "Interprocedural uint modulo should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_Popcount_IsSuppressed()
        {
            var source = @"
class Test {
    public ulong CountBits(ulong v) {
        v = v - ((v >> 1) & 0x5555555555555555UL);
        return v;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "Popcount bit manipulation should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_NarrowingCast_WithGuard_IsSuppressed()
        {
            var source = @"
class Test {
    public int SafeCast(long size) {
        if (size < 2147483647) {
            return (int)size;
        }
        return 0;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC025");
            Assert.IsNull(finding, "Narrowing cast with < int.MaxValue guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NarrowingCast_WithMathMinClampAndLength_IsSuppressed()
        {
            var source = @"
using System;
class Test {
    public int SafeCast(string s) {
        return (int)Math.Min((long)s.Length, int.MaxValue);
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC025");
            Assert.IsNull(finding, "Narrowing cast guarded by Math.Min(x, int.MaxValue) with non-negative Length should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NarrowingCast_WithMathMinClampAcrossMethods_IsSuppressed()
        {
            var source = @"
using System;
static class Helper {
    public static long GetLen(string s) => s.Length;
}
class Test {
    public int SafeCast(string s) {
        return (int)Math.Min(Helper.GetLen(s), int.MaxValue);
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC025");
            Assert.IsNull(finding, "Narrowing cast guarded by Math.Min(x, int.MaxValue) should be suppressed when x is returned from an inductively non-negative helper.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NarrowingCast_WithMathClampToIntMax_IsSuppressed()
        {
            var source = @"
using System;
class Test {
    public int SafeCast(long size) {
        return (int)Math.Clamp(size, 0, int.MaxValue);
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC025");
            Assert.IsNull(finding, "Narrowing cast guarded by Math.Clamp(x, 0, int.MaxValue) should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NarrowingCast_FromMemoryStreamLength_IsSuppressed_IntraMethod()
        {
            var source = @"
using System.IO;
class Test {
    public int SafeLen(byte[] data) {
        var ms = new MemoryStream(data);
        return (int)ms.Length;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC025");
            Assert.IsNull(finding, "Narrowing cast from MemoryStream.Length should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_NarrowingCast_FromStreamLength_IsNotSuppressed()
        {
            var source = @"
using System.IO;
class Test {
    public int UnsafeLen(Stream s) {
        return (int)s.Length;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC025");
            Assert.IsNotNull(finding, "Narrowing cast from Stream.Length should still be reported.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_CrossFile_NarrowingCast_WithMathMinClampAcrossMethods_IsSuppressed()
        {
            var files = new[]
            {
                (
@"
using System;
public static class Helper {
    public static long GetLen(string s) => s.Length;
}", "Helper.cs"),
                (
@"
using System;
public class Test {
    public int SafeCast(string s) {
        return (int)Math.Min(Helper.GetLen(s), int.MaxValue);
    }
}", "Test.cs")
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIde,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeProject(files, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC025");
            Assert.IsNull(finding, "Cross-file narrowing cast guarded by Math.Min(x, int.MaxValue) should be suppressed when x is returned from an inductively non-negative helper.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_CrossFile_NarrowingCast_FromMemoryStreamLength_IsSuppressed()
        {
            var files = new[]
            {
                (
@"
using System.IO;
public static class Helper {
    public static int Len(byte[] data) {
        var ms = new MemoryStream(data);
        return (int)ms.Length;
    }
}", "Helper.cs"),
                (
@"
public class Test {
    public int Call(byte[] data) => Helper.Len(data);
}", "Test.cs")
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIde,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeProject(files, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC025");
            Assert.IsNull(finding, "Cross-file narrowing cast from MemoryStream.Length should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_UnsignedSub_InElseBranch_IsSuppressed()
        {
            var source = @"
class Test {
    public uint SafeSub(uint temp) {
        if (temp < 10) {
            return 0;
        } else {
            return temp - 10;
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "Unsigned subtraction in else-branch of < guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_RangeCheckIdiom_IsSuppressed()
        {
            var source = @"
class Test {
    public string GetReason(int statusCode) {
        if ((uint)(statusCode - 100) < 500) {
            return ""Known"";
        }
        return """";
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var findingSub = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            var findingWrap = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC024");
            
            Assert.IsNull(findingSub, "Range-check idiom subtraction (CSSEC021) should be suppressed.");
            Assert.IsNull(findingWrap, "Range-check idiom wrap (CSSEC024) should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_ModuloBoundary_IsSuppressed()
        {
            var source = @"
class Test {
    public uint GetPadding(uint num) {
        return 16 - (num % 16);
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "Modulo boundary subtraction (C - x%C) should be suppressed.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_NullCheck_ConstantTime_IsSuppressed()
        {
            var source = @"
class Test {
    public void Check(string key) {
        if (key == null) return;
        if (key != null) {
             // ...
        }
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC008");
            Assert.IsNull(finding, "Null checks should not trigger insecure comparison (CSSEC008) warnings.");
        }

        [TestMethod]
        [Timeout(30000)]
        public void Analyze_DebugAssertGuard_IsSuppressed()
        {
            var source = @"
using System.Diagnostics;
class Test {
    public uint SafeSub(uint a, uint b) {
        Debug.Assert(a >= b);
        return a - b;
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "Subtraction following Debug.Assert(a >= b) should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_RangeCheckIdiom_TwoSided_IsSuppressed()
        {
            var source = @"
using System.Diagnostics;
class Test {
    private static bool InRange(int value, int start, int end) {
        Debug.Assert(start <= end);
        return (uint)(value - start) <= (uint)(end - start);
    }

    public bool Run(int x) {
        return InRange(x, 10, 20);
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                ConfidenceThreshold = 0.0,
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural
            };

            var result = analyzer.AnalyzeText(source, options);
            Assert.IsNull(result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021"), "Two-sided range-check subtraction should be suppressed.");
            Assert.IsNull(result.Findings.FirstOrDefault(f => f.BugId == "CSSEC024"), "Two-sided range-check cast wrap should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Ucs4Shift16Sub1_UnderGuard_IsSuppressed()
        {
            var source = @"
class Test {
    public int Run(uint code) {
        if (code > 0x10FFFF) return -1;
        if (code > 0xFFFF) {
            uint hi = (code >> 16) - 1;
            return (int)hi;
        }
        return 0;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                ConfidenceThreshold = 0.0,
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "(code >> 16) - 1 under code > 0xFFFF should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_UnsignedBitTwiddle_Sub1_IsSuppressed()
        {
            var source = @"
class Test {
    public bool ExactlyOne(uint num) {
        return num != 0 && (num & (num - 1)) == 0;
    }

    public int LeastPosition(uint num) {
        if (num == 0) return 0;
        uint diff = num ^ (num - 1);
        return (int)diff;
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                ConfidenceThreshold = 0.0,
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "Unsigned bit-twiddle patterns x&(x-1)/x^(x-1) should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_CrossFile_Ucs4Shift16Sub1_UnderGuard_IsSuppressed()
        {
            var files = new[]
            {
                (
@"
public static class Caller {
    public static int Run(uint code) {
        if (code > 0x10FFFF) return -1;
        if (code > 0xFFFF) {
            return Helper.GetHi(code);
        }
        return 0;
    }
}", "Caller.cs"),
                (
@"
public static class Helper {
    public static int GetHi(uint code) {
        uint hi = (code >> 16) - 1;
        return (int)hi;
    }
}", "Helper.cs")
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                ConfidenceThreshold = 0.0,
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIde,
                AnalysisTimeoutSeconds = 20,
                SaturationTimeoutSeconds = 8
            };

            var result = analyzer.AnalyzeProject(files, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC021");
            Assert.IsNull(finding, "Cross-file (code>>16)-1 guarded by caller code>0xFFFF should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ShiftSizeAllocation_WithMaskGuard_IsSuppressed()
        {
            var source = @"
class Test {
    public byte[] Run(int size) {
        int shift = size & 31;
        return new byte[1 << shift];
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC029");
            Assert.IsNull(finding, "Shift allocation size guarded by bitwise AND should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_BoundsArithmetic_WithMathMaxGuard_IsSuppressed()
        {
            var source = @"
using System;
class Test {
    public string Run(string s, int start, int end) {
        int len = Math.Max(0, end - start);
        return s.Substring(start, len);
    }
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with { ConfidenceThreshold = 0.0 };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC026");
            Assert.IsNull(finding, "Bounds arithmetic guarded by Math.Max should be suppressed.");
        }
    }
}
