// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.IO;
namespace SymRules
{
    public static class RuleParser
    {
        public static List<string> ParseRulesFromDirectory(string path)
        {
            var result = new List<string>();
            if (!Directory.Exists(path)) return result;
            foreach (var file in Directory.GetFiles(path, "*.rule.txt"))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) result.Add(trimmed);
                }
            }
            return result;
        }
    }
}
