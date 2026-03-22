// Copyright Warren Harding 2026
#nullable enable
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class SecurityFlowExtendedTests
    {
        private CSharpMathBugAnalyzer _analyzer = null!;
        private CSharpMathBugAnalyzerOptions _options = null!;

        [TestInitialize]
        public void Setup()
        {
            _analyzer = new CSharpMathBugAnalyzer();
            _options = CSharpMathBugAnalyzerOptions.Default with 
            { 
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0 // Ensure we catch everything for testing
            };
        }

        private CSharpMathBugFinding? AnalyzeAndGetFinding(string source, string bugId)
        {
            var result = _analyzer.AnalyzeText(source, _options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == bugId);
            if (finding == null && result.Diagnostics.Count > 0)
            {
                 // For debugging test failures
                 Console.WriteLine($"Diagnostics: {string.Join(Environment.NewLine, result.Diagnostics)}");
            }
            return finding;
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ConsoleReadLine_To_ProcessStart_Detected()
        {
            var source = @"
using System;
using System.Diagnostics;

public class Test {
    public void Run() {
        var input = Console.ReadLine();
        Process.Start(input);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC003");
            Assert.IsNotNull(finding, "Console.ReadLine -> Process.Start should be detected.");
            Assert.IsTrue(finding.Evidence.Any(e => e.Contains("Console.ReadLine")), "Evidence should mention Console.ReadLine.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_EnvironmentVariable_To_FileReadAllText_Detected()
        {
            var source = @"
using System;
using System.IO;

public class Test {
    public void Run() {
        var path = Environment.GetEnvironmentVariable(""PATH"");
        File.ReadAllText(path);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC004");
            Assert.IsNotNull(finding, "GetEnvironmentVariable -> File.ReadAllText should be detected.");
            Assert.IsTrue(finding.Evidence.Any(e => e.Contains("GetEnvironmentVariable")), "Evidence should mention GetEnvironmentVariable.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_FileStreamConstructor_Detected()
        {
            var source = @"
using System.IO;

public class Test {
    public void Run(string unsafePath) {
        var fs = new FileStream(unsafePath, FileMode.Open);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC004");
            Assert.IsNotNull(finding, "FileStream constructor sink should be detected.");
            Assert.IsTrue(finding.Evidence.Any(e => e.Contains("Source parameter")), "Evidence should mention source parameter.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_FileStreamConstructor_NamedArgsOutOfOrder_IsDetected()
        {
            var source = @"
using System.IO;

public class Test {
    public void Run(string unsafePath) {
        var fs = new FileStream(mode: FileMode.Open, path: unsafePath);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC004");
            Assert.IsNotNull(finding, "Named arguments out of order should not hide the FileStream path sink.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_DirectoryGetFiles_Detected()
        {
            var source = @"
using System.IO;

public class Test {
    public void Run(string unsafePath) {
        var files = Directory.GetFiles(unsafePath);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC004");
            Assert.IsNotNull(finding, "Directory.GetFiles sink should be detected.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_StringConcatenation_Propagation_Detected()
        {
            var source = @"
using System;
using System.Diagnostics;

public class Test {
    public void Run(string part1, string part2) {
        string cmd = ""cmd.exe /c "" + part1 + "" "" + part2;
        Process.Start(cmd);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC003");
            Assert.IsNotNull(finding, "String concatenation propagation should be detected.");
            // Evidence usually points to the first tainted source found in the expression tree or the last propagation step
            Assert.IsTrue(finding.Evidence.Any(e => e.Contains("Source parameter") || e.Contains("Propagated")), "Evidence should show propagation.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Sanitizer_PathGetFileName_SuppressesFinding()
        {
            var source = @"
using System.IO;

public class Test {
    public void Run(string input) {
        var safe = Path.GetFileName(input);
        File.ReadAllText(safe);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC004");
            Assert.IsNull(finding, "Path.GetFileName should sanitize the input.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_MockedQueryCollection_Item_Detected()
        {
            // We mock the interface to ensure the analyzer matches the type name
            var source = @"
using System;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Http {
    public interface IQueryCollection {
        string this[string key] { get; }
    }
    public interface IHttpContext {
        IQueryCollection Query { get; }
    }
}

public class Test {
    public void Run(Microsoft.AspNetCore.Http.IHttpContext context) {
        var val = context.Query[""id""];
        Process.Start(val);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC003");
            Assert.IsNotNull(finding, "IQueryCollection.Item should be detected as a source.");
            Assert.IsTrue(finding.Evidence.Any(e => e.Contains("Source: Property") || e.Contains("Query")), 
                $"Evidence mismatch. Actual: {string.Join(", ", finding?.Evidence ?? System.Array.Empty<string>())}");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_MockedFormCollection_Item_Detected()
        {
            var source = @"
using System;
using System.IO;

namespace Microsoft.AspNetCore.Http {
    public interface IFormCollection {
        string this[string key] { get; }
    }
}

public class Test {
    public void Run(Microsoft.AspNetCore.Http.IFormCollection form) {
        var val = form[""path""];
        File.ReadAllText(val);
    }
}";
            var finding = AnalyzeAndGetFinding(source, "CSSEC004");
            Assert.IsNotNull(finding, "IFormCollection.Item should be detected as a source.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_ImplicitConversion_Propagation()
        {
            // This might be tricky because we need `Wrapper` to be considered a source.
            // But `input` param is a source. 
            // If we use a param of type Wrapper?
            
            var source2 = @"
using System.Diagnostics;

public class Wrapper {
    public static implicit operator string(Wrapper w) => ""bad"";
}

public class Test {
    public void Run(Wrapper w) {
        Process.Start(w); // Implicit conversion to string here
    }
}";
            var finding = AnalyzeAndGetFinding(source2, "CSSEC003");
            Assert.IsNotNull(finding, "Implicit conversion from source parameter should be followed.");
        }
    }
}
