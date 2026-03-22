// Copyright Warren Harding 2026
using Sym.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sym.CSharpIO
{
    public sealed class CSharpProgram
    {
        public IReadOnlyList<IExpression> Expressions { get; }

        public IReadOnlyList<Rule> Rules { get; }

        public IReadOnlyList<CSharpDiagnostic> Diagnostics { get; }

        public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        public CSharpProgram(
            IReadOnlyList<IExpression> expressions,
            IReadOnlyList<Rule> rules,
            IReadOnlyList<CSharpDiagnostic> diagnostics)
        {
            Expressions = expressions ?? throw new ArgumentNullException(nameof(expressions));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }
    }

    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class CSharpDiagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public int? Line { get; }
        public int? Column { get; }

        public CSharpDiagnostic(DiagnosticSeverity severity, string message, int? line = null, int? column = null)
        {
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Line = line;
            Column = column;
        }
    }
}

