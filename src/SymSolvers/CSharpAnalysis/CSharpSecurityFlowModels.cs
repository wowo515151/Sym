using System;
using System.Collections.Generic;

namespace SymSolvers.CSharpAnalysis
{
    public enum SecurityFlowKind
    {
        CommandInjection,
        PathTraversal,
        SqlInjection,
        LdapInjection,
        XpathInjection,
        RedirectInjection,
        HeaderInjection,
        TemplateInjection,
        WeakRandomness,
        HardcodedSecret
    }

    public enum SourceKind
    {
        // SourceKind precedence is centralized in CSharpSecurityFlowCore.MergeSourceKind;
        // keep both flow analyzers using that helper instead of enum ordinals.
        AiSource,        // LLM/model output or AI-generated text
        UserSource,      // External unverified input (e.g., Request.Query)
        InternalSource,  // Internal but potentially untrusted (e.g., Database, File)
        PublicParameter  // Public API parameter (legacy behavior)
    }

    public record SourceSpec(
        string TypeName,
        string MethodName,
        string[]? TaintedReturnProperties = null, // e.g., "Query" on generic Request object
        bool IsProperty = false,
        SourceKind Kind = SourceKind.UserSource
    );

    public record SinkSpec(
        string TypeName,
        string MethodName,
        int[] TaintedIndices,
        SecurityFlowKind Kind,
        string Description
    );

    public record SanitizerSpec(
        string TypeName,
        string MethodName,
        int[] SanitizedIndices, // Which args are sanitized?
        bool ReturnsSanitized // Does it return a clean string?
    );

    public record PropagationSpec(
        string TypeName,
        string MethodName,
        int[] InputIndices,
        bool PropagatesToReturn
    );

    public record TaintTraceStep(
        string Location, // File:Line
        string Description, // "Source: Request.Query['id']" or "Assigned to 'cmd'"
        string Symbol, // The variable name or expression
        TaintTraceStep? Previous = null,
        SourceKind Kind = SourceKind.UserSource
    );
}
