// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Sym.Atoms;
using Sym.Core;

namespace SymRules;

public record RulePack(string Name, string Description, string Path, bool EnabledByDefault, int Priority, ImmutableList<Sym.Core.Rule> Rules);
public record RulePackInfo(string Name, string Description, string Path, string SourcePath, bool EnabledByDefault, int Priority);

/// <summary>
/// Central place to enumerate curated rule pack folders.
/// </summary>
public static class RulePackLibrary
{
    private static readonly Dictionary<string, ImmutableList<Sym.Core.Rule>> BuiltinMapping = new()
    {
        { "AlgebraicStrategy", IdentityRuleLibrary.PiecewiseRules },
        { "RecurrenceStrategy", IdentityRuleLibrary.RecurrenceRules },
        { "Graph", SymRules.Graph.GraphRules.Rules }
    };

    private static readonly Lazy<IReadOnlyList<RulePackInfo>> CachedRulePackInfos =
        new(DiscoverRulePackInfos, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly ConcurrentDictionary<string, Lazy<RulePack>> CachedRulePacks =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<RulePackInfo> GetRulePackInfos()
    {
        return CachedRulePackInfos.Value;
    }

    public static IReadOnlyList<RulePack> GetRulePacks()
    {
        return CachedRulePackInfos.Value.Select(GetOrLoadPack).ToList();
    }

    public static RulePack? FindRulePack(string nameOrPath)
    {
        var info = FindRulePackInfo(nameOrPath);
        return info is null ? null : GetOrLoadPack(info);
    }

    public static RulePackInfo? FindRulePackInfo(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return null;
        }

        return CachedRulePackInfos.Value.FirstOrDefault(p => MatchesPack(p, nameOrPath));
    }

    private static IReadOnlyList<RulePackInfo> DiscoverRulePackInfos()
    {
        var skipFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".vs", "Properties", "Bad" };
        var discovered = new List<RulePackInfo>();
        var rulesRoot = FindRulesRoot();

        if (rulesRoot is null)
        {
            return EmbeddedRulePacks.GetPackInfos();
        }

        foreach (var packPath in Directory.GetDirectories(rulesRoot))
        {
            var folderName = Path.GetFileName(packPath);
            if (skipFolders.Contains(folderName))
            {
                continue;
            }

            discovered.Add(LoadPackInfo(packPath, folderName));
        }

        return discovered.Count > 0 ? discovered : EmbeddedRulePacks.GetPackInfos();
    }

    private static string? FindRulesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SymRules");
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            dir = dir.Parent;
        }

        return null;
    }

    private static RulePackInfo LoadPackInfo(string path, string defaultName, string? displayPath = null)
    {
        var (name, description, enabled, priority) = LoadPackMetadata(path, defaultName);
        return new RulePackInfo(name, description, displayPath ?? path, path, enabled, priority);
    }

    private static RulePack GetOrLoadPack(RulePackInfo info)
    {
        return CachedRulePacks.GetOrAdd(
            info.SourcePath,
            static (_, state) => new Lazy<RulePack>(
                () => LoadPack(state.SourcePath, state.Name, state.Path, state.Description, state.EnabledByDefault, state.Priority),
                LazyThreadSafetyMode.ExecutionAndPublication),
            info).Value;
    }

    private static RulePack LoadPack(string path, string name, string displayPath, string description, bool enabled, int priority)
    {
        var rawRules = EmbeddedRulePacks.TryGetPack(path, out _) || EmbeddedRulePacks.TryGetPack(name, out _)
            ? EmbeddedRulePacks.LoadRules(path)
            : RuleLoader.LoadRules(path).ToList();
        var rules = new List<Sym.Core.Rule>();

        foreach (var rr in rawRules)
        {
            var core = rr.ToCoreRule();
            if (core != null)
            {
                rules.Add(core);
            }
            else if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            {
                Console.WriteLine($"DEBUG: Failed to convert rule: {rr.Text} \n CoreSource: {rr.CoreSource}");
            }
        }

        if (BuiltinMapping.TryGetValue(name, out var builtinRules))
        {
            rules.AddRange(builtinRules);
        }

        return new RulePack(name, description, displayPath, enabled, priority, rules.ToImmutableList());
    }

    private static bool MatchesPack(RulePackInfo pack, string nameOrPath)
    {
        return pack.Path.EndsWith("\\" + nameOrPath, StringComparison.OrdinalIgnoreCase) ||
               pack.Path.EndsWith("/" + nameOrPath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFileName(pack.Path), nameOrPath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(pack.Name, nameOrPath, StringComparison.OrdinalIgnoreCase);
    }

    private static (string name, string description, bool enabled, int priority) LoadPackMetadata(string path, string defaultName)
    {
        string name = defaultName;
        string description = "Standard rule pack.";
        bool enabled = true;
        int priority = 10;

        if (EmbeddedRulePacks.TryGetPack(defaultName, out var embeddedPack) && embeddedPack is not null)
        {
            name = embeddedPack.Name;
            description = embeddedPack.Description;
            enabled = embeddedPack.EnabledByDefault;
            priority = embeddedPack.Priority;
        }

        var jsonPath = Path.Combine(path, "pack.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var n)) name = n.GetString() ?? name;
                if (root.TryGetProperty("description", out var d)) description = d.GetString() ?? description;
                if (root.TryGetProperty("enabledByDefault", out var e)) enabled = e.GetBoolean();
                if (root.TryGetProperty("priority", out var p)) priority = p.GetInt32();
            }
            catch
            {
                // Fallback to defaults when metadata cannot be parsed.
            }
        }

        return (name, description, enabled, priority);
    }
}