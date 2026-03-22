// Copyright Warren Harding 2026
#nullable enable
using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class SinkComprehensivenessExtendedTests
    {
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

        // SqlInjection (CSSEC031)
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_SqlInjection_Positive()
        {
            var source = @"
using System;
namespace System.Data.SqlClient { public class SqlCommand { public SqlCommand(string cmd) {} } }
class P { void M() { var q = Console.ReadLine(); new System.Data.SqlClient.SqlCommand(q); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC031"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_SqlInjection_Negative()
        {
            var source = @"
namespace System.Data.SqlClient { public class SqlCommand { public SqlCommand(string cmd) {} } }
class P { void M() { new System.Data.SqlClient.SqlCommand(""SELECT * FROM Users""); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsFalse(result.Findings.Any(f => f.BugId == "CSSEC031"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Inter_SqlInjection_Positive()
        {
            var source = @"
using System;
namespace System.Data.SqlClient { public class SqlCommand { public SqlCommand(string cmd) {} } }
class P { 
    void M() { var q = Console.ReadLine(); Run(q); } 
    void Run(string q) { new System.Data.SqlClient.SqlCommand(q); } 
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, InterOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC031"));
        }

        // LdapInjection (CSSEC032)
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_LdapInjection_Positive()
        {
            var source = @"
using System;
namespace System.DirectoryServices { public class DirectorySearcher { public DirectorySearcher(string f) {} } }
class P { void M() { var f = Console.ReadLine(); new System.DirectoryServices.DirectorySearcher(f); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC032"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_LdapInjection_Negative()
        {
            var source = @"
namespace System.DirectoryServices { public class DirectorySearcher { public DirectorySearcher(string f) {} } }
class P { void M() { new System.DirectoryServices.DirectorySearcher(""(objectClass=user)""); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsFalse(result.Findings.Any(f => f.BugId == "CSSEC032"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Inter_LdapInjection_Positive()
        {
            var source = @"
using System;
namespace System.DirectoryServices { public class DirectorySearcher { public DirectorySearcher(string f) {} } }
class P { 
    void M() { var f = Console.ReadLine(); Run(f); } 
    void Run(string f) { new System.DirectoryServices.DirectorySearcher(f); } 
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, InterOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC032"));
        }

        // XpathInjection (CSSEC033)
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_XpathInjection_Positive()
        {
            var source = @"
using System;
namespace System.Xml.XPath { public class XPathExpression { public static void Compile(string x) {} } }
class P { void M() { var x = Console.ReadLine(); System.Xml.XPath.XPathExpression.Compile(x); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC033"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_XpathInjection_Negative()
        {
            var source = @"
namespace System.Xml.XPath { public class XPathExpression { public static void Compile(string x) {} } }
class P { void M() { System.Xml.XPath.XPathExpression.Compile(""//user""); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsFalse(result.Findings.Any(f => f.BugId == "CSSEC033"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Inter_XpathInjection_Positive()
        {
            var source = @"
using System;
namespace System.Xml.XPath { public class XPathExpression { public static void Compile(string x) {} } }
class P { 
    void M() { var x = Console.ReadLine(); Run(x); } 
    void Run(string x) { System.Xml.XPath.XPathExpression.Compile(x); } 
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, InterOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC033"));
        }

        // RedirectInjection (CSSEC034)
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_RedirectInjection_Positive()
        {
            var source = @"
using System;
namespace Microsoft.AspNetCore.Http { public class HttpResponse { public void Redirect(string u) {} } }
class P { void M(Microsoft.AspNetCore.Http.HttpResponse r) { var url = Console.ReadLine(); r.Redirect(url); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC034"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_RedirectInjection_Negative()
        {
            var source = @"
namespace Microsoft.AspNetCore.Http { public class HttpResponse { public void Redirect(string u) {} } }
class P { void M(Microsoft.AspNetCore.Http.HttpResponse r) { r.Redirect(""/home""); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsFalse(result.Findings.Any(f => f.BugId == "CSSEC034"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Inter_RedirectInjection_Positive()
        {
            var source = @"
using System;
namespace Microsoft.AspNetCore.Http { public class HttpResponse { public void Redirect(string u) {} } }
class P { 
    void M(Microsoft.AspNetCore.Http.HttpResponse r) { var url = Console.ReadLine(); Run(r, url); } 
    void Run(Microsoft.AspNetCore.Http.HttpResponse r, string url) { r.Redirect(url); } 
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, InterOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC034"));
        }

        // HeaderInjection (CSSEC035)
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_HeaderInjection_Positive()
        {
            var source = @"
using System;
namespace System.Web { public class HttpResponse { public void AddHeader(string n, string v) {} } }
class P { void M(System.Web.HttpResponse r) { var val = Console.ReadLine(); r.AddHeader(""X-Custom"", val); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC035"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_HeaderInjection_Negative()
        {
            var source = @"
namespace System.Web { public class HttpResponse { public void AddHeader(string n, string v) {} } }
class P { void M(System.Web.HttpResponse r) { r.AddHeader(""X-Custom"", ""safe-value""); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsFalse(result.Findings.Any(f => f.BugId == "CSSEC035"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Inter_HeaderInjection_Positive()
        {
            var source = @"
using System;
namespace System.Web { public class HttpResponse { public void AddHeader(string n, string v) {} } }
class P { 
    void M(System.Web.HttpResponse r) { var val = Console.ReadLine(); Run(r, val); } 
    void Run(System.Web.HttpResponse r, string val) { r.AddHeader(""X-Custom"", val); } 
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, InterOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC035"));
        }

        // TemplateInjection (CSSEC036)
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_TemplateInjection_Positive()
        {
            var source = @"
using System;
namespace Scriban { public class Template { public static void Parse(string t) {} } }
class P { void M() { var t = Console.ReadLine(); Scriban.Template.Parse(t); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC036"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Intra_TemplateInjection_Negative()
        {
            var source = @"
namespace Scriban { public class Template { public static void Parse(string t) {} } }
class P { void M() { Scriban.Template.Parse(""Hello {{name}}""); } }";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, IntraOptions);
            Assert.IsFalse(result.Findings.Any(f => f.BugId == "CSSEC036"));
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_Inter_TemplateInjection_Positive()
        {
            var source = @"
using System;
namespace Scriban { public class Template { public static void Parse(string t) {} } }
class P { 
    void M() { var t = Console.ReadLine(); Run(t); } 
    void Run(string t) { Scriban.Template.Parse(t); } 
}";
            var analyzer = new CSharpMathBugAnalyzer();
            var result = analyzer.AnalyzeText(source, InterOptions);
            Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC036"));
        }

        // RepoScanInfo Append and Replace tests for new kinds
        [TestMethod]
        [Timeout(10000)]
        public void Analyze_RepoScanInfo_AppendMode_SqlInjection()
        {
            var source = @"
using System;
namespace Custom {
    public class Sql { public void Exec(string q) { } }
}
class P { 
    void M() { var q = Console.ReadLine(); new Custom.Sql().Exec(q); } 
}";
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|append
sink|SqlInjection|Custom.Sql|Exec|0|Exec SQL argument
");

                var analyzer = new CSharpMathBugAnalyzer();
                var options = CSharpMathBugAnalyzerOptions.Default with
                {
                    SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                    ConfidenceThreshold = 0.0,
                    RepoScanInfoPath = repoScanInfoPath
                };

                var result = analyzer.AnalyzeText(source, options);
                Assert.IsTrue(result.Findings.Any(f => f.BugId == "CSSEC031"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        [TestMethod]
        [Timeout(10000)]
        public void Analyze_RepoScanInfo_ReplaceMode_DropsDefaultSinks()
        {
            var source = @"
using System;
namespace System.Data.SqlClient { public class SqlCommand { public SqlCommand(string cmd) {} } }
class P { 
    void M() { var q = Console.ReadLine(); new System.Data.SqlClient.SqlCommand(q); } 
}";
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var repoScanInfoPath = Path.Combine(tempDir, "RepoScanInfo.txt");
                File.WriteAllText(repoScanInfoPath, @"
version|1
model|replace
sink|SqlInjection|Custom.Sql|Exec|0|Exec SQL argument
");

                var analyzer = new CSharpMathBugAnalyzer();
                var options = CSharpMathBugAnalyzerOptions.Default with
                {
                    SecurityFlowMode = CSharpSecurityFlowMode.IntraProcedural,
                    ConfidenceThreshold = 0.0,
                    RepoScanInfoPath = repoScanInfoPath
                };

                var result = analyzer.AnalyzeText(source, options);
                // Since model|replace drops all defaults, SqlCommand should NO LONGER be flagged.
                Assert.IsFalse(result.Findings.Any(f => f.BugId == "CSSEC031"));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}