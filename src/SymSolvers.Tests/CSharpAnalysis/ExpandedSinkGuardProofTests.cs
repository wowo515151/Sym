// Copyright Warren Harding 2026
#nullable enable
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class ExpandedSinkGuardProofTests
    {
        private readonly CSharpMathBugAnalyzer _analyzer = new();

        private static CSharpMathBugAnalyzerOptions IntraOptions => CSharpMathBugAnalyzerOptions.Default with
        {
            SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
            ConfidenceThreshold = 0.0
        };

        private static CSharpMathBugAnalyzerOptions InterOptions => CSharpMathBugAnalyzerOptions.Default with
        {
            SecurityFlowMode = CSharpSecurityFlowMode.InterproceduralIfds,
            ConfidenceThreshold = 0.0
        };

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedSqlSink_IsSuppressed()
        {
            var source = @"
using System;
namespace System.Data.SqlClient { public class SqlCommand { public SqlCommand(string cmd) {} } }
static class Validators { public static bool ValidateSql(string sql) => sql.Length > 0; }
class P {
    void M() {
        var q = Console.ReadLine();
        if (Validators.ValidateSql(q)) {
            new System.Data.SqlClient.SqlCommand(q);
        }
    }
}";

            var result = _analyzer.AnalyzeText(source, IntraOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC031");
            Assert.IsNull(finding, "Guarded SQL sink should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_NegatedSqlGuard_IsNotSuppressed()
        {
            var source = @"
using System;
namespace System.Data.SqlClient { public class SqlCommand { public SqlCommand(string cmd) {} } }
static class Validators { public static bool ValidateSql(string sql) => sql.Length > 0; }
class P {
    void M() {
        var q = Console.ReadLine();
        if (!Validators.ValidateSql(q)) {
            new System.Data.SqlClient.SqlCommand(q);
        }
    }
}";

            var result = _analyzer.AnalyzeText(source, IntraOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC031");
            Assert.IsNotNull(finding, "Negated guard branch must not suppress SQL sink.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedLdapSink_IsSuppressed()
        {
            var source = @"
using System;
namespace System.DirectoryServices { public class DirectorySearcher { public DirectorySearcher(string filter) {} } }
static class Validators { public static bool ValidateLdapFilter(string filter) => filter.Length > 0; }
class P {
    void M() {
        var f = Console.ReadLine();
        if (Validators.ValidateLdapFilter(f)) {
            new System.DirectoryServices.DirectorySearcher(f);
        }
    }
}";

            var result = _analyzer.AnalyzeText(source, IntraOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC032");
            Assert.IsNull(finding, "Guarded LDAP sink should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedXPathSink_IsSuppressed()
        {
            var source = @"
using System;
namespace System.Xml.XPath { public class XPathExpression { public static void Compile(string x) {} } }
static class Validators { public static bool IsSafeXPath(string x) => x.Length > 0; }
class P {
    void M() {
        var x = Console.ReadLine();
        if (Validators.IsSafeXPath(x)) {
            System.Xml.XPath.XPathExpression.Compile(x);
        }
    }
}";

            var result = _analyzer.AnalyzeText(source, IntraOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC033");
            Assert.IsNull(finding, "Guarded XPath sink should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedRedirectSink_IsSuppressed()
        {
            var source = @"
using System;
namespace Microsoft.AspNetCore.Http { public class HttpResponse { public void Redirect(string url) {} } }
static class Validators { public static bool IsLocalUrl(string url) => url.StartsWith(""/"", StringComparison.Ordinal); }
class P {
    void M(Microsoft.AspNetCore.Http.HttpResponse response) {
        var url = Console.ReadLine();
        if (Validators.IsLocalUrl(url)) {
            response.Redirect(url);
        }
    }
}";

            var result = _analyzer.AnalyzeText(source, IntraOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC034");
            Assert.IsNull(finding, "Guarded redirect sink should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedHeaderSink_IsSuppressed()
        {
            var source = @"
using System;
namespace System.Web { public class HttpResponse { public void AddHeader(string name, string value) {} } }
static class Validators { public static bool ValidateHeader(string name, string value) => name.Length > 0 && value.Length > 0; }
class P {
    void M(System.Web.HttpResponse response) {
        var value = Console.ReadLine();
        if (Validators.ValidateHeader(""X-Test"", value)) {
            response.AddHeader(""X-Test"", value);
        }
    }
}";

            var result = _analyzer.AnalyzeText(source, IntraOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC035");
            Assert.IsNull(finding, "Guarded header sink should be suppressed.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedTemplateSink_IsSuppressed()
        {
            var source = @"
using System;
namespace Scriban { public class Template { public static void Parse(string template) {} } }
static class Validators { public static bool IsTrustedTemplate(string template) => template.StartsWith(""SAFE:"", StringComparison.Ordinal); }
class P {
    void M() {
        var template = Console.ReadLine();
        if (Validators.IsTrustedTemplate(template)) {
            Scriban.Template.Parse(template);
        }
    }
}";

            var result = _analyzer.AnalyzeText(source, IntraOptions);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC036");
            Assert.IsNull(finding, "Guarded template sink should be suppressed.");
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
        var query = Console.ReadLine();
        if (Validators.ValidateSql(query)) {
            SinkHelpers.Execute(query);
        }
    }
}",
                    "Entry.cs"
                ),
                (
@"
public static class Validators {
    public static bool ValidateSql(string sql) => sql.Length > 0;
}",
                    "Validators.cs"
                ),
                (
@"
namespace System.Data.SqlClient { public class SqlCommand { public SqlCommand(string cmd) {} } }
public static class SinkHelpers {
    public static void Execute(string query) {
        new System.Data.SqlClient.SqlCommand(query);
    }
}",
                    "SinkHelpers.cs"
                )
            };

            var result = _analyzer.AnalyzeProject(files, InterOptions);
            var callSiteFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC031" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("Entry.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNull(callSiteFinding, "Caller guard should suppress call-site SQL finding.");

            var calleeFinding = result.Findings.FirstOrDefault(f =>
                f.BugId == "CSSEC031" &&
                f.SourceSpan is not null &&
                f.SourceSpan.FilePath.Contains("SinkHelpers.cs", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(calleeFinding, "Callee entry-point SQL finding should remain visible for the public sink method.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_GuardedSqlSink_WhenGuardProverTimesOut_FailsClosed()
        {
            var source = @"
using System;
namespace System.Data.SqlClient { public class SqlCommand { public SqlCommand(string cmd) {} } }
static class Validators { public static bool ValidateSql(string sql) => sql.Length > 0; }
class P {
    void M() {
        var q = Console.ReadLine();
        if (Validators.ValidateSql(q)) {
            new System.Data.SqlClient.SqlCommand(q);
        }
    }
}";

            var options = IntraOptions with { GuardTimeoutSeconds = 0.0 };
            var result = _analyzer.AnalyzeText(source, options);
            var finding = result.Findings.FirstOrDefault(f => f.BugId == "CSSEC031");
            Assert.IsNotNull(finding, "Guard prover timeout must fail closed and keep SQL sink finding.");
            Assert.IsTrue(
                result.Diagnostics.Any(d => d.Contains("Guard prover timed out", StringComparison.OrdinalIgnoreCase)),
                "Expected guard-timeout diagnostic for failed-closed suppression.");
        }
    }
}
