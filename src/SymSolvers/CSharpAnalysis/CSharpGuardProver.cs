using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Sym.Core;
using Sym.Core.EGraph;

namespace SymSolvers.CSharpAnalysis
{
    internal enum CSharpGuardStrength
    {
        Medium = 1,
        Strong = 2
    }

    // Memory note: sink-facing guard kinds must stay aligned with
    // CSharpSecurityFlowCore.RequiredGuardBySinkKind and CSharpGuardRuleLibrary cs_guard_* rules.
    internal enum CSharpGuardKind
    {
        NonZero,
        NotNull,
        PathValidated,
        CommandAllowlisted,
        SqlValidated,
        LdapFilterValidated,
        XpathValidated,
        RedirectAllowlisted,
        HeaderValidated,
        TemplateTrusted,
        NonNegative,
        NonPositive,
        InInt32Range,
        InRangeIdiom,
        IsConstantTimeEquivalent,
        SafeModuloResult,
        SafeUnsignedWrap,
        ValidShiftAmount
    }

    internal sealed record CSharpGuardFact(
        CSharpGuardKind Kind,
        string Subject,
        CSharpGuardStrength Strength,
        string Evidence);

    internal sealed class CSharpGuardProver
    {
        private readonly IReadOnlyList<Rule> _rules;
        private readonly CSharpSemanticLowerer _lowerer;
        private readonly CSharpMathBugAnalyzerOptions _options;

        public CSharpGuardProver(CSharpMathBugAnalyzerOptions? options = null)
            : this(CSharpGuardRuleLibrary.GetRules(), options)
        {
        }

        public CSharpGuardProver(IReadOnlyList<Rule> rules, CSharpMathBugAnalyzerOptions? options = null)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _lowerer = new CSharpSemanticLowerer();
            _options = options ?? CSharpMathBugAnalyzerOptions.Default;
        }

        public IReadOnlyList<CSharpGuardFact> DeriveBranchFacts(
            IOperation? condition,
            bool branchTaken,
            SemanticModel semanticModel,
            List<string>? diagnostics = null,
            CancellationToken ct = default)
        {
            if (!_options.EnableGuardProver || condition is null)
            {
                return Array.Empty<CSharpGuardFact>();
            }

            var facts = new List<CSharpGuardFact>();
            CollectBranchFacts(condition, branchTaken, semanticModel, facts, diagnostics, ct);
            return facts
                .GroupBy(f => $"{f.Kind}|{f.Subject}", StringComparer.Ordinal)
                .Select(g => g.First())
                .Take(_options.GuardMaxFacts)
                .ToList();
        }

        public IReadOnlyList<CSharpGuardFact> DeriveAllowlistSanitizerBranchFacts(
            IOperation? condition,
            bool branchTaken,
            SemanticModel semanticModel,
            IReadOnlyList<SanitizerSpec> sanitizers,
            List<string>? diagnostics = null,
            CancellationToken ct = default)
        {
            if (!_options.EnableGuardProver || condition is null || sanitizers.Count == 0)
            {
                return Array.Empty<CSharpGuardFact>();
            }

            var facts = new List<CSharpGuardFact>();
            CollectAllowlistSanitizerFacts(condition, branchTaken, semanticModel, sanitizers, facts, diagnostics, ct);
            return facts
                .GroupBy(f => $"{f.Kind}|{f.Subject}", StringComparer.Ordinal)
                .Select(g => g.First())
                .Take(_options.GuardMaxFacts)
                .ToList();
        }

        private void DeriveTypeFacts(IOperation operation, SemanticModel semanticModel, List<CSharpGuardFact> results, CancellationToken ct)
        {
            if (operation.Type != null)
            {
                var special = operation.Type.SpecialType;
                if (special == SpecialType.System_UInt16 || 
                    special == SpecialType.System_UInt32 || 
                    special == SpecialType.System_UInt64 ||
                    special == SpecialType.System_Byte ||
                    operation.Type.Name == "UIntPtr")
                {
                    AddManualFact(CSharpGuardKind.NonNegative, operation, "Type is unsigned", semanticModel, results, ct);
                }
            }

            foreach (var child in operation.ChildOperations)
            {
                DeriveTypeFacts(child, semanticModel, results, ct);
            }
        }

        private void DeriveAssertionFacts(IOperation expression, SemanticModel semanticModel, List<CSharpGuardFact> results, List<string>? diagnostics, CancellationToken ct)
        {
            // Walk up to the containing block and look for preceding assertions
            IOperation? current = expression;
            while (current != null && current is not IBlockOperation)
            {
                current = current.Parent;
            }

            if (current is IBlockOperation block)
            {
                foreach (var op in block.Operations)
                {
                    if (op == expression || op.Syntax.SpanStart >= expression.Syntax.SpanStart)
                    {
                        break;
                    }

                    // Look for Debug.Assert(condition, ...) or CryptoUtil.Assert(condition, ...)
                    IOperation? statement = op;
                    if (statement is IExpressionStatementOperation exprStmt)
                    {
                        statement = exprStmt.Operation;
                    }

                    if (statement is IInvocationOperation invocation)
                    {
                        if (LooksLikeAssertionCall(invocation))
                        {
                            if (invocation.Arguments.Length > 0)
                            {
                                var condition = invocation.Arguments[0].Value;
                                CollectBranchFacts(condition, true, semanticModel, results, diagnostics, ct);
                            }
                        }
                    }

                    // Fallback when the assert call doesn't bind cleanly (e.g., missing framework references):
                    // detect Debug.Assert(...) / CryptoUtil.Assert(...) from syntax and re-bind only the condition argument.
                    if (op.Syntax is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invSyntax } &&
                        LooksLikeAssertionInvocation(invSyntax) &&
                        invSyntax.ArgumentList.Arguments.Count > 0)
                    {
                        var conditionSyntax = invSyntax.ArgumentList.Arguments[0].Expression;
                        var conditionOp = semanticModel.GetOperation(conditionSyntax, ct);
                        if (conditionOp != null)
                        {
                            CollectBranchFacts(conditionOp, true, semanticModel, results, diagnostics, ct);
                        }
                    }
                }
            }
        }

        private static bool LooksLikeAssertionCall(IInvocationOperation invocation)
        {
            // Prefer semantic identification when available.
            // Note: In analyzer scenarios, framework references can be incomplete; fall back to syntax-based matching.
            var methodName = invocation.TargetMethod?.ToDisplayString() ?? string.Empty;
            if (!string.IsNullOrEmpty(methodName))
            {
                if (methodName.Contains("Debug.Assert", StringComparison.Ordinal) ||
                    methodName.Contains("System.Diagnostics.Debug.Assert", StringComparison.Ordinal) ||
                    methodName.Contains("CryptoUtil.Assert", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (invocation.Syntax is not InvocationExpressionSyntax invSyntax)
            {
                return false;
            }

            // Syntax-based fallback: Debug.Assert(...) / System.Diagnostics.Debug.Assert(...)
            if (invSyntax.Expression is MemberAccessExpressionSyntax member)
            {
                if (!string.Equals(member.Name.Identifier.ValueText, "Assert", StringComparison.Ordinal))
                {
                    return false;
                }

                var target = member.Expression.ToString();
                if (string.Equals(target, "Debug", StringComparison.Ordinal) ||
                    target.EndsWith(".Debug", StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(target, "CryptoUtil", StringComparison.Ordinal) ||
                    target.EndsWith(".CryptoUtil", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeAssertionInvocation(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
            {
                return false;
            }

            if (!string.Equals(member.Name.Identifier.ValueText, "Assert", StringComparison.Ordinal))
            {
                return false;
            }

            var target = member.Expression.ToString();
            return string.Equals(target, "Debug", StringComparison.Ordinal) ||
                   target.EndsWith(".Debug", StringComparison.Ordinal) ||
                   string.Equals(target, "CryptoUtil", StringComparison.Ordinal) ||
                   target.EndsWith(".CryptoUtil", StringComparison.Ordinal);
        }

        public IReadOnlyList<CSharpGuardFact> DeriveExpressionFacts(
            IOperation? expression,
            SemanticModel semanticModel,
            List<string>? diagnostics = null,
            CancellationToken ct = default)
        {
            if (!_options.EnableGuardProver || expression is null)
            {
                return Array.Empty<CSharpGuardFact>();
            }

            var results = new List<CSharpGuardFact>();
            // Order matters: prefer explicit guards/assertions over generic type-derived facts.
            // Deduplication keeps the first fact per (Kind, Subject).
            DeriveAssertionFacts(expression, semanticModel, results, diagnostics, ct);
            results.AddRange(DeriveAtomicFacts(expression, true, semanticModel, diagnostics, ct));
            DeriveSemanticNonNegativeFacts(expression, semanticModel, results, ct);
            DeriveSemanticInt32RangeFacts(expression, semanticModel, results, ct);
            DeriveTypeFacts(expression, semanticModel, results, ct);

            return results
                .GroupBy(f => $"{f.Kind}|{f.Subject}", StringComparer.Ordinal)
                .Select(g => g.First())
                .Take(_options.GuardMaxFacts)
                .ToList();
        }

        private void DeriveSemanticNonNegativeFacts(
            IOperation expression,
            SemanticModel semanticModel,
            List<CSharpGuardFact> results,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = semanticModel.Compilation;
            if (compilation is null)
            {
                return;
            }

            var enclosingMethod = semanticModel.GetEnclosingSymbol(expression.Syntax.SpanStart, ct) as IMethodSymbol;
            if (enclosingMethod is null)
            {
                return;
            }

            // Walk the expression subtree and add conservative non-negative facts that require semantic context.
            var stack = new Stack<IOperation>();
            stack.Push(expression);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = stack.Pop();

                switch (current)
                {
                    case IFieldReferenceOperation fieldRef:
                        if (CSharpInductiveNonNegative.IsFieldInductivelyNonNegative(fieldRef.Field, compilation, ct))
                        {
                            AddManualFact(CSharpGuardKind.NonNegative, fieldRef, "Inductively non-negative field", semanticModel, results, ct);
                        }
                        break;
                    case IPropertyReferenceOperation propRef:
                        if (propRef.Property.Name == "Length" || CSharpInductiveNonNegative.IsPropertyInductivelyNonNegative(propRef.Property, compilation, ct))
                        {
                            AddManualFact(CSharpGuardKind.NonNegative, propRef, "Inductively non-negative property", semanticModel, results, ct);
                        }
                        break;
                    case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } add:
                        if (TryProveNonNegativeAdd(add, expression.Syntax, enclosingMethod, semanticModel, compilation, ct))
                        {
                            AddManualFact(CSharpGuardKind.NonNegative, add, "Non-negative add (local guards + invariants)", semanticModel, results, ct);
                        }
                        break;
                }

                foreach (var child in current.ChildOperations)
                {
                    stack.Push(child);
                }
            }
        }

        private void DeriveSemanticInt32RangeFacts(
            IOperation expression,
            SemanticModel semanticModel,
            List<CSharpGuardFact> results,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = semanticModel.Compilation;
            if (compilation is null)
            {
                return;
            }

            var enclosingMethod = semanticModel.GetEnclosingSymbol(expression.Syntax.SpanStart, ct) as IMethodSymbol;
            if (enclosingMethod is null)
            {
                return;
            }

            // Walk the expression subtree and add conservative range facts that require semantic context.
            var stack = new Stack<IOperation>();
            stack.Push(expression);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = stack.Pop();

                if (current is IPropertyReferenceOperation propRef &&
                    IsMemoryStreamLengthOrPosition(propRef, compilation))
                {
                    AddManualFact(CSharpGuardKind.InInt32Range, propRef, "MemoryStream Length/Position fits int", semanticModel, results, ct);
                    AddManualFact(CSharpGuardKind.NonNegative, propRef, "MemoryStream Length/Position is non-negative", semanticModel, results, ct);
                }

                if (current is IInvocationOperation invocation &&
                    invocation.TargetMethod?.ContainingType?.Name == "Math")
                {
                    if (TryProveMathClampToIntMax(invocation, out var producesNonNegative, expression.Syntax, enclosingMethod, semanticModel, compilation, ct))
                    {
                        AddManualFact(CSharpGuardKind.InInt32Range, invocation, "Clamp to int.MaxValue (semantic)", semanticModel, results, ct);
                        if (producesNonNegative)
                        {
                            AddManualFact(CSharpGuardKind.NonNegative, invocation, "Clamp result is non-negative (semantic)", semanticModel, results, ct);
                        }
                    }
                }

                foreach (var child in current.ChildOperations)
                {
                    stack.Push(child);
                }
            }
        }

        private static bool IsMemoryStreamLengthOrPosition(IPropertyReferenceOperation propRef, Compilation compilation)
        {
            if (propRef.Property is null)
            {
                return false;
            }

            // Only treat these well-known MemoryStream properties as int-range.
            if (!string.Equals(propRef.Property.Name, "Length", StringComparison.Ordinal) &&
                !string.Equals(propRef.Property.Name, "Position", StringComparison.Ordinal))
            {
                return false;
            }

            // Must be on a MemoryStream-typed receiver.
            var instanceType = propRef.Instance?.Type;
            if (instanceType is null)
            {
                return false;
            }

            var memoryStream = compilation.GetTypeByMetadataName("System.IO.MemoryStream");
            if (memoryStream is null)
            {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(instanceType, memoryStream))
            {
                return true;
            }

            return IsOrDerivedFrom(instanceType, memoryStream);
        }

        private static bool IsOrDerivedFrom(ITypeSymbol type, INamedTypeSymbol baseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, baseType))
            {
                return true;
            }

            // Only the class inheritance chain matters here (MemoryStream is a class).
            var current = type.BaseType;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static bool TryProveMathClampToIntMax(
            IInvocationOperation invocation,
            out bool producesNonNegative,
            SyntaxNode bugSiteSyntax,
            IMethodSymbol enclosingMethod,
            SemanticModel semanticModel,
            Compilation compilation,
            CancellationToken ct)
        {
            producesNonNegative = false;
            ct.ThrowIfCancellationRequested();

            if (invocation.TargetMethod is null)
            {
                return false;
            }

            // Pattern 1: Math.Clamp(x, 0, int.MaxValue) is always safe for narrowing to int.
            if (string.Equals(invocation.TargetMethod.Name, "Clamp", StringComparison.Ordinal) && invocation.Arguments.Length == 3)
            {
                var minArg = invocation.Arguments[1].Value;
                var maxArg = invocation.Arguments[2].Value;
                if (IsConstantZero(minArg) && IsConstantIntMax(maxArg))
                {
                    producesNonNegative = true;
                    return true;
                }
            }

            // Pattern 2: Math.Min(x, int.MaxValue) is safe for narrowing to int if x is provably non-negative.
            if (string.Equals(invocation.TargetMethod.Name, "Min", StringComparison.Ordinal) && invocation.Arguments.Length == 2)
            {
                var left = invocation.Arguments[0].Value;
                var right = invocation.Arguments[1].Value;

                if (IsConstantIntMax(left) && TryProveOperandNonNegative(right, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct))
                {
                    producesNonNegative = true;
                    return true;
                }

                if (IsConstantIntMax(right) && TryProveOperandNonNegative(left, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct))
                {
                    producesNonNegative = true;
                    return true;
                }
            }

            return false;
        }

        private static bool IsConstantZero(IOperation operation)
        {
            if (!operation.ConstantValue.HasValue || operation.ConstantValue.Value is null)
            {
                return false;
            }

            try
            {
                return Convert.ToDecimal(operation.ConstantValue.Value) == 0m;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsConstantIntMax(IOperation operation)
        {
            if (!operation.ConstantValue.HasValue || operation.ConstantValue.Value is null)
            {
                return false;
            }

            try
            {
                return Convert.ToDecimal(operation.ConstantValue.Value) == 2147483647m;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryProveNonNegativeAdd(
            IBinaryOperation add,
            SyntaxNode bugSiteSyntax,
            IMethodSymbol enclosingMethod,
            SemanticModel semanticModel,
            Compilation compilation,
            CancellationToken ct)
        {
            // Avoid proving arbitrary sums; this is intended for modulo-index dividends like (head + index).
            // We keep it conservative and require both operands to be provably non-negative via local checks.
            return TryProveOperandNonNegative(add.LeftOperand, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct)
                && TryProveOperandNonNegative(add.RightOperand, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct);
        }

        private static bool TryProveOperandNonNegative(
            IOperation operand,
            SyntaxNode bugSiteSyntax,
            IMethodSymbol enclosingMethod,
            SemanticModel semanticModel,
            Compilation compilation,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (operand.Type != null)
            {
                var special = operand.Type.SpecialType;
                if (special is SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_Byte || operand.Type.Name == "UIntPtr")
                {
                    return true;
                }
            }

            if (operand.ConstantValue.HasValue && operand.ConstantValue.Value is not null)
            {
                try
                {
                    return Convert.ToDecimal(operand.ConstantValue.Value) >= 0m;
                }
                catch
                {
                    // Ignore conversion failures.
                }
            }

            if (operand is IConversionOperation conv)
            {
                return TryProveOperandNonNegative(conv.Operand, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct);
            }

            if (operand is IInvocationOperation invocation)
            {
                var method = invocation.TargetMethod;
                if (method != null && CSharpInductiveNonNegative.IsMethodInductivelyNonNegative(method, compilation, ct))
                {
                    return true;
                }
            }

            if (operand is ILocalReferenceOperation local)
            {
                if (IsLocalForLoopIndexFromNonNegativeInitializer(local.Local, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct))
                {
                    return true;
                }

                return TryProveLocalNonNegativeFromDominatingAssignment(local.Local, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct);
            }

            if (operand is IParameterReferenceOperation param)
            {
                return HasDominatingNonNegativeGuardForParameter(param.Parameter, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct);
            }

            if (operand is IFieldReferenceOperation field)
            {
                return CSharpInductiveNonNegative.IsFieldInductivelyNonNegative(field.Field, compilation, ct);
            }

            if (operand is IPropertyReferenceOperation prop)
            {
                if (prop.Property.Name == "Length")
                {
                    return true;
                }

                return CSharpInductiveNonNegative.IsPropertyInductivelyNonNegative(prop.Property, compilation, ct);
            }

            return false;
        }

        private static bool TryProveLocalNonNegativeFromDominatingAssignment(
            ILocalSymbol local,
            SyntaxNode bugSiteSyntax,
            IMethodSymbol enclosingMethod,
            SemanticModel semanticModel,
            Compilation compilation,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // 1) Prefer the local's initializer when available.
            foreach (var decl in local.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (decl.GetSyntax(ct) is VariableDeclaratorSyntax vds && vds.Initializer is not null)
                {
                    var initOp = semanticModel.GetOperation(vds.Initializer.Value, ct);
                    if (initOp != null && TryProveOperandNonNegative(initOp, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct))
                    {
                        return true;
                    }
                }
            }

            // 2) Otherwise, scan a short window of dominating statements in the enclosing block.
            if (!TryGetStatementInEnclosingBlock(bugSiteSyntax, out var block, out var statement))
            {
                return false;
            }

            var statementIndex = block.Statements.IndexOf(statement);
            if (statementIndex < 0)
            {
                return false;
            }

            const int MaxToScan = 16;
            var toScan = Math.Min(statementIndex, MaxToScan);
            for (var i = 1; i <= toScan; i++)
            {
                ct.ThrowIfCancellationRequested();
                var prev = block.Statements[statementIndex - i];

                if (prev is LocalDeclarationStatementSyntax localDecl)
                {
                    foreach (var variable in localDecl.Declaration.Variables)
                    {
                        ct.ThrowIfCancellationRequested();
                        var declaredSym = semanticModel.GetDeclaredSymbol(variable, ct);
                        if (declaredSym is not ILocalSymbol declaredLocal || !SymbolEqualityComparer.Default.Equals(declaredLocal, local))
                        {
                            continue;
                        }

                        if (variable.Initializer is null)
                        {
                            continue;
                        }

                        var valueOp = semanticModel.GetOperation(variable.Initializer.Value, ct);
                        if (valueOp != null && TryProveOperandNonNegative(valueOp, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct))
                        {
                            return true;
                        }
                    }
                }

                if (prev is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assign } &&
                    assign.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                    assign.Left is IdentifierNameSyntax leftId)
                {
                    var lhsSym = semanticModel.GetSymbolInfo(leftId, ct).Symbol;
                    if (lhsSym is ILocalSymbol lhsLocal && SymbolEqualityComparer.Default.Equals(lhsLocal, local))
                    {
                        var rhsOp = semanticModel.GetOperation(assign.Right, ct);
                        if (rhsOp != null && TryProveOperandNonNegative(rhsOp, bugSiteSyntax, enclosingMethod, semanticModel, compilation, ct))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool HasDominatingNonNegativeGuardForParameter(
            IParameterSymbol parameter,
            SyntaxNode bugSiteSyntax,
            IMethodSymbol enclosingMethod,
            SemanticModel semanticModel,
            Compilation compilation,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!TryGetStatementInEnclosingBlock(bugSiteSyntax, out var block, out var statement))
            {
                return false;
            }

            var statementIndex = block.Statements.IndexOf(statement);
            if (statementIndex < 0)
            {
                return false;
            }

            var maxToScan = Math.Min(statementIndex, 12);
            for (var i = 1; i <= maxToScan; i++)
            {
                ct.ThrowIfCancellationRequested();
                var prev = block.Statements[statementIndex - i];

                if (prev is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invSyntax })
                {
                    if (InvocationEnforcesParamNonNegative(invSyntax, parameter, compilation, semanticModel, ct))
                    {
                        return true;
                    }
                }

                if (prev is IfStatementSyntax ifStmt)
                {
                    if (IfThrowsOnNegative(ifStmt, parameter, semanticModel, ct))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetStatementInEnclosingBlock(
            SyntaxNode syntax,
            out BlockSyntax block,
            out StatementSyntax statement)
        {
            StatementSyntax? current = syntax.FirstAncestorOrSelf<StatementSyntax>();
            while (current is not null && current.Parent is not BlockSyntax)
            {
                current = current.Parent as StatementSyntax;
            }

            if (current?.Parent is not BlockSyntax parentBlock)
            {
                block = null!;
                statement = null!;
                return false;
            }

            block = parentBlock;
            statement = current;
            return true;
        }

        private static bool InvocationEnforcesParamNonNegative(
            InvocationExpressionSyntax invocationSyntax,
            IParameterSymbol parameter,
            Compilation compilation,
            SemanticModel currentModel,
            CancellationToken ct)
        {
            var invocationOp = currentModel.GetOperation(invocationSyntax, ct) as IInvocationOperation;
            if (invocationOp is null)
            {
                return false;
            }

            if (invocationOp.Arguments.Length != 1)
            {
                return false;
            }

            if (invocationOp.Arguments[0].Value is not IParameterReferenceOperation argParam ||
                !SymbolEqualityComparer.Default.Equals(argParam.Parameter, parameter))
            {
                return false;
            }

            var method = invocationOp.TargetMethod;
            if (method is null)
            {
                return false;
            }

            if (!method.Name.Contains("Index", StringComparison.OrdinalIgnoreCase) &&
                !method.Name.Contains("Assert", StringComparison.OrdinalIgnoreCase) &&
                !method.Name.Contains("Validate", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return MethodEnforcesParameterNonNegative(method, parameterOrdinal: 0, compilation, ct);
        }

        private static bool MethodEnforcesParameterNonNegative(
            IMethodSymbol method,
            int parameterOrdinal,
            Compilation compilation,
            CancellationToken ct)
        {
            if (parameterOrdinal < 0 || parameterOrdinal >= method.Parameters.Length)
            {
                return false;
            }

            var parameter = method.Parameters[parameterOrdinal];

            foreach (var decl in method.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (decl.GetSyntax(ct) is not MethodDeclarationSyntax mds)
                {
                    continue;
                }

                if (mds.Body is null)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(mds.SyntaxTree);
                foreach (var ifStmt in mds.Body.DescendantNodes().OfType<IfStatementSyntax>())
                {
                    ct.ThrowIfCancellationRequested();
                    if (IfThrowsOnNegative(ifStmt, parameter, model, ct))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IfThrowsOnNegative(
            IfStatementSyntax ifStmt,
            IParameterSymbol parameter,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            if (!StatementAlwaysThrows(ifStmt.Statement))
            {
                return false;
            }

            if (ifStmt.Condition is not BinaryExpressionSyntax bin)
            {
                return false;
            }

            if (bin.Kind() != SyntaxKind.LessThanExpression)
            {
                return false;
            }

            if (IsParameterReference(bin.Left, parameter, semanticModel, ct) && IsZeroLiteral(bin.Right, semanticModel, ct))
            {
                return true;
            }

            if (IsZeroLiteral(bin.Left, semanticModel, ct) && IsParameterReference(bin.Right, parameter, semanticModel, ct))
            {
                return true;
            }

            return false;
        }

        private static bool StatementAlwaysThrows(StatementSyntax stmt)
        {
            return stmt switch
            {
                ThrowStatementSyntax => true,
                BlockSyntax b => b.Statements.Count > 0 && b.Statements.All(StatementAlwaysThrows),
                _ => false
            };
        }

        private static bool IsParameterReference(
            ExpressionSyntax expr,
            IParameterSymbol parameter,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            var symbol = semanticModel.GetSymbolInfo(expr, ct).Symbol;
            return symbol is IParameterSymbol p && SymbolEqualityComparer.Default.Equals(p, parameter);
        }

        private static bool IsZeroLiteral(ExpressionSyntax expr, SemanticModel semanticModel, CancellationToken ct)
        {
            var constant = semanticModel.GetConstantValue(expr, ct);
            if (!constant.HasValue || constant.Value is null)
            {
                return false;
            }

            try
            {
                return Convert.ToDecimal(constant.Value) == 0m;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLocalForLoopIndexFromNonNegativeInitializer(
            ILocalSymbol local,
            SyntaxNode bugSiteSyntax,
            IMethodSymbol enclosingMethod,
            SemanticModel semanticModel,
            Compilation compilation,
            CancellationToken ct)
        {
            foreach (var forStmt in bugSiteSyntax.AncestorsAndSelf().OfType<ForStatementSyntax>())
            {
                ct.ThrowIfCancellationRequested();

                if (forStmt.Declaration is null || forStmt.Declaration.Variables.Count != 1)
                {
                    continue;
                }

                var variable = forStmt.Declaration.Variables[0];
                var declared = semanticModel.GetDeclaredSymbol(variable, ct) as ILocalSymbol;
                if (declared is null || !SymbolEqualityComparer.Default.Equals(declared, local))
                {
                    continue;
                }

                if (variable.Initializer?.Value is null)
                {
                    continue;
                }

                var initValue = variable.Initializer.Value;
                var initConst = semanticModel.GetConstantValue(initValue, ct);
                if (initConst.HasValue && initConst.Value is not null)
                {
                    try
                    {
                        if (Convert.ToDecimal(initConst.Value) != 0m)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
                else if (initValue is IdentifierNameSyntax id)
                {
                    var sym = semanticModel.GetSymbolInfo(id, ct).Symbol;
                    if (sym is not IParameterSymbol p)
                    {
                        continue;
                    }

                    if (!HasDominatingNonNegativeGuardForParameter(p, forStmt, enclosingMethod, semanticModel, compilation, ct))
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                if (forStmt.Incrementors.Count == 0)
                {
                    continue;
                }

                var hasIncrement = forStmt.Incrementors.Any(inc =>
                    (inc is PostfixUnaryExpressionSyntax post && post.Kind() == SyntaxKind.PostIncrementExpression &&
                     post.Operand is IdentifierNameSyntax id1 && string.Equals(id1.Identifier.ValueText, local.Name, StringComparison.Ordinal))
                    ||
                    (inc is PrefixUnaryExpressionSyntax pre && pre.Kind() == SyntaxKind.PreIncrementExpression &&
                     pre.Operand is IdentifierNameSyntax id2 && string.Equals(id2.Identifier.ValueText, local.Name, StringComparison.Ordinal)));

                if (!hasIncrement)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        public CSharpGuardStrength? GetSinkProtectionStrength(
            SecurityFlowKind sinkKind,
            IOperation sinkArgument,
            IReadOnlyCollection<CSharpGuardFact> activeGuards,
            SemanticModel semanticModel,
            CancellationToken ct = default)
        {
            if (!_options.EnableGuardProver || activeGuards.Count == 0)
            {
                return null;
            }

            var subjects = BuildSubjectKeys(sinkArgument, semanticModel, ct);
            if (subjects.Count == 0)
            {
                return null;
            }

            CSharpGuardStrength? best = null;
            foreach (var guard in activeGuards)
            {
                if (!subjects.Contains(guard.Subject))
                {
                    continue;
                }

                if (!GuardAppliesToSink(guard.Kind, sinkKind))
                {
                    continue;
                }

                if (best is null || guard.Strength > best.Value)
                {
                    best = guard.Strength;
                }

                if (best == CSharpGuardStrength.Strong)
                {
                    return best;
                }
            }

            return best;
        }

        public bool IsSinkProtected(
            SecurityFlowKind sinkKind,
            IOperation sinkArgument,
            IReadOnlyCollection<CSharpGuardFact> activeGuards,
            SemanticModel semanticModel,
            CancellationToken ct = default)
        {
            return GetSinkProtectionStrength(sinkKind, sinkArgument, activeGuards, semanticModel, ct) == CSharpGuardStrength.Strong;
        }

        private void CollectBranchFacts(
            IOperation condition,
            bool assumeTrue,
            SemanticModel semanticModel,
            List<CSharpGuardFact> results,
            List<string>? diagnostics,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (condition is IConversionOperation conversion &&
                conversion.Type?.SpecialType == SpecialType.System_Boolean)
            {
                CollectBranchFacts(conversion.Operand, assumeTrue, semanticModel, results, diagnostics, ct);
                return;
            }

            if (condition is IUnaryOperation unary &&
                unary.OperatorKind == UnaryOperatorKind.Not)
            {
                CollectBranchFacts(unary.Operand, !assumeTrue, semanticModel, results, diagnostics, ct);
                return;
            }

            if (condition is IBinaryOperation binary)
            {
                if (binary.OperatorKind == BinaryOperatorKind.ConditionalAnd)
                {
                    if (assumeTrue)
                    {
                        CollectBranchFacts(binary.LeftOperand, true, semanticModel, results, diagnostics, ct);
                        CollectBranchFacts(binary.RightOperand, true, semanticModel, results, diagnostics, ct);
                    }

                    return;
                }

                if (binary.OperatorKind == BinaryOperatorKind.ConditionalOr)
                {
                    if (!assumeTrue)
                    {
                        CollectBranchFacts(binary.LeftOperand, false, semanticModel, results, diagnostics, ct);
                        CollectBranchFacts(binary.RightOperand, false, semanticModel, results, diagnostics, ct);
                    }

                    return;
                }
            }

            results.AddRange(DeriveAtomicFacts(condition, assumeTrue, semanticModel, diagnostics, ct));
        }

        private void CollectAllowlistSanitizerFacts(
            IOperation condition,
            bool assumeTrue,
            SemanticModel semanticModel,
            IReadOnlyList<SanitizerSpec> sanitizers,
            List<CSharpGuardFact> results,
            List<string>? diagnostics,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (condition is IConversionOperation conversion &&
                conversion.Type?.SpecialType == SpecialType.System_Boolean)
            {
                CollectAllowlistSanitizerFacts(conversion.Operand, assumeTrue, semanticModel, sanitizers, results, diagnostics, ct);
                return;
            }

            if (condition is IUnaryOperation unary && unary.OperatorKind == UnaryOperatorKind.Not)
            {
                CollectAllowlistSanitizerFacts(unary.Operand, !assumeTrue, semanticModel, sanitizers, results, diagnostics, ct);
                return;
            }

            if (condition is IBinaryOperation binary)
            {
                if (binary.OperatorKind == BinaryOperatorKind.ConditionalAnd)
                {
                    if (assumeTrue)
                    {
                        CollectAllowlistSanitizerFacts(binary.LeftOperand, true, semanticModel, sanitizers, results, diagnostics, ct);
                        CollectAllowlistSanitizerFacts(binary.RightOperand, true, semanticModel, sanitizers, results, diagnostics, ct);
                    }

                    return;
                }

                if (binary.OperatorKind == BinaryOperatorKind.ConditionalOr)
                {
                    if (!assumeTrue)
                    {
                        CollectAllowlistSanitizerFacts(binary.LeftOperand, false, semanticModel, sanitizers, results, diagnostics, ct);
                        CollectAllowlistSanitizerFacts(binary.RightOperand, false, semanticModel, sanitizers, results, diagnostics, ct);
                    }

                    return;
                }

                if (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
                {
                    if (TryGetBoolLiteral(binary.LeftOperand, out var leftConst))
                    {
                        var assumeRight = ConvertAssumption(binary.OperatorKind, assumeTrue, leftConst);
                        CollectAllowlistSanitizerFacts(binary.RightOperand, assumeRight, semanticModel, sanitizers, results, diagnostics, ct);
                        return;
                    }

                    if (TryGetBoolLiteral(binary.RightOperand, out var rightConst))
                    {
                        var assumeLeft = ConvertAssumption(binary.OperatorKind, assumeTrue, rightConst);
                        CollectAllowlistSanitizerFacts(binary.LeftOperand, assumeLeft, semanticModel, sanitizers, results, diagnostics, ct);
                        return;
                    }
                }
            }

            if (condition is IInvocationOperation invocation)
            {
                TryAddAllowlistSanitizerFacts(invocation, assumeTrue, semanticModel, sanitizers, results, ct);
            }
        }

        private static bool ConvertAssumption(BinaryOperatorKind operatorKind, bool expressionAssumedTrue, bool comparedConstant)
        {
            // Derive truth of the non-literal operand for expressions like:
            // - x == true / x == false
            // - x != true / x != false
            return operatorKind switch
            {
                BinaryOperatorKind.Equals => expressionAssumedTrue ? comparedConstant : !comparedConstant,
                BinaryOperatorKind.NotEquals => expressionAssumedTrue ? !comparedConstant : comparedConstant,
                _ => expressionAssumedTrue
            };
        }

        private static bool TryGetBoolLiteral(IOperation operation, out bool value)
        {
            value = false;
            if (!operation.ConstantValue.HasValue || operation.ConstantValue.Value is null)
            {
                return false;
            }

            if (operation.ConstantValue.Value is bool b)
            {
                value = b;
                return true;
            }

            return false;
        }

        private void TryAddAllowlistSanitizerFacts(
            IInvocationOperation invocation,
            bool assumeTrue,
            SemanticModel semanticModel,
            IReadOnlyList<SanitizerSpec> sanitizers,
            List<CSharpGuardFact> results,
            CancellationToken ct)
        {
            if (!assumeTrue)
            {
                return;
            }

            var method = invocation.TargetMethod;
            if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                return;
            }

            var typeName = method.ContainingType?.ToDisplayString();
            var sanitizer = sanitizers.FirstOrDefault(s =>
                s.ReturnsSanitized &&
                CSharpSecurityFlowCore.TypeNameMatches(typeName, s.TypeName) &&
                string.Equals(s.MethodName, method.Name, StringComparison.Ordinal));

            if (sanitizer is null)
            {
                return;
            }

            if (!TryMapAllowlistSanitizerToGuardKind(method, out var guardKind))
            {
                return;
            }

            var taintedOrdinals = new HashSet<int>(sanitizer.SanitizedIndices);
            foreach (var arg in invocation.Arguments)
            {
                var ordinal = arg.Parameter?.Ordinal;
                if (ordinal is null || !taintedOrdinals.Contains(ordinal.Value))
                {
                    continue;
                }

                AddManualFact(
                    guardKind,
                    arg.Value,
                    $"Allowlist: {method.ContainingType?.ToDisplayString()}.{method.Name}",
                    semanticModel,
                    results,
                    ct);
            }
        }

        private static bool TryMapAllowlistSanitizerToGuardKind(IMethodSymbol method, out CSharpGuardKind kind)
        {
            var name = method.Name ?? string.Empty;
            if (name.Length == 0)
            {
                kind = default;
                return false;
            }

            if (name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("FilePath", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Directory", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("File", StringComparison.OrdinalIgnoreCase))
            {
                kind = CSharpGuardKind.PathValidated;
                return true;
            }

            if (name.Contains("Uri", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Redirect", StringComparison.OrdinalIgnoreCase))
            {
                kind = CSharpGuardKind.RedirectAllowlisted;
                return true;
            }

            if (name.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Shell", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Process", StringComparison.OrdinalIgnoreCase))
            {
                kind = CSharpGuardKind.CommandAllowlisted;
                return true;
            }

            if (name.Contains("Sql", StringComparison.OrdinalIgnoreCase))
            {
                kind = CSharpGuardKind.SqlValidated;
                return true;
            }

            if (name.Contains("Ldap", StringComparison.OrdinalIgnoreCase))
            {
                kind = CSharpGuardKind.LdapFilterValidated;
                return true;
            }

            if (name.Contains("Xpath", StringComparison.OrdinalIgnoreCase))
            {
                kind = CSharpGuardKind.XpathValidated;
                return true;
            }

            if (name.Contains("Header", StringComparison.OrdinalIgnoreCase))
            {
                kind = CSharpGuardKind.HeaderValidated;
                return true;
            }

            if (name.Contains("Template", StringComparison.OrdinalIgnoreCase))
            {
                kind = CSharpGuardKind.TemplateTrusted;
                return true;
            }

            kind = default;
            return false;
        }

        private IReadOnlyList<CSharpGuardFact> DeriveAtomicFacts(
            IOperation condition,
            bool assumeTrue,
            SemanticModel semanticModel,
            List<string>? diagnostics,
            CancellationToken ct)
        {
            _ = semanticModel;
            IExpression? lowered = null;

            if (assumeTrue)
            {
                lowered = _lowerer.LowerExpression(condition, null);
            }
            else
            {
                // Invert the operation if possible
                if (condition is IBinaryOperation binary)
                {
                    var invertedKind = binary.OperatorKind switch
                    {
                        BinaryOperatorKind.Equals => BinaryOperatorKind.NotEquals,
                        BinaryOperatorKind.NotEquals => BinaryOperatorKind.Equals,
                        BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThanOrEqual,
                        BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThan,
                        BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThanOrEqual,
                        BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThan,
                        _ => (BinaryOperatorKind?)null
                    };

                    if (invertedKind.HasValue)
                    {
                        lowered = _lowerer.LowerBinaryComparison(binary.LeftOperand, binary.RightOperand, invertedKind.Value, null);
                    }
                }
            }

            if (lowered is null)
            {
                return Array.Empty<CSharpGuardFact>();
            }

            using var egraph = new EGraph();
            var rootId = egraph.Add(lowered.Canonicalize());
            egraph.Rebuild(ct);
            var saturationStatus = SaturateGuardRules(egraph, _rules, ct);
            if (!saturationStatus.IsComplete)
            {
                if (diagnostics is not null)
                {
                    if (!diagnostics.Contains(saturationStatus.Diagnostic, StringComparer.Ordinal))
                    {
                        diagnostics.Add(saturationStatus.Diagnostic);
                    }
                }

                // Fail closed: if guard proving did not complete, do not suppress.
                return Array.Empty<CSharpGuardFact>();
            }

            var rootClassId = egraph.Find(rootId);
            var guardFacts = new List<CSharpGuardFact>();
            var eclass = egraph.GetClass(rootClassId);
            foreach (var node in eclass.Nodes)
            {
                var head = NormalizeHead(node.Head);
                if (!TryMapGuardHead(head, out var kind))
                {
                    continue;
                }

                if (node.Children.Count == 0)
                {
                    continue;
                }

                var subjectExpr = EGraphExtract.ExtractBest(egraph, node.Children[0], null, null, ct);
                if (subjectExpr is null)
                {
                    continue;
                }

                var subject = NormalizeSubjectKey(subjectExpr.Canonicalize().ToDisplayString());
                if (string.IsNullOrWhiteSpace(subject))
                {
                    continue;
                }

                guardFacts.Add(new CSharpGuardFact(
                    kind,
                    subject,
                    CSharpGuardStrength.Strong,
                    $"Guard proved via {head}"));
            }

            return guardFacts;
        }

        private readonly record struct GuardSaturationStatus(bool IsComplete, string Diagnostic);

        private GuardSaturationStatus SaturateGuardRules(EGraph egraph, IReadOnlyList<Rule> rules, CancellationToken ct)
        {
            if (rules.Count == 0)
            {
                return new GuardSaturationStatus(true, string.Empty);
            }

            if (_options.GuardTimeoutSeconds <= 0)
            {
                return new GuardSaturationStatus(
                    false,
                    $"Guard prover timed out after {_options.GuardTimeoutSeconds:0.##}s. Guard-based suppression is disabled for this check.");
            }

            var history = new MatchHistory();
            var changed = true;
            var iterations = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (changed && iterations < _options.GuardMaxIterations)
            {
                ct.ThrowIfCancellationRequested();
                if (sw.Elapsed.TotalSeconds > _options.GuardTimeoutSeconds)
                {
                    return new GuardSaturationStatus(
                        false,
                        $"Guard prover timed out after {_options.GuardTimeoutSeconds:0.##}s. Guard-based suppression is disabled for this check.");
                }

                changed = false;
                iterations++;

                var matches = EGraphMatcher.FindMatches(egraph, rules, history, maxConcurrency: 1, ct);
                foreach (var match in matches)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    int instantiatedClass;
                    if (match.Rule.Condition is not null || match.Rule.Transform is not null)
                    {
                        var materializedBindings = ImmutableDictionary.CreateBuilder<string, IExpression>();
                        foreach (var kvp in match.Bindings)
                        {
                            var bestExpr = EGraphExtract.ExtractBest(egraph, kvp.Value, null, null, ct);
                            if (bestExpr is not null)
                            {
                                materializedBindings[kvp.Key] = bestExpr;
                            }
                        }

                        var bindings = materializedBindings.ToImmutable();
                        if (match.Rule.Condition is not null && !match.Rule.Condition(bindings))
                        {
                            continue;
                        }

                        if (match.Rule.Transform is not null)
                        {
                            var transformed = match.Rule.Transform(bindings);
                            if (transformed is null)
                            {
                                continue;
                            }

                            instantiatedClass = egraph.Add(transformed.Canonicalize());
                        }
                        else
                        {
                            instantiatedClass = EGraphInstantiator.Instantiate(egraph, match.Rule.Replacement, match.Bindings);
                        }
                    }
                    else
                    {
                        instantiatedClass = EGraphInstantiator.Instantiate(egraph, match.Rule.Replacement, match.Bindings);
                    }

                    if (egraph.Find(match.RootClassId) != egraph.Find(instantiatedClass))
                    {
                        egraph.Union(match.RootClassId, instantiatedClass);
                        changed = true;
                    }
                }

                if (changed)
                {
                    egraph.Rebuild(ct);
                }
            }

            // If we stopped because we hit the iteration cap while still changing, results are incomplete.
            // If we converged (changed == false), treat as complete even if iterations == cap.
            if (iterations >= _options.GuardMaxIterations && changed)
            {
                return new GuardSaturationStatus(
                    false,
                    $"Guard prover reached iteration budget ({_options.GuardMaxIterations}). Guard-based suppression is disabled for this check.");
            }

            return new GuardSaturationStatus(true, string.Empty);
        }

        private void AddManualFact(
            CSharpGuardKind kind,
            IOperation subjectOperation,
            string evidence,
            SemanticModel semanticModel,
            List<CSharpGuardFact> results,
            CancellationToken ct)
        {
            var subjects = BuildSubjectKeysWithStrength(subjectOperation, semanticModel, ct);
            foreach (var subject in subjects)
            {
                results.Add(new CSharpGuardFact(kind, subject.Key, subject.Strength, evidence));
            }
        }

        private sealed record SubjectKey(string Key, CSharpGuardStrength Strength);

        private HashSet<string> BuildSubjectKeys(
            IOperation operation,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            var withStrength = BuildSubjectKeysWithStrength(operation, semanticModel, ct);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in withStrength)
            {
                keys.Add(key.Key);
            }

            keys.RemoveWhere(string.IsNullOrWhiteSpace);
            return keys;
        }

        private List<SubjectKey> BuildSubjectKeysWithStrength(
            IOperation operation,
            SemanticModel semanticModel,
            CancellationToken ct)
        {
            _ = semanticModel;
            var keys = new List<SubjectKey>();

            var lowered = _lowerer.LowerExpression(operation, null);
            if (lowered is not null)
            {
                var key = NormalizeSubjectKey(lowered.Canonicalize().ToDisplayString());
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(new SubjectKey(key, CSharpGuardStrength.Strong));
                }
            }

            var stack = new Stack<IOperation>();
            stack.Push(operation);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = stack.Pop();
                switch (current)
                {
                    case ILocalReferenceOperation local:
                        keys.Add(new SubjectKey(local.Local.Name, CSharpGuardStrength.Medium));
                        break;
                    case IParameterReferenceOperation parameter:
                        keys.Add(new SubjectKey(parameter.Parameter.Name, CSharpGuardStrength.Medium));
                        break;
                    case IFieldReferenceOperation field:
                        keys.Add(new SubjectKey(field.Field.Name, CSharpGuardStrength.Medium));
                        break;
                    case IPropertyReferenceOperation property:
                        keys.Add(new SubjectKey(property.Property.Name, CSharpGuardStrength.Medium));
                        break;
                }

                foreach (var child in current.ChildOperations)
                {
                    stack.Push(child);
                }
            }

            return keys
                .Where(k => !string.IsNullOrWhiteSpace(k.Key))
                .GroupBy(k => k.Key, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(x => x.Strength).First())
                .ToList();
        }

        private static bool GuardAppliesToSink(CSharpGuardKind guardKind, SecurityFlowKind sinkKind)
        {
            return CSharpSecurityFlowCore.GuardAppliesToSink(guardKind, sinkKind);
        }

        private static bool TryGetZeroComparedOperand(IBinaryOperation binary, out IOperation operand)
        {
            if (IsZeroLiteral(binary.LeftOperand))
            {
                operand = binary.RightOperand;
                return true;
            }

            if (IsZeroLiteral(binary.RightOperand))
            {
                operand = binary.LeftOperand;
                return true;
            }

            operand = null!;
            return false;
        }

        private static bool TryGetNullComparedOperand(IBinaryOperation binary, out IOperation operand)
        {
            if (IsNullLiteral(binary.LeftOperand))
            {
                operand = binary.RightOperand;
                return true;
            }

            if (IsNullLiteral(binary.RightOperand))
            {
                operand = binary.LeftOperand;
                return true;
            }

            operand = null!;
            return false;
        }

        internal static bool IsZeroLiteral(IOperation operation)
        {
            if (!operation.ConstantValue.HasValue || operation.ConstantValue.Value is null)
            {
                return false;
            }

            try
            {
                return Convert.ToDecimal(operation.ConstantValue.Value) == 0m;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsNullLiteral(IOperation operation)
        {
            return operation.ConstantValue.HasValue && operation.ConstantValue.Value is null;
        }

        private static string NormalizeHead(string head)
        {
            return head.StartsWith("Func:", StringComparison.Ordinal)
                ? head.Substring("Func:".Length)
                : head;
        }

        private static bool TryMapGuardHead(string head, out CSharpGuardKind kind)
        {
            switch (head)
            {
                case "cs_guard_nonzero":
                    kind = CSharpGuardKind.NonZero;
                    return true;
                case "cs_guard_not_null":
                    kind = CSharpGuardKind.NotNull;
                    return true;
                case "cs_guard_path_valid":
                    kind = CSharpGuardKind.PathValidated;
                    return true;
                case "cs_guard_command_allowlisted":
                    kind = CSharpGuardKind.CommandAllowlisted;
                    return true;
                case "cs_guard_sql_validated":
                    kind = CSharpGuardKind.SqlValidated;
                    return true;
                case "cs_guard_ldap_filter_validated":
                    kind = CSharpGuardKind.LdapFilterValidated;
                    return true;
                case "cs_guard_xpath_validated":
                    kind = CSharpGuardKind.XpathValidated;
                    return true;
                case "cs_guard_redirect_allowlisted":
                    kind = CSharpGuardKind.RedirectAllowlisted;
                    return true;
                case "cs_guard_header_validated":
                    kind = CSharpGuardKind.HeaderValidated;
                    return true;
                case "cs_guard_template_trusted":
                    kind = CSharpGuardKind.TemplateTrusted;
                    return true;
                case "cs_guard_non_negative":
                    kind = CSharpGuardKind.NonNegative;
                    return true;
                case "cs_guard_non_positive":
                    kind = CSharpGuardKind.NonPositive;
                    return true;
                case "cs_guard_in_int32_range":
                    kind = CSharpGuardKind.InInt32Range;
                    return true;
                case "cs_guard_in_range_idiom":
                    kind = CSharpGuardKind.InRangeIdiom;
                    return true;
                case "cs_guard_constant_time":
                    kind = CSharpGuardKind.IsConstantTimeEquivalent;
                    return true;
                case "cs_guard_safe_modulo":
                    kind = CSharpGuardKind.SafeModuloResult;
                    return true;
                case "cs_guard_safe_unsigned_wrap":
                    kind = CSharpGuardKind.SafeUnsignedWrap;
                    return true;
                case "cs_guard_valid_shift_amount":
                    kind = CSharpGuardKind.ValidShiftAmount;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private static string NormalizeSubjectKey(string subject)
        {
            return subject.Trim();
        }
    }
}
