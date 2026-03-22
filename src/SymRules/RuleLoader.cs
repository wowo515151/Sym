// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SymRules
{
    public static class RuleLoader
    {
        /// <summary>
        /// Loads rules from all .rule files in a directory and its subdirectories.
        /// </summary>
        public static IEnumerable<RuleDefinition> LoadRules(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Rules folder path must be provided.", nameof(folder));

            if (!Directory.Exists(folder))
            {
                if (EmbeddedRulePacks.TryGetPack(folder, out _))
                {
                    foreach (var rule in EmbeddedRulePacks.LoadRules(folder))
                    {
                        yield return rule;
                    }

                    yield break;
                }

                throw new DirectoryNotFoundException($"Rules folder not found: {folder}");
            }

            foreach (var file in GetRuleFiles(folder))
            {
                foreach (var rule in LoadRulesFromFile(file))
                {
                    yield return rule;
                }
            }
        }

        /// <summary>
        /// Loads rules from a specific file.
        /// </summary>
        public static IEnumerable<RuleDefinition> LoadRulesFromFile(string file)
        {
            if (!File.Exists(file)) yield break;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch
            {
                yield break;
            }

            foreach (var rule in EnumerateRulesFromLines(lines, Path.GetFileNameWithoutExtension(file)))
            {
                yield return rule;
            }
        }

        internal static IReadOnlyList<RuleDefinition> LoadRulesFromText(string text, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<RuleDefinition>();
            }

            var lines = text.Replace("\r\n", "\n").Split('\n');
            return EnumerateRulesFromLines(lines, defaultName).ToList();
        }

        public static string GetPackFolderName(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return string.Empty;
            }

            var normalized = folder.Replace('\\', '/').TrimEnd('/');
            var lastSlash = normalized.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < normalized.Length - 1)
            {
                normalized = normalized.Substring(lastSlash + 1);
            }

            return normalized;
        }

        private static IEnumerable<RuleDefinition> EnumerateRulesFromLines(IEnumerable<string> lines, string defaultName)
        {
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                    continue;

                var name = RuleTextParser.ExtractName(line, defaultName);

                if (RuleTextParser.TryGenerateCoreSource(line, out var coreSource, out var diagnostic))
                {
                    yield return new RuleDefinition
                    {
                        Name = name,
                        Text = line,
                        CoreSource = coreSource,
                        Diagnostics = null
                    };
                }
                else
                {
                    yield return new RuleDefinition
                    {
                        Name = name,
                        Text = line,
                        CoreSource = string.Empty,
                        Diagnostics = diagnostic
                    };
                }
            }
        }

        /// <summary>
        /// Centralized helper to enumerate rule files in a folder.
        /// </summary>
        public static IEnumerable<string> GetRuleFiles(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return Enumerable.Empty<string>();

            if (!Directory.Exists(folder))
                return Enumerable.Empty<string>();

            return Directory.EnumerateFiles(folder, "*.rule", SearchOption.AllDirectories);
        }
    }
}