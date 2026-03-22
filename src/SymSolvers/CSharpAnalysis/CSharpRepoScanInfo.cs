using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace SymSolvers.CSharpAnalysis
{
    public sealed record CSharpRepoScanInfo(
        bool ReplaceDefaultModel,
        CSharpSecurityFlowModel Model,
        IReadOnlyList<string> Diagnostics)
    {
        public bool HasEntries =>
            Model.Sources.Length > 0 ||
            Model.Sinks.Length > 0 ||
            Model.Sanitizers.Length > 0;
    }

    public static class CSharpRepoScanInfoLoader
    {
        private const string DefaultSourceName = "<memory>";

        public static CSharpRepoScanInfo LoadFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new CSharpRepoScanInfo(
                    ReplaceDefaultModel: false,
                    Model: CSharpSecurityFlowModel.Empty,
                    Diagnostics: new[] { "RepoScanInfo path is empty." });
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                {
                    return new CSharpRepoScanInfo(
                        ReplaceDefaultModel: false,
                        Model: CSharpSecurityFlowModel.Empty,
                        Diagnostics: new[] { $"RepoScanInfo file not found: {fullPath}" });
                }

                var content = File.ReadAllText(fullPath);
                return Parse(content, fullPath);
            }
            catch (Exception ex)
            {
                return new CSharpRepoScanInfo(
                    ReplaceDefaultModel: false,
                    Model: CSharpSecurityFlowModel.Empty,
                    Diagnostics: new[] { $"Failed to load RepoScanInfo '{path}': {ex.Message}" });
            }
        }

        public static CSharpRepoScanInfo Parse(string content, string? sourceName = null)
        {
            var sources = ImmutableArray.CreateBuilder<SourceSpec>();
            var sinks = ImmutableArray.CreateBuilder<SinkSpec>();
            var sanitizers = ImmutableArray.CreateBuilder<SanitizerSpec>();
            var diagnostics = new List<string>();
            var replaceDefaultModel = false;
            var origin = string.IsNullOrWhiteSpace(sourceName) ? DefaultSourceName : sourceName!;

            if (string.IsNullOrWhiteSpace(content))
            {
                diagnostics.Add($"{origin}: RepoScanInfo is empty.");
                return new CSharpRepoScanInfo(
                    ReplaceDefaultModel: false,
                    Model: CSharpSecurityFlowModel.Empty,
                    Diagnostics: diagnostics);
            }

            var lines = content.Replace("\r\n", "\n").Split('\n');
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var raw = lines[lineIndex];
                var trimmed = raw.Trim();
                var lineNumber = lineIndex + 1;

                if (string.IsNullOrWhiteSpace(trimmed) ||
                    trimmed.StartsWith("#", StringComparison.Ordinal) ||
                    trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var fields = SplitFields(trimmed);
                if (fields.Length == 0)
                {
                    continue;
                }

                var directive = fields[0].Trim().TrimStart('\uFEFF').ToLowerInvariant();
                // If a new model spec type is introduced, add it here and update docs/RepoScanInfo.md + template.
                switch (directive)
                {
                    case "version":
                        ParseVersion(fields, diagnostics, origin, lineNumber);
                        break;
                    case "model":
                        ParseModelMode(fields, diagnostics, origin, lineNumber, ref replaceDefaultModel);
                        break;
                    case "source":
                        ParseSource(fields, sources, diagnostics, origin, lineNumber);
                        break;
                    case "sink":
                        ParseSink(fields, sinks, diagnostics, origin, lineNumber);
                        break;
                    case "sanitizer":
                        ParseSanitizer(fields, sanitizers, diagnostics, origin, lineNumber);
                        break;
                    default:
                        diagnostics.Add($"{origin}:{lineNumber}: Unknown RepoScanInfo directive '{fields[0]}'.");
                        break;
                }
            }

            var parsedModel = new CSharpSecurityFlowModel(
                sources.ToImmutable(),
                sinks.ToImmutable(),
                sanitizers.ToImmutable());
            // Keep parser output normalized so replace-mode configs do not emit duplicate logical entries.
            var normalizedModel = CSharpSecurityFlowModelCatalog.Merge(CSharpSecurityFlowModel.Empty, parsedModel);

            return new CSharpRepoScanInfo(
                ReplaceDefaultModel: replaceDefaultModel,
                Model: normalizedModel,
                Diagnostics: diagnostics);
        }

        private static void ParseVersion(
            IReadOnlyList<string> fields,
            ICollection<string> diagnostics,
            string origin,
            int lineNumber)
        {
            if (fields.Count < 2)
            {
                diagnostics.Add($"{origin}:{lineNumber}: 'version' requires one value (expected 'version|1').");
                return;
            }

            if (!string.Equals(fields[1], "1", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Unsupported RepoScanInfo version '{fields[1]}'. Expected '1'.");
            }
        }

        private static void ParseModelMode(
            IReadOnlyList<string> fields,
            ICollection<string> diagnostics,
            string origin,
            int lineNumber,
            ref bool replaceDefaultModel)
        {
            if (fields.Count < 2)
            {
                diagnostics.Add($"{origin}:{lineNumber}: 'model' requires one value ('append' or 'replace').");
                return;
            }

            var mode = fields[1].Trim().ToLowerInvariant();
            switch (mode)
            {
                case "append":
                    replaceDefaultModel = false;
                    break;
                case "replace":
                    replaceDefaultModel = true;
                    break;
                default:
                    diagnostics.Add($"{origin}:{lineNumber}: Unknown model mode '{fields[1]}'. Use 'append' or 'replace'.");
                    break;
            }
        }

        private static void ParseSource(
            IReadOnlyList<string> fields,
            ICollection<SourceSpec> sources,
            ICollection<string> diagnostics,
            string origin,
            int lineNumber)
        {
            // source|<SourceKind>|<TypeName>|<MemberName>|<method/property>|[ReturnProp1,ReturnProp2]
            if (fields.Count < 4)
            {
                diagnostics.Add($"{origin}:{lineNumber}: 'source' requires at least 4 fields.");
                return;
            }

            if (!TryParseEnum(fields[1], out SourceKind sourceKind))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Invalid SourceKind '{fields[1]}'.");
                return;
            }

            var typeName = fields[2];
            var memberName = fields[3];
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(memberName))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Source type/member cannot be empty.");
                return;
            }

            var memberKind = fields.Count >= 5 ? fields[4] : "method";
            var isProperty = memberKind.Equals("property", StringComparison.OrdinalIgnoreCase);
            if (!isProperty && !memberKind.Equals("method", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Invalid source member kind '{memberKind}'. Use 'method' or 'property'.");
                return;
            }

            string[]? taintedReturnProperties = null;
            if (fields.Count >= 6 && !string.IsNullOrWhiteSpace(fields[5]))
            {
                taintedReturnProperties = SplitCsv(fields[5]).ToArray();
            }

            sources.Add(new SourceSpec(
                TypeName: typeName,
                MethodName: memberName,
                TaintedReturnProperties: taintedReturnProperties,
                IsProperty: isProperty,
                Kind: sourceKind));
        }

        private static void ParseSink(
            IReadOnlyList<string> fields,
            ICollection<SinkSpec> sinks,
            ICollection<string> diagnostics,
            string origin,
            int lineNumber)
        {
            // sink|<SecurityFlowKind>|<TypeName>|<MemberName>|<TaintedIndicesCsv>|[Description]
            if (fields.Count < 5)
            {
                diagnostics.Add($"{origin}:{lineNumber}: 'sink' requires at least 5 fields.");
                return;
            }

            if (!TryParseEnum(fields[1], out SecurityFlowKind sinkKind))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Invalid SecurityFlowKind '{fields[1]}'.");
                return;
            }

            var typeName = fields[2];
            var memberName = fields[3];
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(memberName))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Sink type/member cannot be empty.");
                return;
            }

            if (!TryParseIndices(fields[4], out var indices))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Invalid sink indices '{fields[4]}'.");
                return;
            }

            var description = fields.Count >= 6 && !string.IsNullOrWhiteSpace(fields[5])
                ? string.Join(" | ", fields.Skip(5))
                : $"{typeName}.{memberName} tainted argument";

            sinks.Add(new SinkSpec(
                TypeName: typeName,
                MethodName: memberName,
                TaintedIndices: indices,
                Kind: sinkKind,
                Description: description));
        }

        private static void ParseSanitizer(
            IReadOnlyList<string> fields,
            ICollection<SanitizerSpec> sanitizers,
            ICollection<string> diagnostics,
            string origin,
            int lineNumber)
        {
            // sanitizer|<TypeName>|<MemberName>|<SanitizedIndicesCsv>|<ReturnsSanitizedBool>
            if (fields.Count < 5)
            {
                diagnostics.Add($"{origin}:{lineNumber}: 'sanitizer' requires 5 fields.");
                return;
            }

            var typeName = fields[1];
            var memberName = fields[2];
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(memberName))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Sanitizer type/member cannot be empty.");
                return;
            }

            if (!TryParseIndices(fields[3], out var indices))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Invalid sanitizer indices '{fields[3]}'.");
                return;
            }

            if (!bool.TryParse(fields[4], out var returnsSanitized))
            {
                diagnostics.Add($"{origin}:{lineNumber}: Invalid sanitizer ReturnsSanitized value '{fields[4]}'. Use true/false.");
                return;
            }

            sanitizers.Add(new SanitizerSpec(
                TypeName: typeName,
                MethodName: memberName,
                SanitizedIndices: indices,
                ReturnsSanitized: returnsSanitized));
        }

        private static string[] SplitFields(string line) =>
            line.Split('|')
                .Select(static part => part.Trim())
                .ToArray();

        private static IEnumerable<string> SplitCsv(string value) =>
            value.Split(',')
                .Select(static part => part.Trim())
                .Where(static part => !string.IsNullOrWhiteSpace(part));

        private static bool TryParseIndices(string csv, out int[] indices)
        {
            var parsed = new List<int>();
            foreach (var token in SplitCsv(csv))
            {
                if (!int.TryParse(token, out var index) || index < 0)
                {
                    indices = Array.Empty<int>();
                    return false;
                }

                parsed.Add(index);
            }

            indices = parsed.Distinct().OrderBy(static i => i).ToArray();
            return indices.Length > 0;
        }

        private static bool TryParseEnum<TEnum>(string raw, out TEnum value)
            where TEnum : struct, Enum
        {
            if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) &&
                Enum.IsDefined(typeof(TEnum), parsed))
            {
                value = parsed;
                return true;
            }

            value = default;
            return false;
        }
    }
}
