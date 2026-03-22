// Copyright Warren Harding 2026
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SymCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SymSolvers.ProblemStructure;

public sealed class ProblemStruct
{
    public sealed class Variable
    {
        public string VariableName { get; set; } = string.Empty;
        public string VariableType { get; set; } = string.Empty;
    }

    public string WordProblem { get; set; } = string.Empty;
    public string ProblemScript { get; set; } = string.Empty;
    public string KnownSolution { get; set; } = string.Empty;
    public string KnownSolutionMatch { get; set; } = string.Empty;
    public string CalculatedSolution { get; set; } = string.Empty;
    public string SolverNotes { get; set; } = string.Empty;

    public bool CalculatedSolutionMatchesKnownSolution { get; set; }

    public List<Variable> Variables { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string Notes { get; set; } = string.Empty;

    private static readonly Regex TagsRegex =
        new(@"<Tags>(.*?)</Tags>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NotesRegex =
        new(@"<Notes>(.*?)</Notes>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ProblemStruct WordProblemToProblemStruct(string wordProblem, string knownSolution)
    {
        return new ProblemStruct
        {
            WordProblem = wordProblem ?? string.Empty,
            ProblemScript = string.Empty,
            KnownSolution = knownSolution ?? string.Empty
        };
    }

    public static ProblemStruct ProblemScriptToProblemStruct(string problemScript)
    {
        var ps = new ProblemStruct();
        if (string.IsNullOrWhiteSpace(problemScript)) return ps;

        ps.ProblemScript = problemScript;

        string code = ExtractXmlAndRemove(problemScript, ps);

        // Pre-pass: Clean up code lines and extract Word Problem text
        var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var cleanCodeLines = new List<string>();
        foreach (var rawLine in lines)
        {
            var line = StripLineComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("Word Problem:", StringComparison.OrdinalIgnoreCase))
            {
                ps.WordProblem = line.Substring("Word Problem:".Length).Trim();
                continue;
            }
            if (line.StartsWith("Problem:", StringComparison.OrdinalIgnoreCase))
            {
                ps.WordProblem = line.Substring("Problem:".Length).Trim();
                continue;
            }
            if (line.StartsWith("-", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (line.StartsWith("```", StringComparison.Ordinal)) continue;

            // Fix common LLM typo: "int x == 3" -> "int x = 3" (only for declarations)
            var fixedLine = Regex.Replace(line, @"\b(int|decimal|var|double|float|long|short)\s+([A-Za-z_][A-Za-z0-9_]*)\s*==\s*", "$1 $2 = ");
            
            // Fix "type f(args) = body" -> "type f(args) => body" (for function definitions using =)
            fixedLine = Regex.Replace(fixedLine, @"\b(int|decimal|var|double|float|long|short)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*=\s*(?![=])", "$1 $2($3) => ");

            cleanCodeLines.Add(fixedLine);
        }
        code = string.Join(Environment.NewLine, cleanCodeLines);
        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine($"DEBUG: ProblemScriptToProblemStruct code to parse:\n{code}");

        // Robust Parsing with Roslyn
        try
        {
            var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(kind: SourceCodeKind.Script));
            var root = tree.GetCompilationUnitRoot();

            foreach (var member in root.Members)
            {
                if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine($"DEBUG: Processing member {member.Kind()}");
                if (member is GlobalStatementSyntax globalStmt)
                {
                    ProcessStatement(globalStmt.Statement, ps);
                }
                else if (member is FieldDeclarationSyntax field)
                {
                    ProcessVariableDeclaration(field.Declaration, ps, field);
                }
                else if (member is MethodDeclarationSyntax method)
                {
                    var name = method.Identifier.ValueText;
                    var parameters = method.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToList();
                    if (method.ExpressionBody != null)
                    {
                        var body = method.ExpressionBody.Expression.NormalizeWhitespace().ToFullString();
                        ps.Constraints.Add(EnsureSemicolon($"{name}({string.Join(", ", parameters)}) == {body}"));
                        UpsertVariable(ps, name, "");
                        AddIdentifiersAsVariables(ps, method, parameters);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.LogError("ProblemScriptToProblemStructRoslynParseError", ex.Message, $"Code:\n{code}\nStack Trace: {ex.StackTrace}");
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine($"DEBUG: Roslyn parsing failed: {ex.Message}. Fallback to line-based parsing.");
            return ps;
        }

        // De-dupe
        ps.Tags = ps.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ps.Constraints = ps.Constraints.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        ps.Variables = ps.Variables.GroupBy(v => v.VariableName, StringComparer.Ordinal).Select(g =>
        {
            var bestType = g.Select(x => x.VariableType).FirstOrDefault(t => t == "int" || t == "decimal") ?? "";
            return new Variable { VariableName = g.Key, VariableType = bestType };
        }).ToList();

        return ps;
    }

    private static void ProcessVariableDeclaration(VariableDeclarationSyntax declaration, ProblemStruct ps, CSharpSyntaxNode parentNode)
    {
        var typeText = declaration.Type.ToString().Trim();
        var variableType = (typeText == "int" || typeText == "decimal") ? typeText : string.Empty;

        foreach (var v in declaration.Variables)
        {
            var name = v.Identifier.ValueText.Trim();
            if (name.Length == 0) continue;

            UpsertVariable(ps, name, variableType);

            if (v.Initializer is not null)
            {
                var rhs = v.Initializer.Value.NormalizeWhitespace().ToFullString().Trim();
                ps.Constraints.Add(EnsureSemicolon($"{name} == {rhs}"));
                AddIdentifiersAsVariables(ps, v.Initializer.Value);
            }
        }
        AddIdentifiersAsVariables(ps, parentNode);
    }

    private static void ProcessStatement(StatementSyntax stmt, ProblemStruct ps)
    {
        if (stmt is BlockSyntax block)
        {
            foreach (var s in block.Statements) ProcessStatement(s, ps);
            return;
        }

        if (stmt is EmptyStatementSyntax) return;

        if (stmt is LocalFunctionStatementSyntax localFunc)
        {
            var name = localFunc.Identifier.ValueText;
            var parameters = localFunc.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToList();
            if (localFunc.ExpressionBody != null)
            {
                var body = localFunc.ExpressionBody.Expression.NormalizeWhitespace().ToFullString();
                ps.Constraints.Add(EnsureSemicolon($"{name}({string.Join(", ", parameters)}) == {body}"));
                UpsertVariable(ps, name, "");
                AddIdentifiersAsVariables(ps, localFunc, parameters);
            }
            return;
        }

        if (stmt is LocalDeclarationStatementSyntax decl)
        {
            ProcessVariableDeclaration(decl.Declaration, ps, decl);
        }
        else if (stmt is ExpressionStatementSyntax exprStmt)
        {
            if (exprStmt.Expression is AssignmentExpressionSyntax assign &&
                (assign.Kind() == SyntaxKind.SimpleAssignmentExpression))
            {
                var left = assign.Left.NormalizeWhitespace().ToFullString().Trim();
                var right = assign.Right.NormalizeWhitespace().ToFullString().Trim();
                ps.Constraints.Add(EnsureSemicolon($"{left} == {right}"));
                AddIdentifiersAsVariables(ps, assign);
            }
            else if (exprStmt.Expression is InvocationExpressionSyntax invocation)
            {
                var text = invocation.NormalizeWhitespace().ToFullString().Trim();
                if (text.Length > 0)
                {
                    ps.Constraints.Add(EnsureSemicolon(text));
                    AddIdentifiersAsVariables(ps, invocation);
                }
            }
            else
            {
                var text = exprStmt.Expression.NormalizeWhitespace().ToFullString().Trim();
                if (text.Length > 0)
                {
                    ps.Constraints.Add(EnsureSemicolon(text));
                    AddIdentifiersAsVariables(ps, exprStmt.Expression);
                }
            }
        }
        else if (stmt is IfStatementSyntax ifStmt)
        {
            var condition = ifStmt.Condition.NormalizeWhitespace().ToFullString().Trim();
            
            var thenPs = new ProblemStruct();
            ProcessStatement(ifStmt.Statement, thenPs);
            
            if (thenPs.Constraints.Count > 0)
            {
                var thenConstraint = thenPs.Constraints.Count == 1 
                    ? thenPs.Constraints[0].TrimEnd(';')
                    : $"and({string.Join(", ", thenPs.Constraints.Select(c => c.TrimEnd(';')))})";
                
                ps.Constraints.Add(EnsureSemicolon($"implies({condition}, {thenConstraint})"));
                foreach (var v in thenPs.Variables) UpsertVariable(ps, v.VariableName, v.VariableType);
            }

            if (ifStmt.Else != null)
            {
                var elsePs = new ProblemStruct();
                ProcessStatement(ifStmt.Else.Statement, elsePs);
                
                if (elsePs.Constraints.Count > 0)
                {
                    var elseConstraint = elsePs.Constraints.Count == 1 
                        ? elsePs.Constraints[0].TrimEnd(';')
                        : $"and({string.Join(", ", elsePs.Constraints.Select(c => c.TrimEnd(';')))})";
                    
                    ps.Constraints.Add(EnsureSemicolon($"implies(not({condition}), {elseConstraint})"));
                    foreach (var v in elsePs.Variables) UpsertVariable(ps, v.VariableName, v.VariableType);
                }
            }
            AddIdentifiersAsVariables(ps, ifStmt.Condition);
        }
    }

    private static string ExtractXmlAndRemove(string input, ProblemStruct ps)
    {
        foreach (Match m in TagsRegex.Matches(input))
        {
            var inner = m.Groups.Count > 1 ? m.Groups[1].Value : "";
            foreach (var t in inner.Split(','))
            {
                var tag = t.Trim();
                if (tag.Length > 0 && !ps.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) ps.Tags.Add(tag);
            }
        }

        var noteMatches = NotesRegex.Matches(input);
        if (noteMatches.Count > 0)
        {
            var parts = new List<string>();
            foreach (Match m in noteMatches)
            {
                var inner = (m.Groups.Count > 1 ? m.Groups[1].Value : "").Trim();
                if (inner.Length > 0) parts.Add(inner);
            }
            ps.Notes = string.Join(Environment.NewLine, parts);
        }

        var scriptMatch = Regex.Match(input, @"<(?:revised_script|problem_script|ProblemScript)><!\[CDATA\[(.*?)\]\]></(?:revised_script|problem_script|ProblemScript)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (scriptMatch.Success) return scriptMatch.Groups[1].Value;

        scriptMatch = Regex.Match(input, @"<(?:revised_script|problem_script|ProblemScript)>(.*?)</(?:revised_script|problem_script|ProblemScript)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (scriptMatch.Success) return scriptMatch.Groups[1].Value;

        var stripped = TagsRegex.Replace(input, "");
        stripped = NotesRegex.Replace(stripped, "");
        stripped = Regex.Replace(stripped, @"<Options>.*?</Options>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<Rules>.*?</Rules>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<(?:revised_script|problem_script|ProblemScript)>.*?</(?:revised_script|problem_script|ProblemScript)>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        // Remove block comments
        stripped = Regex.Replace(stripped, @"/\*.*?\*/", "", RegexOptions.Singleline);

        return stripped;
    }

    private static string StripLineComment(string line)
    {
        if (line is null) return string.Empty;
        int idx = line.IndexOf("//", StringComparison.Ordinal);
        if (idx >= 0) line = line.Substring(0, idx);
        
        // Also handle block comments that might be on a single line
        line = Regex.Replace(line, @"/\*.*?\*/", "");
        
        return line;
    }

    private static string EnsureSemicolon(string s)
    {
        s = (s ?? string.Empty).TrimEnd();
        return s.EndsWith(";") ? s : (s + ";");
    }

    private static void UpsertVariable(ProblemStruct ps, string name, string typeOrEmpty)
    {
        var existing = ps.Variables.FirstOrDefault(v => string.Equals(v.VariableName, name, StringComparison.Ordinal));
        if (existing is null)
        {
            ps.Variables.Add(new Variable { VariableName = name, VariableType = typeOrEmpty ?? string.Empty });
            return;
        }
        if ((existing.VariableType != "int" && existing.VariableType != "decimal") && (typeOrEmpty == "int" || typeOrEmpty == "decimal"))
        {
            existing.VariableType = typeOrEmpty;
        }
    }

    private static void AddIdentifiersAsVariables(ProblemStruct ps, CSharpSyntaxNode node, IEnumerable<string>? excludedIdentifiers = null)
    {
        var excludedSet = excludedIdentifiers?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        
        // Auto-exclude parameters of any function/method found in this node tree
        foreach (var func in node.DescendantNodesAndSelf().OfType<LocalFunctionStatementSyntax>())
        {
            foreach (var p in func.ParameterList.Parameters) excludedSet.Add(p.Identifier.ValueText);
        }
        foreach (var meth in node.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>())
        {
            foreach (var p in meth.ParameterList.Parameters) excludedSet.Add(p.Identifier.ValueText);
        }

        if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine($"DEBUG: AddIdentifiersAsVariables node={node.GetType().Name} excluded={string.Join(",", excludedSet)}");
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        {
            "Vector", "Matrix", "Piecewise", "Sum", "Product", "Integrate", "Derive", "Limit",
            "mod", "gcd", "lcm", "pow", "sqrt", "abs", "sin", "cos", "tan", "arcsin", "arccos",
            "arctan", "exp", "ln", "log", "and", "or", "not", "implies", "iff",
            "forall", "exists", "interval", "count", "filter", "length", "min", "max",
            "minimize", "maximize", "isinteger", "real", "realnumber", "integer"
        };

        foreach (var id in node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (IsMemberName(id)) continue;
            // EXPLICIT CHECK: If this identifier is part of a ParameterSyntax, it is NOT a variable to harvest.
            if (id.Parent is ParameterSyntax) continue;

            var name = id.Identifier.ValueText.Trim();
            if (name.Length == 0 || excludedSet.Contains(name)) continue;
            if (IsInvocationTarget(id) && reserved.Contains(name)) continue;
            if (System.Environment.GetEnvironmentVariable("SYM_DEBUG_SOLVE") == "1") Console.WriteLine($"DEBUG: Harvesting variable: {name} from {id.Parent?.GetType().Name}");
            UpsertVariable(ps, name, string.Empty);
        }

        foreach (var inv in node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is IdentifierNameSyntax id && !reserved.Contains(id.Identifier.ValueText))
            {
                bool allConstant = true;
                foreach (var arg in inv.ArgumentList.Arguments)
                {
                    if (arg.Expression is not LiteralExpressionSyntax && !(arg.Expression is InvocationExpressionSyntax subInv && IsConstantInvocation(subInv, reserved)))
                    {
                        allConstant = false;
                        break;
                    }
                }
                if (allConstant)
                {
                    var display = inv.NormalizeWhitespace().ToFullString().Trim();
                    if (!excludedSet.Contains(display)) UpsertVariable(ps, display, string.Empty);
                }
            }
        }
    }

    private static bool IsConstantInvocation(InvocationExpressionSyntax inv, HashSet<string> reserved)
    {
        if (inv.Expression is not IdentifierNameSyntax id || reserved.Contains(id.Identifier.ValueText)) return false;
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (arg.Expression is not LiteralExpressionSyntax && !(arg.Expression is InvocationExpressionSyntax subInv && IsConstantInvocation(subInv, reserved))) return false;
        }
        return true;
    }

    private static bool IsInvocationTarget(IdentifierNameSyntax id) => id.Parent is InvocationExpressionSyntax inv && inv.Expression == id;
    private static bool IsMemberName(IdentifierNameSyntax id) => id.Parent is MemberAccessExpressionSyntax ma && ma.Name == id;
}
