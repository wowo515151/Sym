using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SymCore;
using SymSolvers.ProblemStructure;

namespace WordsToSym;

public sealed class ProblemStructs
{
    public List<ProblemStruct> Items { get; } = new();

    public ProblemStructs()
    {
    }

    public ProblemStructs(IEnumerable<ProblemStruct> items)
    {
        if (items is null) return;
        Items.AddRange(items.Where(i => i is not null));
    }

    public sealed record ConvertBatchOptions(
        int DegreeOfParallelism = 8,
        TimeSpan? PerProblemTimeout = null,
        int MaxAttempts = 2,
        bool UseStrictOnRetry = true,
        bool ValidateProblemStruct = true,
        ProblemStructValidationOptions? ValidationOptions = null,
        ProblemScriptCleanupOptions? CleanupOptions = null,
        bool PopulateKnownSolutionMatch = true,
        KnownSolutionMatchOptions? KnownSolutionMatchOptions = null);

    public sealed record ProblemConvertOutcome(
        int Index,
        string WordProblem,
        bool TimedOut,
        TimeSpan Elapsed,
        string? ProblemScript,
        ProblemStruct? ProblemStruct,
        string? ErrorMessage);

    private static async Task<ProblemConvertOutcome> ConvertOneAsync(
        int index,
        WordProblemInput input,
        ConvertBatchOptions options,
        Func<string, Task<Response>> converter,
        Func<string, Task<Response>> strictConverter,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[convert {index + 1}] starting");
        var sw = Stopwatch.StartNew();

        var timeout = options.PerProblemTimeout ?? TimeSpan.FromSeconds(10);
        var attempts = Math.Max(1, options.MaxAttempts);
        var cleanupOptions = options.CleanupOptions ?? new ProblemScriptCleanupOptions();
        var validationOptions = options.ValidationOptions ?? new ProblemStructValidationOptions(CleanupOptions: cleanupOptions);
        if (validationOptions.CleanupOptions is null)
        {
            validationOptions = validationOptions with { CleanupOptions = cleanupOptions };
        }

        string? lastScript = null;
        ProblemStruct? lastStruct = null;
        string? lastError = null;
        bool timedOut = false;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            var useStrict = attempt > 1 && options.UseStrictOnRetry;
            var activeConverter = useStrict ? strictConverter : converter;

            var (attemptTimedOut, script, errorMessage) = await ConvertScriptAsync(
                input.WordProblem ?? string.Empty,
                timeout,
                activeConverter,
                cancellationToken).ConfigureAwait(false);

            if (attemptTimedOut)
            {
                timedOut = true;
                lastError = errorMessage ?? $"Timed out after {timeout.TotalSeconds:0.###}s";
                break;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                lastError = errorMessage;
                if (attempt >= attempts)
                {
                    break;
                }
                continue;
            }

            var cleanedScript = ProblemScriptCleaner.NormalizeProblemScript(script ?? string.Empty, cleanupOptions);
            var parsed = ProblemStruct.ProblemScriptToProblemStruct(cleanedScript);
            parsed.WordProblem = input.WordProblem ?? string.Empty;
            parsed.KnownSolution = input.KnownSolution ?? string.Empty;
            parsed.ProblemScript = cleanedScript;

            var inferred = RelatedConstraintInference.InferConstraints(parsed, parsed.Constraints);
            if (inferred.Count > 0)
            {
                foreach (var constraint in inferred)
                {
                    var line = (constraint ?? string.Empty).Trim();
                    if (line.Length == 0) continue;
                    parsed.Constraints.Add(line.EndsWith(";", StringComparison.Ordinal) ? line : line + ";");
                }
            }

            lastStruct = parsed;
            lastScript = cleanedScript;

            if (options.ValidateProblemStruct)
            {
                var validation = ProblemStructValidator.Validate(parsed, validationOptions);
                if (validation.IsValid)
                {
                    lastError = null;
                    break;
                }

                lastError = validation.Message;
                if (attempt >= attempts)
                {
                    break;
                }
                continue;
            }

            lastError = null;
            break;
        }

        sw.Stop();

        if (timedOut)
        {
            Console.WriteLine($"[convert {index + 1}] timed out after {sw.Elapsed.TotalSeconds:0.00}s");
        }
        else if (!string.IsNullOrWhiteSpace(lastError))
        {
            Console.WriteLine($"[convert {index + 1}] failed after {sw.Elapsed.TotalSeconds:0.00}s: {lastError}");
        }
        else
        {
            Console.WriteLine($"[convert {index + 1}] completed in {sw.Elapsed.TotalSeconds:0.00}s");
        }

        return new ProblemConvertOutcome(
            index,
            input.WordProblem ?? string.Empty,
            TimedOut: timedOut,
            Elapsed: sw.Elapsed,
            ProblemScript: lastScript,
            ProblemStruct: lastStruct,
            ErrorMessage: lastError);
    }

    private static async Task<(bool timedOut, string? script, string? errorMessage)> ConvertScriptAsync(
        string wordProblem,
        TimeSpan timeout,
        Func<string, Task<Response>> converter,
        CancellationToken cancellationToken)
    {
        var convertTask = Task.Run(async () =>
        {
            var resp = await converter(wordProblem).ConfigureAwait(false);
            return resp?.Result ?? string.Empty;
        }, cancellationToken);

        var delayTask = Task.Delay(timeout, cancellationToken);

        try
        {
            var completed = await Task.WhenAny(convertTask, delayTask).ConfigureAwait(false);
            if (completed == delayTask)
            {
                return (true, null, $"Timed out after {timeout.TotalSeconds:0.###}s");
            }

            var script = await convertTask.ConfigureAwait(false);
            return (false, script, null);
        }
        catch (Exception ex)
        {
            Logging.LogError("ProblemStructsConvertProblemScriptAsync", ex.Message, ex.StackTrace);
            return (false, null, ex.Message);
        }
    }

    private static string AppendError(string? existing, string message)
    {
        if (string.IsNullOrWhiteSpace(existing)) return message;
        return $"{existing} | {message}";
    }

    public string ToXmlString()
    {
        // Use centralized XML serialization from SymCore.Common exclusively.
        return Common.ToXml(this);
    }

    public static ProblemStructs FromXmlString(string xml)
    {
        // Use centralized XML deserialization; return empty batch when parsing fails.
        var parsed = Common.FromXml<ProblemStructs>(xml);
        return parsed ?? new ProblemStructs();
    }

    private static XElement ToXml(ProblemStruct p)
    {
        var vars = new XElement("Variables");
        foreach (var v in p.Variables)
        {
            vars.Add(new XElement("Variable",
                new XAttribute("name", v.VariableName ?? string.Empty),
                new XAttribute("type", v.VariableType ?? string.Empty)));
        }

        var constraints = new XElement("Constraints");
        foreach (var c in p.Constraints)
        {
            constraints.Add(new XElement("Constraint", c ?? string.Empty));
        }

        var tags = new XElement("Tags");
        foreach (var t in p.Tags)
        {
            tags.Add(new XElement("Tag", t ?? string.Empty));
        }

        return new XElement("Problem",
            vars,
            constraints,
            tags,
            new XElement("Notes", p.Notes ?? string.Empty),
            new XElement("SolverNotes", p.SolverNotes ?? string.Empty),
            new XElement("KnownSolutionMatch", p.KnownSolutionMatch ?? string.Empty));
    }

    private static ProblemStruct FromXmlProblem(XElement problem)
    {
        var ps = new ProblemStruct();

        var vars = problem.Element("Variables");
        if (vars is not null)
        {
            foreach (var v in vars.Elements("Variable"))
            {
                ps.Variables.Add(new ProblemStruct.Variable
                {
                    VariableName = (string?)v.Attribute("name") ?? string.Empty,
                    VariableType = (string?)v.Attribute("type") ?? string.Empty
                });
            }
        }

        var constraints = problem.Element("Constraints");
        if (constraints is not null)
        {
            foreach (var c in constraints.Elements("Constraint"))
            {
                ps.Constraints.Add(c.Value ?? string.Empty);
            }
        }

        var tags = problem.Element("Tags");
        if (tags is not null)
        {
            foreach (var t in tags.Elements("Tag"))
            {
                var val = t.Value?.Trim() ?? string.Empty;
                if (val.Length > 0) ps.Tags.Add(val);
            }
        }

        ps.Notes = problem.Element("Notes")?.Value ?? string.Empty;
        ps.SolverNotes = problem.Element("SolverNotes")?.Value ?? string.Empty;
        ps.KnownSolutionMatch = problem.Element("KnownSolutionMatch")?.Value ?? string.Empty;
        return ps;
    }

    public double Grade()
    {
        if (Items.Count == 0) return 0d;
        var correct = Items.Count(p => p is not null && p.CalculatedSolutionMatchesKnownSolution);
        return (double)correct / Items.Count;
    }

    public static string CanonicalizeKnownSolutionMatch(string? knownSolutionMatch)
    {
        var text = (knownSolutionMatch ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;

        try
        {
            var expr = Sym.CSharpIO.CSharpIO.ParseExpressions(text).FirstOrDefault();
            if (expr is null) return text;

            return Sym.CSharpIO.CSharpIO.FormatExpr(expr.Canonicalize()).Trim();
        }
        catch
        {
            return text;
        }
    }

    public static int CanonicalizeKnownSolutionMatches(ProblemStructs batch)
    {
        if (batch is null) return 0;

        int updated = 0;
        foreach (var problem in batch.Items)
        {
            if (problem is null) continue;

            var canonical = CanonicalizeKnownSolutionMatch(problem.KnownSolutionMatch);
            if (!string.Equals(problem.KnownSolutionMatch ?? string.Empty, canonical, StringComparison.Ordinal))
            {
                problem.KnownSolutionMatch = canonical;
                updated++;
            }
        }

        return updated;
    }

    public static int CanonicalizeKnownSolutionMatchesInFiles(IEnumerable<string> files)
    {
        if (files is null) return 0;

        int updatedTotal = 0;
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;

            string xml;
            try
            {
                xml = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            var batch = FromXmlString(xml);
            if (batch.Items.Count == 0) continue;

            var updated = CanonicalizeKnownSolutionMatches(batch);
            if (updated == 0) continue;

            var updatedXml = batch.ToXmlString();
            try
            {
                File.WriteAllText(file, updatedXml);
                updatedTotal += updated;
            }
            catch
            {
                // Ignore files we cannot rewrite; the caller can decide how to surface failures.
            }
        }

        return updatedTotal;
    }

    public static int CanonicalizeKnownSolutionMatchesInExamFolder(string examsDir)
    {
        if (string.IsNullOrWhiteSpace(examsDir) || !Directory.Exists(examsDir)) return 0;

        var files = Directory.EnumerateFiles(examsDir, "ProblemStructs_*.txt");
        return CanonicalizeKnownSolutionMatchesInFiles(files);
    }
}
