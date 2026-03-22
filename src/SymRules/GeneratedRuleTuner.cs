// Copyright Warren Harding 2026
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SymRules;

public sealed class GeneratedRuleTuner
{
    public const string StateFileName = "generated.rules.state.json";

    private const int MinMaxRules = 64;
    private const int MaxMaxRules = 4096;
    private const int MinResultRules = 8;
    private const int MaxResultRules = 256;

    private readonly string _path;
    private static readonly object StateFileLock = new();

    public GeneratedRuleTuner(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("Generated rules folder must be provided.", nameof(folder));
        }

        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, StateFileName);
    }

    public GeneratedRuleTuningState Load()
    {
        lock (StateFileLock)
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var state = JsonSerializer.Deserialize<GeneratedRuleTuningState>(json, JsonOptions);
                    if (state is not null)
                    {
                        return Normalize(state);
                    }
                }
            }
            catch
            {
                // Fall through to defaults.
            }
        }

        return Normalize(new GeneratedRuleTuningState());
    }

    public void Save(GeneratedRuleTuningState state)
    {
        var normalized = Normalize(state);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        lock (StateFileLock)
        {
            File.WriteAllText(_path, json);
        }
    }

    public GeneratedRuleTuningState Update(
        GeneratedRuleTuningState state,
        int storedCount,
        int added,
        int selected,
        int evaluated)
    {
        var next = Normalize(state);
        next.TotalCycles++;
        next.LastUpdatedUtc = DateTime.UtcNow;
        next.LastStoredCount = Math.Max(0, storedCount);
        next.LastAdded = Math.Max(0, added);
        next.LastSelected = Math.Max(0, selected);
        next.LastEvaluated = Math.Max(0, evaluated);

        if (added == 0)
        {
            next.ZeroAddStreak++;
        }
        else
        {
            next.ZeroAddStreak = 0;
        }

        next.EmaAdded = UpdateEma(next.EmaAdded, added, next.TotalCycles);

        next.MaxRules = AdjustMaxRules(next.MaxRules, storedCount, added, next.ZeroAddStreak);
        next.MaxResultRules = AdjustMaxResultRules(next.MaxResultRules, added, selected, next.ZeroAddStreak);
        return Normalize(next);
    }

    private static GeneratedRuleTuningState Normalize(GeneratedRuleTuningState state)
    {
        state.MaxRules = Clamp(state.MaxRules, MinMaxRules, MaxMaxRules);
        state.MaxResultRules = Clamp(state.MaxResultRules, MinResultRules, MaxResultRules);
        state.ZeroAddStreak = Math.Max(0, state.ZeroAddStreak);
        state.TotalCycles = Math.Max(0, state.TotalCycles);
        state.LastStoredCount = Math.Max(0, state.LastStoredCount);
        state.LastAdded = Math.Max(0, state.LastAdded);
        state.LastSelected = Math.Max(0, state.LastSelected);
        state.LastEvaluated = Math.Max(0, state.LastEvaluated);
        if (state.LastUpdatedUtc == default)
        {
            state.LastUpdatedUtc = DateTime.UtcNow;
        }
        return state;
    }

    private static int AdjustMaxRules(int current, int storedCount, int added, int zeroAddStreak)
    {
        var pressureMargin = Math.Max(8, current / 10);
        var capacityPressure = storedCount >= current - pressureMargin;

        if (capacityPressure && added > 0)
        {
            return Clamp(current + Math.Max(16, current / 6), MinMaxRules, MaxMaxRules);
        }

        if (zeroAddStreak >= 5 && storedCount < current / 2)
        {
            return Clamp(current - Math.Max(8, current / 8), MinMaxRules, MaxMaxRules);
        }

        return current;
    }

    private static int AdjustMaxResultRules(int current, int added, int selected, int zeroAddStreak)
    {
        var highYield = added >= Math.Max(1, (int)Math.Ceiling(current * 0.6));
        var noYield = added == 0 && zeroAddStreak >= 3;

        if (highYield)
        {
            return Clamp(current + Math.Max(4, current / 5), MinResultRules, MaxResultRules);
        }

        if (noYield)
        {
            return Clamp(current - Math.Max(2, current / 8), MinResultRules, MaxResultRules);
        }

        if (selected == 0 && zeroAddStreak >= 2)
        {
            return Clamp(current - Math.Max(2, current / 10), MinResultRules, MaxResultRules);
        }

        return current;
    }

    private static double UpdateEma(double current, int sample, int cycles)
    {
        const double alpha = 0.25;
        if (cycles <= 1 || current <= 0)
        {
            return sample;
        }
        return (current * (1 - alpha)) + (sample * alpha);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        return value > max ? max : value;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}

public sealed class GeneratedRuleTuningState
{
    public int MaxRules { get; set; } = 256;
    public int MaxResultRules { get; set; } = 24;
    public int TotalCycles { get; set; }
    public int ZeroAddStreak { get; set; }
    public int LastStoredCount { get; set; }
    public int LastAdded { get; set; }
    public int LastSelected { get; set; }
    public int LastEvaluated { get; set; }
    public double EmaAdded { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}
