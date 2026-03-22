using System;
using System.IO;

namespace SymSolvers
{
    // PriorityJob: minimal translator from raw math problem files into prepared problem files
    public static class PriorityJob
    {
        // Translates text files found in rawDir into problem_####.txt files placed in outDir.
        // Keeps transformation minimal: trims content and skips empty files.
        public static void TranslateRawToPrepared(string rawDir, string outDir)
        {
            if (rawDir is null) throw new ArgumentNullException(nameof(rawDir));
            if (outDir is null) throw new ArgumentNullException(nameof(outDir));
            if (!Directory.Exists(rawDir)) throw new DirectoryNotFoundException($"Raw directory not found: {rawDir}");

            Directory.CreateDirectory(outDir);

            var files = Directory.GetFiles(rawDir, "*.txt");
            int idx = 1;
            foreach (var f in files)
            {
                var text = File.ReadAllText(f).Trim();
                if (string.IsNullOrEmpty(text)) continue;
                var outFile = Path.Combine(outDir, $"problem_{idx:D4}.txt");
                File.WriteAllText(outFile, text);
                idx++;
            }
        }
    }
}
