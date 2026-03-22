// Copyright Warren Harding 2026
using System;

namespace SymSolvers.CSharpAnalysis
{
    public enum CSharpNumericType
    {
        Unknown,
        I32,
        U32,
        I64,
        U64,
        F32,
        F64,
        Decimal,
        NativeInt, // nint
        NativeUInt // nuint
    }

    public enum CSharpOverflowContext
    {
        Unspecified,
        Checked,
        Unchecked
    }

    public sealed record CSharpExpressionMetadata(
        CSharpNumericType TypeKind,
        CSharpOverflowContext OverflowContext,
        string FilePath,
        int Line,
        int Column,
        string OriginalText,
        object? ConstantValue
    );
}
