// Copyright Warren Harding 2026
using System;
using System.IO;
using Xunit;

namespace SymRules.Tests;

public class GeneratedRuleTunerTests
{
    [Fact]
    public void Load_DefaultsWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gentuner_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var tuner = new GeneratedRuleTuner(root);
            var state = tuner.Load();

            Assert.Equal(256, state.MaxRules);
            Assert.Equal(24, state.MaxResultRules);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Update_IncreasesAndDecreasesCaps()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gentuner_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var tuner = new GeneratedRuleTuner(root);
            var state = new GeneratedRuleTuningState
            {
                MaxRules = 256,
                MaxResultRules = 24,
                ZeroAddStreak = 0
            };

            var increased = tuner.Update(state, storedCount: 250, added: 3, selected: 3, evaluated: 10);
            Assert.True(increased.MaxRules > 256);

            var decreaseSeed = new GeneratedRuleTuningState
            {
                MaxRules = 256,
                MaxResultRules = 24,
                ZeroAddStreak = 5
            };
            var decreased = tuner.Update(decreaseSeed, storedCount: 100, added: 0, selected: 0, evaluated: 10);
            Assert.True(decreased.MaxRules < 256);
            Assert.True(decreased.MaxResultRules < 24);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Save_RoundTripsState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gentuner_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var tuner = new GeneratedRuleTuner(root);
            var state = new GeneratedRuleTuningState
            {
                MaxRules = 320,
                MaxResultRules = 40,
                TotalCycles = 3,
                ZeroAddStreak = 1
            };

            tuner.Save(state);
            var loaded = tuner.Load();

            Assert.Equal(320, loaded.MaxRules);
            Assert.Equal(40, loaded.MaxResultRules);
            Assert.Equal(3, loaded.TotalCycles);
            Assert.Equal(1, loaded.ZeroAddStreak);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
