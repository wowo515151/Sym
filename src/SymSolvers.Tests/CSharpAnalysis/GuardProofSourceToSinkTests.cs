using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class GuardProofSourceToSinkTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_CommandSink_WhenInputEqualsLiteral_IsSuppressed()
        {
            var source = @"
using System;
using System.Diagnostics;

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (input == ""whoami"") {
            Process.Start(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Command sink should be suppressed when the input is allowlisted to a literal in a positive branch.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_CommandSink_WhenStringEqualsLiteral_IsSuppressed()
        {
            var source = @"
using System;
using System.Diagnostics;

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (string.Equals(input, ""whoami"", StringComparison.OrdinalIgnoreCase)) {
            Process.Start(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Command sink should be suppressed when input is allowlisted via string.Equals(..., literal, comparison).");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_SqlSink_WhenInputEqualsLiteral_IsSuppressed()
        {
            var source = @"
using System;
using System.Data.SqlClient;

namespace System.Data.SqlClient {
    public class SqlCommand {
        public SqlCommand(string queryText) { }
    }
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (input == ""SELECT 1"") {
            new SqlCommand(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC031");
            Assert.IsNull(finding, "SQL sink should be suppressed when the query text is allowlisted to a literal in a positive branch.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_PathSink_WhenValidatedByGetFileNameEquality_IsSuppressed()
        {
            var source = @"
using System;
using System.IO;

class Test {
    public void Run() {
        var path = Console.ReadLine();
        if (Path.GetFileName(path) != path) return;
        File.ReadAllText(path);
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC004");
            Assert.IsNull(finding, "Path sink should be suppressed when validated by Path.GetFileName(path) == path idiom.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedCommandSink_IsSuppressed()
        {
            var source = @"
using System;
using System.Diagnostics;

static class Validators {
    public static bool IsAllowedCommand(string command) => command == ""whoami"";
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (Validators.IsAllowedCommand(input)) {
            Process.Start(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Command sink inside a positive allowlist guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedCommandSink_WhenGuardProverTimesOut_FailsClosedAndReportsDiagnostic()
        {
            var source = @"
using System;
using System.Diagnostics;

static class Validators {
    public static bool IsAllowedCommand(string command) => command == ""whoami"";
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (Validators.IsAllowedCommand(input)) {
            Process.Start(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0,
                GuardTimeoutSeconds = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "When the guard prover times out, suppression must fail closed and still report the sink.");
            Assert.IsTrue(
                result.Diagnostics.Any(d => d.Contains("Guard prover timed out", StringComparison.OrdinalIgnoreCase)),
                "Expected a guard prover timeout diagnostic.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_NegatedGuardBranch_IsNotSuppressed()
        {
            var source = @"
using System;
using System.Diagnostics;

static class Validators {
    public static bool IsAllowedCommand(string command) => command == ""whoami"";
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (!Validators.IsAllowedCommand(input)) {
            Process.Start(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNotNull(finding, "Negated guard branch should not suppress the risky sink.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardThenReturn_CarriesGuardToFollowingSink()
        {
            var source = @"
using System;
using System.Diagnostics;

static class Validators {
    public static bool IsAllowedCommand(string command) => command == ""whoami"";
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (!Validators.IsAllowedCommand(input)) return;
        Process.Start(input);
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Guard + early return should preserve guard fact for the continuation path.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_CallerGuard_SuppressesCallSiteFinding()
        {
            var files = new[]
            {
                (
@"
using System;

public static class EntryPoint {
    public static void Run() {
        var command = Console.ReadLine();
        if (Validators.IsAllowedCommand(command)) {
            SinkHelpers.Execute(command);
        }
    }
}",
                    "Entry.cs"
                ),
                (
@"
public static class Validators {
    public static bool IsAllowedCommand(string command) => command == ""whoami"";
}",
                    "Validators.cs"
                ),
                (
@"
using System.Diagnostics;

public static class SinkHelpers {
    public static void Execute(string value) {
        Process.Start(value);
    }
}",
                    "SinkHelpers.cs"
                )
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeProject(files, options);
            var callSiteFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC003" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("Entry.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNull(callSiteFinding, "Caller guard should suppress call-site sink propagation finding.");

            var calleeFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC003" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("SinkHelpers.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(calleeFinding, "Callee entry-point vulnerability remains visible when method itself is public.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_CallerGuard_WithNamedArgsOutOfOrder_SuppressesCallSiteFinding()
        {
            var files = new[]
            {
                (
@"
using System;

public static class EntryPoint {
    public static void Run() {
        var command = Console.ReadLine();
        if (Validators.IsAllowedCommand(command)) {
            SinkHelpers.Execute(dummy: 0, value: command);
        }
    }
}",
                    "Entry.cs"
                ),
                (
@"
public static class Validators {
    public static bool IsAllowedCommand(string command) => command == ""whoami"";
}",
                    "Validators.cs"
                ),
                (
@"
using System.Diagnostics;

public static class SinkHelpers {
    public static void Execute(string value, int dummy = 0) {
        Process.Start(value);
    }
}",
                    "SinkHelpers.cs"
                )
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeProject(files, options);
            var callSiteFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC003" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("Entry.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNull(callSiteFinding, "Caller guard should suppress call-site finding even with named args out of order.");

            var calleeFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC003" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("SinkHelpers.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(calleeFinding, "Callee entry-point vulnerability remains visible when method itself is public.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_CalleeInternalGuard_SuppressesFinding()
        {
            var source = @"
using System;
using System.Diagnostics;

public static class Validators {
    public static bool IsAllowedCommand(string command) => command == ""whoami"";
}

public class SinkHelpers {
    public void Execute(string value) {
        if (Validators.IsAllowedCommand(value)) {
            Process.Start(value);
        }
    }
}

public class EntryPoint {
    public void Run() {
        var command = Console.ReadLine();
        new SinkHelpers().Execute(command);
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC003");
            Assert.IsNull(finding, "Internal callee guard should suppress propagation and summary sink findings.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_CrossFile_PathGuard_SuppressesCallSiteFinding()
        {
            var files = new[]
            {
                (
@"
using System;

public static class EntryPoint {
    public static void Run() {
        var path = Console.ReadLine();
        if (Validators.IsSafePath(path)) {
            FileOps.Load(path);
        }
    }
}",
                    "Entry.cs"
                ),
                (
@"
public static class Validators {
    public static bool IsSafePath(string path) => path.StartsWith(""safe/"", System.StringComparison.Ordinal);
}",
                    "Validators.cs"
                ),
                (
@"
using System.IO;

public static class FileOps {
    public static string Load(string path) {
        return File.ReadAllText(path);
    }
}",
                    "FileOps.cs"
                )
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIde,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeProject(files, options);
            var callSiteFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC004" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("Entry.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNull(callSiteFinding, "Cross-file caller guard should suppress path-traversal call-site propagation finding.");

            var calleeFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC004" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("FileOps.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(calleeFinding, "Callee entry-point finding remains for direct unguarded public sink.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedSqlSink_IsSuppressed()
        {
            var source = @"
using System;
using System.Data.SqlClient;

namespace System.Data.SqlClient {
    public class SqlCommand {
        public SqlCommand(string queryText) { }
    }
}

static class Validators {
    public static bool IsSafeSql(string sql) => true;
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (Validators.IsSafeSql(input)) {
            new SqlCommand(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC031");
            Assert.IsNull(finding, "SQL sink inside a positive allowlist guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedLdapSink_IsSuppressed()
        {
            var source = @"
using System;
using System.DirectoryServices;

namespace System.DirectoryServices {
    public class DirectorySearcher {
        public DirectorySearcher(string filter) { }
    }
}

static class Validators {
    public static bool IsSafeLdap(string filter) => true;
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (Validators.IsSafeLdap(input)) {
            new DirectorySearcher(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC032");
            Assert.IsNull(finding, "LDAP sink inside a positive allowlist guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedXPathSink_IsSuppressed()
        {
            var source = @"
using System;
using System.Xml.XPath;

namespace System.Xml.XPath {
    public static class XPathExpression {
        public static void Compile(string xpath) { }
    }
}

static class Validators {
    public static bool IsSafeXPath(string xpath) => true;
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (Validators.IsSafeXPath(input)) {
            XPathExpression.Compile(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC033");
            Assert.IsNull(finding, "XPath sink inside a positive allowlist guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedRedirectSink_IsSuppressed()
        {
            var source = @"
using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http {
    public class HttpResponse {
        public void Redirect(string url) { }
    }
}

static class Validators {
    public static bool IsSafeRedirect(string url) => true;
}

class Test {
    public void Run(HttpResponse response) {
        var input = Console.ReadLine();
        if (Validators.IsSafeRedirect(input)) {
            response.Redirect(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC034");
            Assert.IsNull(finding, "Redirect sink inside a positive allowlist guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedHeaderSink_IsSuppressed()
        {
            var source = @"
using System;
using System.Web;

namespace System.Web {
    public class HttpResponse {
        public void AddHeader(string name, string value) { }
    }
}

static class Validators {
    public static bool IsValidHeaderName(string name) => true;
}

class Test {
    public void Run(HttpResponse response) {
        var input = Console.ReadLine();
        if (Validators.IsValidHeaderName(input)) {
            response.AddHeader(input, ""value"");
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC035");
            Assert.IsNull(finding, "Header sink inside a positive allowlist guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedTemplateSink_IsSuppressed()
        {
            var source = @"
using System;
using Scriban;

namespace Scriban {
    public static class Template {
        public static void Parse(string text) { }
    }
}

static class Validators {
    public static bool IsTrustedTemplate(string template) => true;
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (Validators.IsTrustedTemplate(input)) {
            Template.Parse(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC036");
            Assert.IsNull(finding, "Template sink inside a positive allowlist guard should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_NegatedSqlGuardBranch_IsNotSuppressed()
        {
            var source = @"
using System;
using System.Data.SqlClient;

namespace System.Data.SqlClient {
    public class SqlCommand {
        public SqlCommand(string queryText) { }
    }
}

static class Validators {
    public static bool IsSafeSql(string sql) => true;
}

class Test {
    public void Run() {
        var input = Console.ReadLine();
        if (!Validators.IsSafeSql(input)) {
            new SqlCommand(input);
        }
    }
}";

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC031");
            Assert.IsNotNull(finding, "Negated SQL guard branch should not suppress the risky sink.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Interprocedural_CallerSqlGuard_SuppressesCallSiteFinding()
        {
            var files = new[]
            {
                (
@"
using System;

public static class EntryPoint {
    public static void Run() {
        var command = Console.ReadLine();
        if (Validators.IsSafeSql(command)) {
            SinkHelpers.ExecuteQuery(command);
        }
    }
}",
                    "Entry.cs"
                ),
                (
@"
public static class Validators {
    public static bool IsSafeSql(string sql) => true;
}",
                    "Validators.cs"
                ),
                (
@"
using System.Data.SqlClient;

namespace System.Data.SqlClient {
    public class SqlCommand {
        public SqlCommand(string queryText) { }
    }
}

public static class SinkHelpers {
    public static void ExecuteQuery(string value) {
        new SqlCommand(value);
    }
}",
                    "SinkHelpers.cs"
                )
            };

            var analyzer = new CSharpMathBugAnalyzer();
            var options = CSharpMathBugAnalyzerOptions.Default with
            {
                SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
                ConfidenceThreshold = 0.0
            };

            var result = analyzer.AnalyzeProject(files, options);
            var callSiteFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC031" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("Entry.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNull(callSiteFinding, "Caller guard should suppress call-site sink propagation finding for SQL.");

            var calleeFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC031" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("SinkHelpers.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(calleeFinding, "Callee entry-point vulnerability remains visible when method itself is public.");
        }
    }
}
