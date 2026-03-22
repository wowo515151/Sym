using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Sym.Algebra;
using Sym.Calculus;
using Sym.Core;
using SymRules;
using SymCore;

namespace SymSolvers;

public static class RuleProvider
{
    private const string RulePacksKey = "RulePacks";

    private static readonly StringComparer Comparer = StringComparer.Ordinal;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImmutableList<Sym.Core.Rule>> Cache = new(StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Sym.Core.RuleIndex> IndexCache = new(StringComparer.Ordinal);

    private static readonly object BuildLock = new();

    public static ImmutableList<Sym.Core.Rule> BuildRules(SolveContext context)
    {
        if ((context.Rules is null || context.Rules.Count == 0))
        {
            var cacheKey = BuildCacheKey(context);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (Cache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }

                lock (BuildLock)
                {
                    if (Cache.TryGetValue(cacheKey, out cached))
                    {
                        return cached;
                    }

                    IReadOnlyList<string> _;
                    var built = BuildRulesWithDiagnostics(context, out _);
                    Cache[cacheKey] = built;
                    return built;
                }
            }

            IReadOnlyList<string> __;
            return BuildRulesWithDiagnostics(context, out __);
        }

        IReadOnlyList<string> ___;
        return BuildRulesWithDiagnostics(context, out ___);
    }

    public static ImmutableList<Sym.Core.Rule> BuildRulesWithDiagnostics(SolveContext context, out IReadOnlyList<string> diagnostics)
    {
        return BuildRulesAndIndex(context, out diagnostics, out _);
    }

    public static ImmutableList<Sym.Core.Rule> BuildRulesAndIndex(SolveContext context, out IReadOnlyList<string> diagnostics, out RuleIndex index)
    {
        var cacheKey = BuildCacheKey(context);
        if (!string.IsNullOrEmpty(cacheKey))
        {
            if (IndexCache.TryGetValue(cacheKey, out var cachedIndex))
            {
                diagnostics = new List<string>();
                index = cachedIndex;
                return cachedIndex.AllRules;
            }
        }

        lock (BuildLock)
        {
            if (!string.IsNullOrEmpty(cacheKey) && IndexCache.TryGetValue(cacheKey, out var cachedIndex))
            {
                diagnostics = new List<string>();
                index = cachedIndex;
                return cachedIndex.AllRules;
            }

            var keys = new HashSet<string>(Comparer);
            var builder = ImmutableList.CreateBuilder<Sym.Core.Rule>();
            var diags = new List<string>();
            var indexMap = new Dictionary<string, List<Sym.Core.Rule>>(StringComparer.Ordinal);
            var headToPatterns = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            bool useArithmeticBenchmarkProfile = context.GetBool(SolverOptionKeys.ArithmeticBenchmarkRuleProfile, false);

            void AddRule(Sym.Core.Rule rule)
            {
                var head = Sym.Core.RuleIndex.GetHead(rule.Pattern);
                var patternText = rule.Pattern.ToDisplayString();
                if (!headToPatterns.TryGetValue(head, out var patterns))
                {
                    patterns = new HashSet<string>(StringComparer.Ordinal);
                    headToPatterns[head] = patterns;
                    patterns.Add(patternText);
                }
                else if (!patterns.Add(patternText))
                {
                    diags.Add($"Duplicate rule pattern detected for head '{head}': {patternText}");
                }

                var key = BuildKey(rule);
                if (!keys.Add(key))
                {
                    diags.Add($"Duplicate rule pattern detected for head '{head}': {patternText}");
                    return;
                }

                builder.Add(rule);
                if (!indexMap.TryGetValue(head, out var list))
                {
                    list = new List<Sym.Core.Rule>();
                    indexMap[head] = list;
                }
                list.Add(rule);
            }

            foreach (var r in context.Rules ?? Enumerable.Empty<Sym.Core.Rule>())
            {
                AddRule(r);
            }

            foreach (var r in AlgebraicSimplificationRules.SimplificationRules)
            {
                AddRule(r);
            }

            if (!useArithmeticBenchmarkProfile)
            {
                foreach (var r in CalculusRules.DifferentiationRules)
                {
                    AddRule(r);
                }

                foreach (var r in CalculusRules.IntegrationRules)
                {
                    AddRule(r);
                }

                foreach (var r in CalculusRules.VectorCalculusRules)
                {
                    AddRule(r);
                }
            }

            // Equation-solving normalizations are loaded from SymRules packs.

            if (!useArithmeticBenchmarkProfile)
            {
                foreach (var r in IdentityRuleLibrary.RecurrenceRules)
                {
                    AddRule(r);
                }

                foreach (var r in IdentityRuleLibrary.PiecewiseRules)
                {
                    AddRule(r);
                }
            }

            // Load rules from default curated packs
            var packs = ResolveRulePacks(context, out var packDiag);
            if (packDiag.Count > 0)
            {
                diags.AddRange(packDiag);
            }
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_RULES") == "1")
                Console.WriteLine($"DEBUG: Found {packs.Count()} rule packs.");

            foreach (var pack in packs)
            {
                if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_RULES") == "1")
                    Console.WriteLine($"DEBUG: Loading rules from pack '{pack.Path}'.");

                if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_RULES") == "1")
                    Console.WriteLine($"DEBUG: Loaded {pack.Rules.Count} rules from '{pack.Path}'.");

                foreach (var core in pack.Rules)
                {
                    AddRule(core);
                }
            }

            var rulesFolder = context.GetString(SolverOptionKeys.RulesFolder, string.Empty);
            if (!string.IsNullOrWhiteSpace(rulesFolder))
            {
                foreach (var textRule in RuleLoader.LoadRules(rulesFolder))
                {
                    try
                    {
                        var core = textRule.ToCoreRule();
                        if (core != null) AddRule(core);
                    }
                    catch
                    {
                        diags.Add($"Failed to convert rule '{textRule.Text}' in folder '{rulesFolder}'.");
                    }
                }
            }

            var generatedFolder = context.GetString(SolverOptionKeys.GeneratedRulesFolder, string.Empty);
            if (!string.IsNullOrWhiteSpace(generatedFolder))
            {
                try
                {
                    var store = new GeneratedRuleStore(generatedFolder);
                    foreach (var rule in store.LoadCoreRules())
                    {
                        AddRule(rule);
                    }
                }
                catch (Exception ex)
                {
                    Logging.LogError("UniversalSolverStrategyLoadGeneratedRules", ex.Message, ex.StackTrace);
                    diags.Add($"Failed to load generated rules from '{generatedFolder}': {ex.Message}");
                }
            }

            diagnostics = diags;
            index = new RuleIndex(
                builder.ToImmutable(),
                indexMap.ToImmutableDictionary(k => k.Key, k => k.Value.ToImmutableList(), StringComparer.Ordinal));

            if (!string.IsNullOrEmpty(cacheKey))
            {
                IndexCache[cacheKey] = index;
            }

            return builder.ToImmutable();
        }
    }

    private static IReadOnlyList<RulePack> ResolveRulePacks(SolveContext context, out List<string> diagnostics)
    {
        diagnostics = new List<string>();
        var all = RulePackLibrary.GetRulePackInfos();
        bool useArithmeticBenchmarkProfile = context.GetBool(SolverOptionKeys.ArithmeticBenchmarkRuleProfile, false);

        if (context.AdditionalData is null || !context.AdditionalData.TryGetValue(RulePacksKey, out var raw) || raw is null)
        {
            if (useArithmeticBenchmarkProfile)
            {
                var equationPack = RulePackLibrary.FindRulePack("EquationSolving");
                return equationPack is null ? [] : [equationPack];
            }

            return all.Select(info => RulePackLibrary.FindRulePack(info.Name)).Where(pack => pack is not null).Select(pack => pack!).ToList();
        }

        var requested = NormalizePackNames(raw);
        if (requested.Count == 0)
        {
            return all.Select(info => RulePackLibrary.FindRulePack(info.Name)).Where(pack => pack is not null).Select(pack => pack!).ToList();
        }

        var selected = new List<RulePack>();
        foreach (var name in requested)
        {
            var pack = RulePackLibrary.FindRulePack(name);

            if (pack is null)
            {
                diagnostics.Add($"Requested rule pack '{name}' not found.");
                continue;
            }

            selected.Add(pack);
        }

        return selected;
    }

    private static List<string> NormalizePackNames(object raw)
    {
        if (raw is string s)
        {
            return s
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (raw is IEnumerable<string> list)
        {
            return list
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new List<string>();
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string stamp, DateTime expires)> StampCache = new(StringComparer.Ordinal);

    private static string BuildCacheKey(SolveContext context)
    {
        var rulesFolder = context.GetString(SolverOptionKeys.RulesFolder, string.Empty);
        if (context.Rules is not null && context.Rules.Count > 0)
        {
            return string.Empty;
        }
        var packKey = GetPackSelectionKey(context);
        var profileKey = context.GetBool(SolverOptionKeys.ArithmeticBenchmarkRuleProfile, false) ? "profile:arith-bench" : "profile:default";
        var generatedFolder = context.GetString(SolverOptionKeys.GeneratedRulesFolder, string.Empty);
        if (!string.IsNullOrWhiteSpace(generatedFolder))
        {
            var baseKey = string.IsNullOrWhiteSpace(rulesFolder) ? "<default>" : rulesFolder.Trim();
            var stamp = "missing";

            if (StampCache.TryGetValue(generatedFolder, out var cached) && cached.expires > DateTime.UtcNow)
            {
                stamp = cached.stamp;
            }
            else
            {
                try
                {
                    var path = Path.Combine(generatedFolder, GeneratedRuleStore.DefaultFileName);
                    if (File.Exists(path))
                    {
                        stamp = File.GetLastWriteTimeUtc(path).Ticks.ToString();
                    }
                    StampCache[generatedFolder] = (stamp, DateTime.UtcNow.AddSeconds(10));
                }
                catch
                {
                    stamp = "error";
                }
            }
            return $"{baseKey}|{packKey}|{profileKey}|gen:{generatedFolder}|{stamp}";
        }
        return $"{(string.IsNullOrWhiteSpace(rulesFolder) ? "<default>" : rulesFolder.Trim())}|{packKey}|{profileKey}";
    }

    private static string GetPackSelectionKey(SolveContext context)
    {
        if (context.AdditionalData is null || !context.AdditionalData.TryGetValue(RulePacksKey, out var raw) || raw is null)
        {
            return "packs:*";
        }

        var names = NormalizePackNames(raw);
        if (names.Count == 0)
        {
            return "packs:*";
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return "packs:" + string.Join(",", names);
    }

    private static string BuildKey(Sym.Core.Rule rule)
    {
        var pattern = rule.Pattern.ToDisplayString();
        var replacement = rule.Replacement.ToDisplayString();
        return $"{pattern}=>{replacement}";
    }
}
