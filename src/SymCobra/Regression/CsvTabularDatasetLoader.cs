using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SymCobra.Regression;

public static class CsvTabularDatasetLoader
{
    public static TabularDataset Load(string path, string targetColumn, IReadOnlyList<string>? featureColumns = null)
    {
        var lines = File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        if (lines.Length < 2)
        {
            throw new InvalidOperationException("Regression dataset must contain a header and at least one data row.");
        }

        var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        int targetIndex = Array.FindIndex(headers, h => string.Equals(h, targetColumn, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
        {
            throw new InvalidOperationException($"Target column '{targetColumn}' was not found in dataset.");
        }

        var selectedFeatures = featureColumns is { Count: > 0 }
            ? featureColumns.ToArray()
            : headers.Where((_, i) => i != targetIndex).ToArray();

        var featureIndexes = selectedFeatures
            .Select(name =>
            {
                int index = Array.FindIndex(headers, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    throw new InvalidOperationException($"Feature column '{name}' was not found in dataset.");
                }
                return index;
            })
            .ToArray();

        var rows = new List<double[]>();
        var targets = new List<double>();

        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length != headers.Length)
            {
                continue;
            }

            var row = new double[featureIndexes.Length];
            for (int j = 0; j < featureIndexes.Length; j++)
            {
                row[j] = double.Parse(parts[featureIndexes[j]], CultureInfo.InvariantCulture);
            }

            rows.Add(row);
            targets.Add(double.Parse(parts[targetIndex], CultureInfo.InvariantCulture));
        }

        return new TabularDataset(selectedFeatures, rows, targets);
    }
}
