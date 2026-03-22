// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SymSolvers.CSharpAnalysis
{
    internal static class CSharpSecurityFlowCore
    {
        private static readonly IReadOnlyDictionary<SecurityFlowKind, string> BugIdBySinkKind =
            new Dictionary<SecurityFlowKind, string>
            {
                [SecurityFlowKind.CommandInjection] = "CSSEC003",
                [SecurityFlowKind.PathTraversal] = "CSSEC004",
                [SecurityFlowKind.SqlInjection] = "CSSEC031",
                [SecurityFlowKind.LdapInjection] = "CSSEC032",
                [SecurityFlowKind.XpathInjection] = "CSSEC033",
                [SecurityFlowKind.RedirectInjection] = "CSSEC034",
                [SecurityFlowKind.HeaderInjection] = "CSSEC035",
                [SecurityFlowKind.TemplateInjection] = "CSSEC036",
                [SecurityFlowKind.WeakRandomness] = "CSSEC037",
                [SecurityFlowKind.HardcodedSecret] = "CSSEC038"
            };

        private static readonly IReadOnlyDictionary<string, SecurityFlowKind> SinkKindByBugId =
            BugIdBySinkKind.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);

        // Memory note: adding a new sink guard requires coordinated updates in:
        // - CSharpGuardRuleLibrary (cs_guard_* derivation),
        // - CSharpGuardProver.TryMapGuardHead (head -> CSharpGuardKind),
        // - this table (sink kind -> required guard).
        private static readonly IReadOnlyDictionary<SecurityFlowKind, CSharpGuardKind> RequiredGuardBySinkKind =
            new Dictionary<SecurityFlowKind, CSharpGuardKind>
            {
                [SecurityFlowKind.CommandInjection] = CSharpGuardKind.CommandAllowlisted,
                [SecurityFlowKind.PathTraversal] = CSharpGuardKind.PathValidated,
                [SecurityFlowKind.SqlInjection] = CSharpGuardKind.SqlValidated,
                [SecurityFlowKind.LdapInjection] = CSharpGuardKind.LdapFilterValidated,
                [SecurityFlowKind.XpathInjection] = CSharpGuardKind.XpathValidated,
                [SecurityFlowKind.RedirectInjection] = CSharpGuardKind.RedirectAllowlisted,
                [SecurityFlowKind.HeaderInjection] = CSharpGuardKind.HeaderValidated,
                [SecurityFlowKind.TemplateInjection] = CSharpGuardKind.TemplateTrusted
            };

        private static readonly string[] StrongAiMethodNameTokens =
        {
            "InvokePrompt",
            "GetChatMessageContent",
            "GetResponse",
            "CreateChatCompletion",
            "GetCompletion",
            "GetCompletions",
            "GenerateContent"
        };

        private static readonly string[] WeakAiMethodNameTokens =
        {
            "Query",
            "Search",
            "Complete",
            "Generate"
        };

        private static readonly string[] PromptParameterTokens =
        {
            "prompt",
            "message",
            "messages",
            "chat",
            "instruction",
            "question",
            "usermessage",
            "systemmessage",
            "assistantreply"
        };

        private static readonly string[] AiContainerTokens =
        {
            "llm",
            "openai",
            "openrouter",
            "semantic",
            "kernel",
            "chatcompletion",
            "chatclient",
            "copilot",
            "assistant",
            "langchain",
            "anthropic",
            "generative"
        };

        public static string MapBugId(SecurityFlowKind kind) =>
            BugIdBySinkKind.TryGetValue(kind, out var bugId) ? bugId : "CSSEC999";

        public static bool TryMapBugIdToSinkKind(string bugId, out SecurityFlowKind kind) =>
            SinkKindByBugId.TryGetValue(bugId, out kind);

        public static bool TryGetRequiredGuardKind(SecurityFlowKind sinkKind, out CSharpGuardKind guardKind) =>
            RequiredGuardBySinkKind.TryGetValue(sinkKind, out guardKind);

        public static bool GuardAppliesToSink(CSharpGuardKind guardKind, SecurityFlowKind sinkKind) =>
            TryGetRequiredGuardKind(sinkKind, out var required) && required == guardKind;

        public static bool IsExternalUntrustedSource(SourceKind kind) =>
            kind is SourceKind.UserSource or SourceKind.AiSource;

        public static SourceKind MergeSourceKind(SourceKind left, SourceKind right) =>
            GetSourcePriority(left) <= GetSourcePriority(right) ? left : right;

        public static bool LooksLikeAiSource(IMethodSymbol method)
        {
            var methodSignal = GetAiMethodSignal(method.Name);
            if (methodSignal == AiMethodSignal.None)
            {
                return false;
            }

            // Keep this heuristic centralized so both intra/interprocedural analyzers stay in sync.
            var hasContainerSignal =
                ContainsAiSignal(method.ContainingType?.ToDisplayString()) ||
                ContainsAiSignal(method.ContainingNamespace?.ToDisplayString()) ||
                ContainsAiSignal(method.ContainingAssembly?.Name);
            var hasPromptLikeInput = HasPromptLikeInput(method);
            var returnsTextLike = ReturnsTextLike(method.ReturnType);

            if (methodSignal == AiMethodSignal.Strong)
            {
                return hasContainerSignal || hasPromptLikeInput || returnsTextLike;
            }

            // Weak names like Query/Search need corroboration to avoid over-classifying non-AI APIs.
            if (hasContainerSignal && (hasPromptLikeInput || returnsTextLike))
            {
                return true;
            }

            return hasPromptLikeInput && returnsTextLike;
        }

        public static bool IsStringPropagationMethod(IMethodSymbol method)
        {
            var typeName = method.ContainingType?.ToDisplayString();
            if (typeName != "System.String" && typeName != "string")
            {
                return false;
            }

            // Propagate taint through common string transformations.
            // Keep this list conservative: only include methods that return string (or object coercions we want to treat as string).
            if (method.ReturnType.SpecialType != SpecialType.System_String)
            {
                return method.Name is "Format" or "Concat" or "Join";
            }

            return method.Name is
                "Format" or
                "Concat" or
                "Join" or
                "Replace" or
                "Substring" or
                "Trim" or
                "TrimStart" or
                "TrimEnd" or
                "ToLower" or
                "ToLowerInvariant" or
                "ToUpper" or
                "ToUpperInvariant" or
                "Remove" or
                "Insert" or
                "PadLeft" or
                "PadRight";
        }

        public static bool IsSanitizer(IMethodSymbol method, IEnumerable<SanitizerSpec> sanitizers)
        {
            var typeName = method.ContainingType?.ToDisplayString();
            return sanitizers.Any(s =>
                TypeNameMatches(typeName, s.TypeName) &&
                string.Equals(s.MethodName, method.Name, StringComparison.Ordinal) &&
                s.ReturnsSanitized);
        }

        public static bool TypeNameMatches(string? actual, string pattern)
        {
            if (string.IsNullOrEmpty(actual))
            {
                return false;
            }

            if (actual == pattern)
            {
                return true;
            }

            return actual.EndsWith("." + pattern, StringComparison.Ordinal);
        }

        public static string GetLocationString(IOperation operation)
        {
            var loc = operation.Syntax.GetLocation().GetLineSpan();
            return $"{loc.Path}:{loc.StartLinePosition.Line + 1}";
        }

        public static bool BranchDefinitelyExits(IOperation? operation)
        {
            if (operation is null)
            {
                return false;
            }

            if (operation is IReturnOperation || operation.Kind == OperationKind.Throw)
            {
                return true;
            }

            if (operation is IBlockOperation block && block.Operations.Length > 0)
            {
                return BranchDefinitelyExits(block.Operations[^1]);
            }

            if (operation is IConditionalOperation conditional)
            {
                return BranchDefinitelyExits(conditional.WhenTrue) &&
                       BranchDefinitelyExits(conditional.WhenFalse);
            }

            return false;
        }

        public static List<CSharpGuardFact> IntersectGuards(
            IEnumerable<CSharpGuardFact> left,
            IEnumerable<CSharpGuardFact> right)
        {
            var rightByKey = right
                .GroupBy(f => $"{f.Kind}|{f.Subject}", StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.Strength).First(),
                    StringComparer.Ordinal);

            var intersected = new List<CSharpGuardFact>();
            foreach (var fact in left)
            {
                var key = $"{fact.Kind}|{fact.Subject}";
                if (!rightByKey.TryGetValue(key, out var rightFact))
                {
                    continue;
                }

                var strength = fact.Strength <= rightFact.Strength ? fact.Strength : rightFact.Strength;
                var evidence = string.IsNullOrWhiteSpace(fact.Evidence) ? rightFact.Evidence : fact.Evidence;
                intersected.Add(new CSharpGuardFact(fact.Kind, fact.Subject, strength, evidence));
            }

            return intersected
                .GroupBy(f => $"{f.Kind}|{f.Subject}", StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(x => x.Strength).First())
                .ToList();
        }

        public static List<string> TrimEvidence(IReadOnlyList<string> evidence, int maxSteps)
        {
            if (evidence.Count == 0)
            {
                return new List<string>();
            }

            if (maxSteps <= 0 || evidence.Count <= maxSteps)
            {
                return evidence.ToList();
            }

            var keepHeadCount = Math.Max(1, maxSteps - 1);
            var trimmed = evidence.Take(keepHeadCount).ToList();
            trimmed.Add($"... truncated {evidence.Count - keepHeadCount} additional step(s).");
            return trimmed;
        }

        public static CSharpMathBugSeverity ParseSeverity(string severity) => severity switch
        {
            "Error" => CSharpMathBugSeverity.Error,
            "Warning" => CSharpMathBugSeverity.Warning,
            "Info" => CSharpMathBugSeverity.Info,
            _ => CSharpMathBugSeverity.Warning
        };

        private static int GetSourcePriority(SourceKind kind) => kind switch
        {
            SourceKind.AiSource => 0,
            SourceKind.UserSource => 1,
            SourceKind.InternalSource => 2,
            _ => 3
        };

        private enum AiMethodSignal
        {
            None = 0,
            Weak = 1,
            Strong = 2
        }

        private static AiMethodSignal GetAiMethodSignal(string methodName)
        {
            foreach (var token in StrongAiMethodNameTokens)
            {
                if (methodName.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return AiMethodSignal.Strong;
                }
            }

            foreach (var token in WeakAiMethodNameTokens)
            {
                if (methodName.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return AiMethodSignal.Weak;
                }
            }

            return AiMethodSignal.None;
        }

        private static bool ContainsAiSignal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (var token in AiContainerTokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPromptLikeInput(IMethodSymbol method)
        {
            foreach (var parameter in method.Parameters)
            {
                if (ContainsPromptToken(parameter.Name) ||
                    ContainsPromptToken(parameter.Type?.ToDisplayString()))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPromptToken(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (var token in PromptParameterTokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReturnsTextLike(ITypeSymbol? returnType)
        {
            if (returnType is null)
            {
                return false;
            }

            if (returnType.SpecialType == SpecialType.System_String)
            {
                return true;
            }

            if (returnType is INamedTypeSymbol named &&
                named.IsGenericType &&
                named.TypeArguments.Length == 1 &&
                named.TypeArguments[0].SpecialType == SpecialType.System_String)
            {
                var original = named.OriginalDefinition.ToDisplayString();
                return original is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>";
            }

            return false;
        }
    }
}
