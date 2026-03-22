using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.CSharpIO;
using Sym.Operations;
using SymRules;
using SymSolvers;
using SymSolvers.ProblemStructure;
#if ENABLE_COBRA
using SymCobra.Regression;
#endif

namespace WordsToSym
{
    public class ProblemScriptEGraphWrapper
    {
        public string SolveWithEGraph(string problemScript, CancellationToken token = default, object? sharedEGraph = null)
        {
            var warnings = new List<string>();
            var diagnostics = new List<string>();

            // 1. Extract problem structure (removes XML and block comments)
            var ps = ProblemStruct.ProblemScriptToProblemStruct(problemScript);

            // 2. Extract options (from the ORIGINAL script or the structure)
            var optionsMap = ParseOptions(problemScript);
            
            // 3. Extract Rules block content
            var rulesBlockRules = ParseRulesBlock(problemScript, diagnostics);
            
            // 4. Build SolveContext data
            var additionalData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            
            // Map options to additionalData with type conversion
            foreach (var kvp in optionsMap)
            {
                if (bool.TryParse(kvp.Value, out var b))
                    additionalData[kvp.Key] = b;
                else if (int.TryParse(kvp.Value, out var i))
                    additionalData[kvp.Key] = i;
                else
                    additionalData[kvp.Key] = kvp.Value;
            }

            // Resolve rule packs (special handling)
            if (optionsMap.TryGetValue("RulePacks", out var rulePacksOption))
            {
                var resolvedPacks = ResolveRulePacks(rulePacksOption, warnings);
                additionalData["RulePacks"] = string.Join(",", resolvedPacks);
            }

            // Options handling
            Symbol? targetVar = null;
            if (optionsMap.TryGetValue("Target", out var targetStr))
            {
                targetVar = new Symbol(targetStr);
            }

            int maxIterations = 1000;
            if (optionsMap.TryGetValue("MaxIterations", out var maxIterStr) && int.TryParse(maxIterStr, out var maxIter))
                maxIterations = maxIter;

            int maxConcurrency = 8;
            if (optionsMap.TryGetValue("MaxConcurrency", out var maxConcStr) && int.TryParse(maxConcStr, out var maxConc))
                maxConcurrency = maxConc;

            bool enableTracing = false;
            if (optionsMap.TryGetValue("EnableTracing", out var traceStr) && bool.TryParse(traceStr, out var trace))
                enableTracing = trace;

            // Handle Assumptions (comma separated)
            var assumptionKeys = new[] { "AssumePositive", "AssumeReal", "AssumeComplex", "AssumeInteger" };
            foreach (var key in assumptionKeys)
            {
                if (optionsMap.TryGetValue(key, out var val))
                {
                    if (val.Contains(','))
                    {
                        additionalData[key] = val.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                    }
                    else if (bool.TryParse(val, out var boolVal))
                    {
                        additionalData[key] = boolVal;
                    }
                    else
                    {
                        additionalData[key] = val;
                    }
                }
            }

            // Handle Shapes option: A=[10,10], B=[10]
            var symbolShapes = new Dictionary<string, Shape>(StringComparer.Ordinal);
            if (optionsMap.TryGetValue("Shapes", out var shapesStr))
            {
                // Use a regex to find Name=[dims] pairs correctly even with commas inside []
                var shapeMatches = Regex.Matches(shapesStr, @"([A-Za-z0-9_]+)\s*=\s*(\[[^\]]+\]|Scalar)", RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in shapeMatches)
                {
                    var symName = m.Groups[1].Value;
                    var shapeVal = m.Groups[2].Value;
                    
                    var match = Regex.Match(shapeVal, @"\[(.*?)\]");
                    if (match.Success)
                    {
                        var dimsStr = match.Groups[1].Value;
                        var dims = dimsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(d => int.TryParse(d.Trim(), out var val) ? val : -1)
                            .Where(v => v >= 0)
                            .ToImmutableArray();
                        symbolShapes[symName] = new Shape(dims);
                    }
                    else if (string.Equals(shapeVal, "Scalar", StringComparison.OrdinalIgnoreCase))
                    {
                        symbolShapes[symName] = Shape.Scalar;
                    }
                }
            }

            // Handle Attributes option: Fact1={Rel:0.9, Cert:1.0, Tokens:50}
            var symbolAttributes = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            if (optionsMap.TryGetValue("Attributes", out var attrStr))
            {
                var attrMatches = Regex.Matches(attrStr, @"([A-Za-z0-9_]+)\s*=\s*\{([^}]+)\}", RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in attrMatches)
                {
                    var symName = m.Groups[1].Value;
                    var propsStr = m.Groups[2].Value;
                    var props = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    
                    var propMatches = Regex.Matches(propsStr, @"([A-Za-z0-9_]+)\s*:\s*([0-9\.]+)");
                    foreach (System.Text.RegularExpressions.Match pm in propMatches)
                    {
                        if (double.TryParse(pm.Groups[2].Value, out var val))
                        {
                            props[pm.Groups[1].Value] = val;
                        }
                    }
                    symbolAttributes[symName] = props;
                }
            }
            additionalData["Attributes"] = symbolAttributes;

            // 6. Parse expressions and inline rules from constraints
            var rawConstraints = new List<IExpression>();
            var allRules = new List<Rule>();
            allRules.AddRange(rulesBlockRules);

            foreach (var constraintStr in ps.Constraints)
            {
                try
                {
                    // Check if it's a Rule(...) call
                    if (constraintStr.TrimStart().StartsWith("Rule(", StringComparison.OrdinalIgnoreCase))
                    {
                        var parsedRules = CSharpIO.ParseRules(constraintStr);
                        allRules.AddRange(parsedRules);
                    }
                    else
                    {
                        // Use strict parsing as per spec
                        var exprs = CSharpIO.ParseExpressionsStrict(constraintStr);
                        rawConstraints.AddRange(exprs);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"Parse error in constraint '{constraintStr}': {ex.Message}");
                }
            }

            // Apply symbol shapes
            var constraints = rawConstraints.Select(c => ExpressionHelpers.Transform(c, e =>
            {
                if (e is Symbol s && symbolShapes.TryGetValue(s.Name, out var shape))
                {
                    return new Symbol(s.Name, shape);
                }
                return e;
            })).ToList();

            if (diagnostics.Any())
            {
                return FormatError("Parsing diagnostics failed.", diagnostics, optionsMap, warnings);
            }

            // Target Inference
            if (targetVar == null && constraints.Any())
            {
                var allSymbols = constraints.SelectMany(c => SymbolCollector.CollectSymbolsList(c)).Select(s => s.Name).Distinct().ToList();
                if (allSymbols.Count == 1)
                {
                    targetVar = new Symbol(allSymbols[0]);
                }
            }

            // Initial context for rule building
            var initialContext = new SolveContext(
                targetVariable: targetVar,
                rules: allRules.ToImmutableList(),
                maxIterations: maxIterations,
                maxConcurrency: maxConcurrency,
                enableTracing: enableTracing,
                additionalData: additionalData.ToImmutableDictionary(),
                cancellationToken: token,
                sharedEGraph: sharedEGraph as EGraph
            );

            // Load full rule set using RuleProvider
            var finalRules = RuleProvider.BuildRules(initialContext);

            // Final context for solving
            var context = new SolveContext(
                targetVariable: targetVar,
                rules: finalRules,
                maxIterations: maxIterations,
                maxConcurrency: maxConcurrency,
                enableTracing: enableTracing,
                additionalData: additionalData.ToImmutableDictionary(),
                cancellationToken: token,
                sharedEGraph: sharedEGraph as EGraph
            );

            // 7. Policy for mixed constraints
            var equalities = constraints.OfType<Equality>().ToList();
            var nonEqualities = constraints.Where(c => c is not Equality).ToList();

            bool regressionMode = false;
            if (optionsMap.TryGetValue("RegressionMode", out var regressionModeRaw) && bool.TryParse(regressionModeRaw, out var parsedRegressionMode))
            {
                regressionMode = parsedRegressionMode;
            }

            bool treatNonEqualitiesAsError = false;
            if (optionsMap.TryGetValue("TreatNonEqualitiesAsError", out var nonEqErrStr) && bool.TryParse(nonEqErrStr, out var nonEqErr))
                treatNonEqualitiesAsError = nonEqErr;

            if (treatNonEqualitiesAsError && nonEqualities.Any())
            {
                return FormatError("Non-equality constraints found and TreatNonEqualitiesAsError is true.", nonEqualities.Select(c => c.ToDisplayString()), optionsMap, warnings);
            }

            if (regressionMode)
            {
#if ENABLE_COBRA
                try
                {
                    var regressionSolver = new CobraRegressionProblemScriptSolver();
                    return regressionSolver.Solve(optionsMap, token);
                }
                catch (Exception ex)
                {
                    return FormatError($"Regression solve failed: {ex.Message}", null, optionsMap, warnings);
                }
#else
                return FormatError("RegressionMode requires a build with COBRA support.", null, optionsMap, warnings);
#endif
            }

            // 8. Invoke solver
            var solver = EGraphBackendSelector.CreateSolveStrategy(context);
            IExpression? problemExpr;

            // Pre-seed attributes into context shared graph if available
            if (sharedEGraph is EGraph eg)
            {
                foreach (var symAttr in symbolAttributes)
                {
                    int classId = eg.Add(new Symbol(symAttr.Key));
                    var eClass = eg.GetClass(classId);
                    foreach (var prop in symAttr.Value)
                    {
                        eClass.Metadata[prop.Key] = prop.Value;
                    }
                }
            }
            
            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
            {
                Console.WriteLine($"DEBUG: Wrapper solving. Constraints count: {constraints.Count}");
                foreach(var c in constraints) Console.WriteLine($"DEBUG: Constraint: {c.ToDisplayString()}");
            }

            // If target is specified, pass only equalities to EGraph saturation.
            // If NO target is specified, pass all constraints (Simplification Mode).
            var toSolve = (targetVar != null && equalities.Any()) ? equalities.Cast<IExpression>().ToList() : constraints;

            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine($"DEBUG: toSolve count: {toSolve.Count}");

            if (toSolve.Count == 1)
            {
                problemExpr = toSolve[0];
            }
            else if (toSolve.Any())
            {
                problemExpr = new Vector(toSolve.ToImmutableList());
            }
            else
            {
                return FormatError("No constraints found to solve.", null, optionsMap, warnings);
            }

            if (Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1")
                Console.WriteLine($"DEBUG: Final problemExpr: {problemExpr.ToDisplayString()}");

            var result = solver.Solve(problemExpr, context);

            // 9. Format and return result
            if (result.IsSuccess)
            {
                if (result.ResultExpression is Vector resVec)
                {
                    return string.Join(Environment.NewLine, resVec.Arguments.Select(CSharpIO.FormatExpr));
                }
                return result.ResultExpression != null ? CSharpIO.FormatExpr(result.ResultExpression) : "Success (no result expression)";
            }
            else
            {
                var solverDiags = new List<string> { result.Message };
                if (nonEqualities.Any())
                {
                    solverDiags.Add("Non-equality constraints omitted from EGraph saturation:");
                    foreach (var ne in nonEqualities) solverDiags.Add("  " + ne.ToDisplayString());
                }
                return FormatError(result.Message, solverDiags, optionsMap, warnings, "Error:");
            }
        }

        private Dictionary<string, string> ParseOptions(string normalizedScript)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // XML-style block
            var xmlMatch = Regex.Match(normalizedScript, @"<Options>(.*?)</Options>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (xmlMatch.Success)
            {
                var content = xmlMatch.Groups[1].Value;
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        options[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }

            // Directive lines
            var directiveMatches = Regex.Matches(normalizedScript, @"@option\s+([A-Za-z0-9_]+)\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in directiveMatches)
            {
                options[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }

            return options;
        }

        private List<Rule> ParseRulesBlock(string normalizedScript, List<string> diagnostics)
        {
            var rules = new List<Rule>();
            var xmlMatch = Regex.Match(normalizedScript, @"<Rules>(.*?)</Rules>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (xmlMatch.Success)
            {
                var content = xmlMatch.Groups[1].Value;
                try
                {
                    var parsed = CSharpIO.ParseRules(content);
                    rules.AddRange(parsed);
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"Error parsing <Rules> block: {ex.Message}");
                }
            }
            return rules;
        }

        private ImmutableList<string> ResolveRulePacks(string rulePacksOption, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(rulePacksOption)) return ImmutableList<string>.Empty;

            var requested = rulePacksOption.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim());
            var available = RulePackLibrary.GetRulePackInfos();
            var resolved = new List<string>();

            foreach (var req in requested)
            {
                var folderMatch = available.FirstOrDefault(p => string.Equals(Path.GetFileName(p.Path), req, StringComparison.OrdinalIgnoreCase));
                if (folderMatch != null)
                {
                    resolved.Add(Path.GetFileName(folderMatch.Path));
                    continue;
                }

                var nameMatch = available.FirstOrDefault(p => string.Equals(p.Name, req, StringComparison.OrdinalIgnoreCase));
                if (nameMatch != null)
                {
                    resolved.Add(Path.GetFileName(nameMatch.Path));
                    continue;
                }

                warnings.Add($"Rule pack '{req}' could not be resolved.");
            }

            return resolved.ToImmutableList();
        }

        private string FormatError(string message, IEnumerable<string>? diagnostics, Dictionary<string, string>? options, List<string>? warnings, string prefix = "Error:")
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{prefix} {message}");
            
            if (diagnostics != null && diagnostics.Any())
            {
                sb.AppendLine("Diagnostics:");
                foreach (var d in diagnostics) sb.AppendLine("  " + d);
            }

            if (warnings != null && warnings.Any())
            {
                sb.AppendLine("Warnings:");
                foreach (var w in warnings) sb.AppendLine("  " + w);
            }

            if (options != null && options.Any())
            {
                sb.AppendLine("Options:");
                foreach (var kvp in options) sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }

            return sb.ToString().Trim();
        }
    }
}
