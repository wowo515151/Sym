// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Sym.CSharpIO;

namespace SymRules;

public sealed class GeneratedRuleStore
{
    public const string DefaultFileName = "generated.rules.txt";

    private readonly string _path;
    private readonly int _maxRules;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public GeneratedRuleStore(string folder, int maxRules = 256)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("Generated rules folder must be provided.", nameof(folder));
        }

        _path = Path.Combine(folder, DefaultFileName);
        _maxRules = Math.Max(1, maxRules);
        Directory.CreateDirectory(folder);
    }

    public IReadOnlyList<RuleDefinition> LoadTextRules()
    {
        _gate.Wait();
        try
        {
            return LoadTextRulesNoLock();
        }
        finally
        {
            _gate.Release();
        }
    }

    public GeneratedRuleSnapshot LoadSnapshot()
    {
        _gate.Wait();
        try
        {
            return LoadSnapshotNoLock();
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<Sym.Core.Rule> LoadCoreRules()
    {
        var textRules = LoadTextRules();
        var coreRules = new List<Sym.Core.Rule>();
        foreach (var rule in textRules)
        {
            try
            {
                var core = rule.ToCoreRule();
            if (core != null) coreRules.Add(core);
            }
            catch
            {
                // Skip malformed generated rules.
            }
        }
        return coreRules;
    }

    public int AppendRules(IEnumerable<Sym.Core.Rule> rules)
    {
        if (rules is null)
        {
            return 0;
        }

        var incoming = rules.ToList();
        if (incoming.Count == 0)
        {
            return 0;
        }

        _gate.Wait();
        try
        {
            var existing = LoadCoreRulesNoLock();
            var ordered = new List<Sym.Core.Rule>(existing);
            var keys = new HashSet<string>(existing.Select(BuildKey), StringComparer.Ordinal);

            var added = 0;
            foreach (var rule in incoming)
            {
                var key = BuildKey(rule);
                if (keys.Add(key))
                {
                    ordered.Add(rule);
                    added++;
                }
            }

            if (added == 0)
            {
                return 0;
            }

            if (ordered.Count > _maxRules)
            {
                ordered = ordered.Skip(ordered.Count - _maxRules).ToList();
            }

            Persist(ordered);
            return added;
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<RuleDefinition> LoadTextRulesNoLock()
    {
        var rules = new List<RuleDefinition>();
        if (!File.Exists(_path))
        {
            return rules;
        }

        foreach (var raw in File.ReadAllLines(_path))
        {
            var trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (RuleTextParser.TryGenerateCoreSource(trimmed, out var coreSource, out _))
            {
                rules.Add(new RuleDefinition
                {
                    Name = "generated",
                    Text = trimmed,
                    CoreSource = coreSource,
                    Diagnostics = null
                });
            }
        }

        return rules;
    }

    private GeneratedRuleSnapshot LoadSnapshotNoLock()
    {
        var textRules = LoadTextRulesNoLock();
        var coreRules = new List<Sym.Core.Rule>();
        foreach (var rule in textRules)
        {
            try
            {
                var core = rule.ToCoreRule();
            if (core != null) coreRules.Add(core);
            }
            catch
            {
                // Skip malformed generated rules.
            }
        }
        return new GeneratedRuleSnapshot(textRules.Count, coreRules);
    }

    private List<Sym.Core.Rule> LoadCoreRulesNoLock()
    {
        var textRules = LoadTextRulesNoLock();
        var coreRules = new List<Sym.Core.Rule>();
        foreach (var rule in textRules)
        {
            try
            {
                var core = rule.ToCoreRule();
            if (core != null) coreRules.Add(core);
            }
            catch
            {
                // Skip malformed generated rules.
            }
        }
        return coreRules;
    }

    private void Persist(IEnumerable<Sym.Core.Rule> rules)
    {
        var lines = rules.Select(CSharpIO.FormatRule).ToArray();
        File.WriteAllLines(_path, lines);
    }

    private static string BuildKey(Sym.Core.Rule rule)
    {
        var pattern = CSharpIO.FormatExpr(rule.Pattern);
        var replacement = CSharpIO.FormatExpr(rule.Replacement);
        return $"{pattern}=>{replacement}";
    }
}

public sealed record GeneratedRuleSnapshot(int Count, IReadOnlyList<Sym.Core.Rule> CoreRules);
