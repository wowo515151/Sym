// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SymSolvers.CSharpAnalysis
{
    public sealed record CSharpSecurityFlowModel(
        ImmutableArray<SourceSpec> Sources,
        ImmutableArray<SinkSpec> Sinks,
        ImmutableArray<SanitizerSpec> Sanitizers)
    {
        public static CSharpSecurityFlowModel Empty { get; } = new(
            ImmutableArray<SourceSpec>.Empty,
            ImmutableArray<SinkSpec>.Empty,
            ImmutableArray<SanitizerSpec>.Empty);
    }

    public static class CSharpSecurityFlowModelCatalog
    {
        private static readonly CSharpSecurityFlowModel DefaultModel = BuildDefault();

        public static CSharpSecurityFlowModel Default => DefaultModel;

        private static CSharpSecurityFlowModel BuildDefault()
        {
            var sources = ImmutableArray.CreateBuilder<SourceSpec>();
            var sinks = ImmutableArray.CreateBuilder<SinkSpec>();
            var sanitizers = ImmutableArray.CreateBuilder<SanitizerSpec>();
            static void AddSink(
                ImmutableArray<SinkSpec>.Builder builder,
                string typeName,
                string methodName,
                int[] taintedIndices,
                SecurityFlowKind kind,
                string description)
            {
                builder.Add(new SinkSpec(typeName, methodName, taintedIndices, kind, description));
            }

            // Sources. This shared catalog is consumed by both intra/interprocedural analyzers.
            sources.Add(new SourceSpec("Microsoft.AspNetCore.Http.IQueryCollection", "Item", IsProperty: true, Kind: SourceKind.UserSource)); // Request.Query["x"]
            sources.Add(new SourceSpec("Microsoft.AspNetCore.Http.IFormCollection", "Item", IsProperty: true, Kind: SourceKind.UserSource)); // Request.Form["x"]
            sources.Add(new SourceSpec("System.Console", "ReadLine", Kind: SourceKind.UserSource));
            sources.Add(new SourceSpec("System.Environment", "GetEnvironmentVariable", Kind: SourceKind.UserSource));

            // Repo-specific and vendor-specific sources belong in RepoScanInfo, not this built-in catalog.

            // Sinks
            // CSSEC003: Command Injection
            AddSink(sinks, "System.Diagnostics.Process", "Start", new[] { 0, 1 }, SecurityFlowKind.CommandInjection, "Process.Start command argument");
            AddSink(sinks, "System.Diagnostics.ProcessStartInfo", ".ctor", new[] { 0, 1 }, SecurityFlowKind.CommandInjection, "ProcessStartInfo command/file argument");

            // CSSEC004: Path Traversal
            AddSink(sinks, "System.IO.File", "ReadAllText", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.ReadAllText path argument");
            AddSink(sinks, "System.IO.File", "ReadAllBytes", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.ReadAllBytes path argument");
            AddSink(sinks, "System.IO.File", "ReadLines", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.ReadLines path argument");
            AddSink(sinks, "System.IO.File", "WriteAllText", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.WriteAllText path argument");
            AddSink(sinks, "System.IO.File", "WriteAllBytes", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.WriteAllBytes path argument");
            AddSink(sinks, "System.IO.File", "AppendAllText", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.AppendAllText path argument");
            AddSink(sinks, "System.IO.File", "Open", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.Open path argument");
            AddSink(sinks, "System.IO.File", "OpenRead", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.OpenRead path argument");
            AddSink(sinks, "System.IO.File", "OpenWrite", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.OpenWrite path argument");
            AddSink(sinks, "System.IO.File", "Delete", new[] { 0 }, SecurityFlowKind.PathTraversal, "File.Delete path argument");
            AddSink(sinks, "System.IO.File", "Move", new[] { 0, 1 }, SecurityFlowKind.PathTraversal, "File.Move source/destination path arguments");
            AddSink(sinks, "System.IO.File", "Copy", new[] { 0, 1 }, SecurityFlowKind.PathTraversal, "File.Copy source/destination path arguments");
            AddSink(sinks, "System.IO.FileStream", ".ctor", new[] { 0 }, SecurityFlowKind.PathTraversal, "FileStream path argument");
            AddSink(sinks, "System.IO.FileInfo", ".ctor", new[] { 0 }, SecurityFlowKind.PathTraversal, "FileInfo constructor path argument");
            AddSink(sinks, "System.IO.DirectoryInfo", ".ctor", new[] { 0 }, SecurityFlowKind.PathTraversal, "DirectoryInfo constructor path argument");
            AddSink(sinks, "System.IO.Directory", "GetFiles", new[] { 0 }, SecurityFlowKind.PathTraversal, "Directory.GetFiles path argument");
            AddSink(sinks, "System.IO.Directory", "GetDirectories", new[] { 0 }, SecurityFlowKind.PathTraversal, "Directory.GetDirectories path argument");
            AddSink(sinks, "System.IO.Directory", "EnumerateFiles", new[] { 0 }, SecurityFlowKind.PathTraversal, "Directory.EnumerateFiles path argument");
            AddSink(sinks, "System.IO.Directory", "EnumerateDirectories", new[] { 0 }, SecurityFlowKind.PathTraversal, "Directory.EnumerateDirectories path argument");
            AddSink(sinks, "System.IO.Directory", "CreateDirectory", new[] { 0 }, SecurityFlowKind.PathTraversal, "Directory.CreateDirectory path argument");
            AddSink(sinks, "System.IO.Directory", "Delete", new[] { 0 }, SecurityFlowKind.PathTraversal, "Directory.Delete path argument");
            AddSink(sinks, "System.IO.Directory", "Move", new[] { 0, 1 }, SecurityFlowKind.PathTraversal, "Directory.Move source/destination path arguments");

            // CSSEC031: SQL Injection
            AddSink(sinks, "System.Data.SqlClient.SqlCommand", ".ctor", new[] { 0 }, SecurityFlowKind.SqlInjection, "SqlCommand query text argument");
            AddSink(sinks, "Microsoft.Data.SqlClient.SqlCommand", ".ctor", new[] { 0 }, SecurityFlowKind.SqlInjection, "SqlCommand query text argument");
            AddSink(sinks, "System.Data.Odbc.OdbcCommand", ".ctor", new[] { 0 }, SecurityFlowKind.SqlInjection, "OdbcCommand query text argument");
            AddSink(sinks, "System.Data.OleDb.OleDbCommand", ".ctor", new[] { 0 }, SecurityFlowKind.SqlInjection, "OleDbCommand query text argument");
            AddSink(sinks, "Microsoft.Data.Sqlite.SqliteCommand", ".ctor", new[] { 0 }, SecurityFlowKind.SqlInjection, "SqliteCommand query text argument");
            AddSink(sinks, "Npgsql.NpgsqlCommand", ".ctor", new[] { 0 }, SecurityFlowKind.SqlInjection, "NpgsqlCommand query text argument");
            AddSink(sinks, "MySql.Data.MySqlClient.MySqlCommand", ".ctor", new[] { 0 }, SecurityFlowKind.SqlInjection, "MySqlCommand query text argument");
            AddSink(sinks, "MySqlConnector.MySqlCommand", ".ctor", new[] { 0 }, SecurityFlowKind.SqlInjection, "MySqlCommand query text argument");

            // CSSEC032: LDAP Injection
            AddSink(sinks, "System.DirectoryServices.DirectorySearcher", ".ctor", new[] { 0 }, SecurityFlowKind.LdapInjection, "DirectorySearcher filter argument");
            AddSink(sinks, "System.DirectoryServices.DirectorySearcher", ".ctor", new[] { 1 }, SecurityFlowKind.LdapInjection, "DirectorySearcher filter argument");
            AddSink(sinks, "System.DirectoryServices.Protocols.SearchRequest", ".ctor", new[] { 1 }, SecurityFlowKind.LdapInjection, "SearchRequest LDAP filter argument");

            // CSSEC033: XPath Injection
            AddSink(sinks, "System.Xml.XPath.XPathExpression", "Compile", new[] { 0 }, SecurityFlowKind.XpathInjection, "XPathExpression.Compile expression argument");
            AddSink(sinks, "System.Xml.XPath.XPathNavigator", "Select", new[] { 0 }, SecurityFlowKind.XpathInjection, "XPathNavigator.Select XPath argument");
            AddSink(sinks, "System.Xml.XPath.XPathNavigator", "SelectSingleNode", new[] { 0 }, SecurityFlowKind.XpathInjection, "XPathNavigator.SelectSingleNode XPath argument");
            AddSink(sinks, "System.Xml.XmlNode", "SelectNodes", new[] { 0 }, SecurityFlowKind.XpathInjection, "XmlNode.SelectNodes XPath argument");
            AddSink(sinks, "System.Xml.XmlNode", "SelectSingleNode", new[] { 0 }, SecurityFlowKind.XpathInjection, "XmlNode.SelectSingleNode XPath argument");
            AddSink(sinks, "System.Xml.XmlDocument", "SelectNodes", new[] { 0 }, SecurityFlowKind.XpathInjection, "XmlDocument.SelectNodes XPath argument");
            AddSink(sinks, "System.Xml.XmlDocument", "SelectSingleNode", new[] { 0 }, SecurityFlowKind.XpathInjection, "XmlDocument.SelectSingleNode XPath argument");

            // CSSEC034: Redirect Injection
            AddSink(sinks, "Microsoft.AspNetCore.Http.HttpResponse", "Redirect", new[] { 0 }, SecurityFlowKind.RedirectInjection, "HttpResponse.Redirect target URL argument");
            AddSink(sinks, "System.Web.HttpResponse", "Redirect", new[] { 0 }, SecurityFlowKind.RedirectInjection, "HttpResponse.Redirect target URL argument");
            AddSink(sinks, "System.Web.HttpResponseBase", "Redirect", new[] { 0 }, SecurityFlowKind.RedirectInjection, "HttpResponseBase.Redirect target URL argument");

            // CSSEC035: Header Injection
            AddSink(sinks, "System.Web.HttpResponse", "AddHeader", new[] { 0, 1 }, SecurityFlowKind.HeaderInjection, "HttpResponse.AddHeader name/value arguments");
            AddSink(sinks, "System.Web.HttpResponseBase", "AddHeader", new[] { 0, 1 }, SecurityFlowKind.HeaderInjection, "HttpResponseBase.AddHeader name/value arguments");
            AddSink(sinks, "Microsoft.AspNetCore.Http.IHeaderDictionary", "Add", new[] { 0, 1 }, SecurityFlowKind.HeaderInjection, "IHeaderDictionary.Add name/value arguments");
            AddSink(sinks, "Microsoft.AspNetCore.Http.IHeaderDictionary", "Append", new[] { 0, 1 }, SecurityFlowKind.HeaderInjection, "IHeaderDictionary.Append name/value arguments");

            // CSSEC036: Template Injection
            AddSink(sinks, "Scriban.Template", "Parse", new[] { 0 }, SecurityFlowKind.TemplateInjection, "Scriban.Template.Parse template argument");
            AddSink(sinks, "Fluid.FluidParser", "Parse", new[] { 0 }, SecurityFlowKind.TemplateInjection, "FluidParser.Parse template argument");
            AddSink(sinks, "RazorEngine.Templating.Razor", "RunCompile", new[] { 0 }, SecurityFlowKind.TemplateInjection, "Razor.RunCompile template argument");

            // Sanitizers
            sanitizers.Add(new SanitizerSpec("System.IO.Path", "GetFileName", new[] { 0 }, ReturnsSanitized: true));

            return new CSharpSecurityFlowModel(
                sources.ToImmutable(),
                sinks.ToImmutable(),
                sanitizers.ToImmutable());
        }

        public static CSharpSecurityFlowModel Merge(CSharpSecurityFlowModel baseline, CSharpSecurityFlowModel overlay)
        {
            // RepoScanInfo overlays should deterministically replace matching built-in entries.
            var sources = MergeSources(baseline.Sources, overlay.Sources);
            var sinks = MergeSinks(baseline.Sinks, overlay.Sinks);
            var sanitizers = MergeSanitizers(baseline.Sanitizers, overlay.Sanitizers);

            return new CSharpSecurityFlowModel(sources, sinks, sanitizers);
        }

        private static ImmutableArray<SourceSpec> MergeSources(
            ImmutableArray<SourceSpec> baseline,
            ImmutableArray<SourceSpec> overlay)
            => MergeWithOverlay(baseline, overlay, CreateSourceKey);

        private static ImmutableArray<SinkSpec> MergeSinks(
            ImmutableArray<SinkSpec> baseline,
            ImmutableArray<SinkSpec> overlay)
            => MergeWithOverlay(baseline, overlay, CreateSinkKey);

        private static ImmutableArray<SanitizerSpec> MergeSanitizers(
            ImmutableArray<SanitizerSpec> baseline,
            ImmutableArray<SanitizerSpec> overlay)
            => MergeWithOverlay(baseline, overlay, CreateSanitizerKey);

        private static ImmutableArray<T> MergeWithOverlay<T>(
            IEnumerable<T> baseline,
            IEnumerable<T> overlay,
            Func<T, string> keySelector)
        {
            var merged = new List<T>();
            var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

            AppendOrReplace(baseline, merged, indexByKey, keySelector);
            AppendOrReplace(overlay, merged, indexByKey, keySelector);
            return merged.ToImmutableArray();
        }

        private static void AppendOrReplace<T>(
            IEnumerable<T> items,
            IList<T> output,
            IDictionary<string, int> indexByKey,
            Func<T, string> keySelector)
        {
            foreach (var item in items)
            {
                var key = keySelector(item);
                if (indexByKey.TryGetValue(key, out var existingIndex))
                {
                    output[existingIndex] = item;
                    continue;
                }

                indexByKey[key] = output.Count;
                output.Add(item);
            }
        }

        private static string CreateSourceKey(SourceSpec source)
            => $"{source.TypeName}|{source.MethodName}|{source.IsProperty}";

        private static string CreateSinkKey(SinkSpec sink)
        {
            var indices = sink.TaintedIndices is { Length: > 0 }
                ? string.Join(",", sink.TaintedIndices.OrderBy(static i => i))
                : string.Empty;
            return $"{sink.Kind}|{sink.TypeName}|{sink.MethodName}|{indices}";
        }

        private static string CreateSanitizerKey(SanitizerSpec sanitizer)
        {
            var indices = sanitizer.SanitizedIndices is { Length: > 0 }
                ? string.Join(",", sanitizer.SanitizedIndices.OrderBy(static i => i))
                : string.Empty;
            return $"{sanitizer.TypeName}|{sanitizer.MethodName}|{indices}";
        }
    }
}
