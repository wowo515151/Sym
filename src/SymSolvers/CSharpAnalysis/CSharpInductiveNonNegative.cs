// Copyright Warren Harding 2026
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SymSolvers.CSharpAnalysis
{
    internal static class CSharpInductiveNonNegative
    {
        private static readonly ConcurrentDictionary<string, bool> FieldNonNegativeCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, bool> PropertyNonNegativeCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, bool> MethodNonNegativeCache = new(StringComparer.Ordinal);

        internal static bool IsFieldInductivelyNonNegative(IFieldSymbol field, Compilation? compilation, CancellationToken ct)
        {
            if (compilation is null)
            {
                return false;
            }

            var cacheKey = $"{field.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}::{field.Name}";
            if (FieldNonNegativeCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var computed = ComputeFieldInductivelyNonNegative(field, compilation, ct);
            FieldNonNegativeCache[cacheKey] = computed;
            return computed;
        }

        internal static bool IsPropertyInductivelyNonNegative(IPropertySymbol property, Compilation? compilation, CancellationToken ct)
        {
            if (compilation is null)
            {
                return false;
            }

            var cacheKey = $"{property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}::{property.Name}";
            if (PropertyNonNegativeCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var computed = ComputePropertyInductivelyNonNegative(property, compilation, ct);
            PropertyNonNegativeCache[cacheKey] = computed;
            return computed;
        }

        internal static bool IsMethodInductivelyNonNegative(IMethodSymbol method, Compilation? compilation, CancellationToken ct)
        {
            if (compilation is null)
            {
                return false;
            }

            // Only reason about methods we can analyze in the current compilation.
            if (method.DeclaringSyntaxReferences.Length == 0)
            {
                return false;
            }

            var cacheKey = method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (MethodNonNegativeCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var computed = ComputeMethodInductivelyNonNegative(method, compilation, ct);
            MethodNonNegativeCache[cacheKey] = computed;
            return computed;
        }

        private static bool ComputeMethodInductivelyNonNegative(IMethodSymbol method, Compilation compilation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (method.ReturnsVoid)
            {
                return false;
            }

            if (method.ReturnType != null && IsUnsignedNumericType(method.ReturnType))
            {
                return true;
            }

            foreach (var decl in method.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (decl.GetSyntax(ct) is not MethodDeclarationSyntax mds)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(mds.SyntaxTree);
                var returnExpr = TryGetTrivialReturnExpression(mds);
                if (returnExpr is null)
                {
                    continue;
                }

                if (IsExpressionProvablyNonNegative(returnExpr, model, compilation, ct))
                {
                    return true;
                }
            }

            return false;
        }

        private static ExpressionSyntax? TryGetTrivialReturnExpression(MethodDeclarationSyntax mds)
        {
            if (mds.ExpressionBody != null)
            {
                return mds.ExpressionBody.Expression;
            }

            if (mds.Body is null)
            {
                return null;
            }

            // Only accept a trivial method body: { return <expr>; }
            if (mds.Body.Statements.Count != 1)
            {
                return null;
            }

            return mds.Body.Statements[0] is ReturnStatementSyntax ret ? ret.Expression : null;
        }

        private static bool IsExpressionProvablyNonNegative(
            ExpressionSyntax expr,
            SemanticModel model,
            Compilation compilation,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            expr = Unparen(expr);

            // Constant >= 0.
            var constant = model.GetConstantValue(expr, ct);
            if (constant.HasValue && constant.Value is not null)
            {
                try
                {
                    return Convert.ToDecimal(constant.Value) >= 0m;
                }
                catch
                {
                    // Ignore conversion failures.
                }
            }

            // Any .Length is assumed non-negative (covers common size/count helpers).
            if (expr is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" })
            {
                return true;
            }

            // Resolve symbol references.
            var sym = model.GetSymbolInfo(expr, ct).Symbol;
            if (sym is IFieldSymbol field)
            {
                return IsFieldInductivelyNonNegative(field, compilation, ct);
            }

            if (sym is IPropertySymbol property)
            {
                if (property.Name == "Length")
                {
                    return true;
                }

                return IsPropertyInductivelyNonNegative(property, compilation, ct);
            }

            // Handle simple numeric conversions around the underlying expression.
            if (expr is CastExpressionSyntax cast)
            {
                return IsExpressionProvablyNonNegative(cast.Expression, model, compilation, ct);
            }

            if (expr is PrefixUnaryExpressionSyntax { OperatorToken.ValueText: "+" } plus)
            {
                return IsExpressionProvablyNonNegative(plus.Operand, model, compilation, ct);
            }

            return false;
        }

        private static bool ComputePropertyInductivelyNonNegative(IPropertySymbol property, Compilation compilation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // If the property type is unsigned, it's inherently non-negative.
            if (property.Type != null && IsUnsignedNumericType(property.Type))
            {
                return true;
            }

            foreach (var decl in property.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (decl.GetSyntax(ct) is not PropertyDeclarationSyntax pds)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(pds.SyntaxTree);
                var valueExpr = TryGetTrivialGetterExpression(pds);
                if (valueExpr is null)
                {
                    continue;
                }

                // Recognize common safe sources without needing symbol resolution.
                if (valueExpr is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" })
                {
                    return true;
                }

                var sym = model.GetSymbolInfo(valueExpr, ct).Symbol;
                if (sym is IFieldSymbol field)
                {
                    return IsFieldInductivelyNonNegative(field, compilation, ct);
                }

                if (sym is IPropertySymbol nested)
                {
                    if (SymbolEqualityComparer.Default.Equals(nested, property))
                    {
                        continue;
                    }

                    return IsPropertyInductivelyNonNegative(nested, compilation, ct);
                }
            }

            return false;
        }

        private static ExpressionSyntax? TryGetTrivialGetterExpression(PropertyDeclarationSyntax pds)
        {
            if (pds.ExpressionBody != null)
            {
                return pds.ExpressionBody.Expression;
            }

            if (pds.AccessorList is null)
            {
                return null;
            }

            var getter = pds.AccessorList.Accessors.FirstOrDefault(a => a.Kind() == SyntaxKind.GetAccessorDeclaration);
            if (getter?.ExpressionBody != null)
            {
                return getter.ExpressionBody.Expression;
            }

            if (getter?.Body != null)
            {
                // Only accept a trivial getter: { return <expr>; }
                if (getter.Body.Statements.Count == 1 && getter.Body.Statements[0] is ReturnStatementSyntax ret)
                {
                    return ret.Expression;
                }
            }

            return null;
        }

        private static bool ComputeFieldInductivelyNonNegative(IFieldSymbol field, Compilation compilation, CancellationToken ct)
        {
            if (TryComputeFieldNonNegativeRingBuffer(field, compilation, ct))
            {
                return true;
            }

            if (TryComputeFieldNonNegativeCounter(field, compilation, ct))
            {
                return true;
            }

            return false;
        }

        private static bool TryComputeFieldNonNegativeRingBuffer(IFieldSymbol field, Compilation compilation, CancellationToken ct)
        {
            // Heuristic for ring-buffer head pointers like _head:
            // - Has an explicit 0 initialization OR relies on implicit default(0) initialization.
            // - Only assigned 0 or (field + k) % m with k >= 0.
            // Fail closed on any other assignment.

            var hasZeroInit = HasZeroOrDefaultInitializer(field, compilation, ct);
            var hasAnyAssignment = false;

            var type = field.ContainingType;
            foreach (var typeRef in type.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (typeRef.GetSyntax(ct) is not TypeDeclarationSyntax tds)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(tds.SyntaxTree);
                foreach (var assignment in tds.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    ct.ThrowIfCancellationRequested();

                    if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
                    {
                        continue;
                    }

                    var leftSymbol = model.GetSymbolInfo(assignment.Left, ct).Symbol;
                    if (leftSymbol is not IFieldSymbol lhs || !SymbolEqualityComparer.Default.Equals(lhs, field))
                    {
                        continue;
                    }

                    hasAnyAssignment = true;

                    // Allow: field = 0;
                    if (IsConstantZero(assignment.Right, model, ct))
                    {
                        hasZeroInit = true;
                        continue;
                    }

                    // Allow: field = (field + k) % m; (parentheses optional)
                    ExpressionSyntax rhs = Unparen(assignment.Right);
                    if (rhs is BinaryExpressionSyntax modulo && modulo.Kind() == SyntaxKind.ModuloExpression)
                    {
                        ExpressionSyntax dividend = Unparen(modulo.Left);
                        if (dividend is BinaryExpressionSyntax add && add.Kind() == SyntaxKind.AddExpression)
                        {
                            var leftIsField = IsFieldReference(add.Left, field, model, ct) || IsLocalAliasOfField(add.Left, field, model, ct);
                            var rightIsField = IsFieldReference(add.Right, field, model, ct) || IsLocalAliasOfField(add.Right, field, model, ct);
                            if (leftIsField ^ rightIsField)
                            {
                                var other = leftIsField ? add.Right : add.Left;
                                if (TryGetNonNegativeConstant(other, model, ct) is decimal)
                                {
                                    continue;
                                }
                            }
                        }
                    }

                    return false;
                }
            }

            return hasZeroInit && hasAnyAssignment;
        }

        private static bool HasZeroOrDefaultInitializer(IFieldSymbol field, Compilation compilation, CancellationToken ct)
        {
            if (HasZeroInitializer(field, compilation, ct))
            {
                return true;
            }

            // C# value-type fields are implicitly initialized to default(T) (e.g., 0 for integers).
            // For our ring-buffer heuristic, we can treat missing initializers as 0 so long as we
            // also verify that all writes preserve non-negativity (fail-closed on other assignments).
            if (!IsIntegralNumericType(field.Type))
            {
                return false;
            }

            foreach (var decl in field.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (decl.GetSyntax(ct) is VariableDeclaratorSyntax vds)
                {
                    // If any declaration has an explicit initializer (non-zero), don't assume default(0).
                    if (vds.Initializer is not null)
                    {
                        return false;
                    }
                }
            }

            // No explicit initializer found across declarations.
            return true;
        }

        private static bool IsLocalAliasOfField(ExpressionSyntax expr, IFieldSymbol field, SemanticModel model, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            expr = Unparen(expr);
            if (expr is not IdentifierNameSyntax id)
            {
                return false;
            }

            var sym = model.GetSymbolInfo(id, ct).Symbol;
            if (sym is not ILocalSymbol local)
            {
                return false;
            }

            foreach (var decl in local.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (decl.GetSyntax(ct) is not VariableDeclaratorSyntax vds)
                {
                    continue;
                }

                if (vds.Initializer is null)
                {
                    continue;
                }

                if (IsFieldReference(vds.Initializer.Value, field, model, ct))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIntegralNumericType(ITypeSymbol? type)
        {
            if (type is null)
            {
                return false;
            }

            return type.SpecialType is
                SpecialType.System_Byte or
                SpecialType.System_SByte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64;
        }

        private static bool TryComputeFieldNonNegativeCounter(IFieldSymbol field, Compilation compilation, CancellationToken ct)
        {
            // Heuristic for non-negative counters like _count:
            // - Has an explicit 0 initialization.
            // - Only modified by +k and -k where k is a non-negative constant.
            // - Any decrement is dominated by a guard that prevents underflow.
            // Fail closed on any other assignment.

            var hasZeroInit = HasZeroInitializer(field, compilation, ct);
            var hasAnyWrite = false;

            var type = field.ContainingType;
            foreach (var typeRef in type.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (typeRef.GetSyntax(ct) is not TypeDeclarationSyntax tds)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(tds.SyntaxTree);

                foreach (var inc in tds.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
                {
                    ct.ThrowIfCancellationRequested();
                    if (inc.Kind() != SyntaxKind.PostIncrementExpression && inc.Kind() != SyntaxKind.PostDecrementExpression)
                    {
                        continue;
                    }

                    if (!IsFieldReference(inc.Operand, field, model, ct))
                    {
                        continue;
                    }

                    hasAnyWrite = true;

                    if (inc.Kind() == SyntaxKind.PostDecrementExpression)
                    {
                        if (!IsGuardedDecrementSite(inc, field, decrementAmount: 1m, model, ct))
                        {
                            return false;
                        }
                    }
                }

                foreach (var inc in tds.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
                {
                    ct.ThrowIfCancellationRequested();
                    if (inc.Kind() != SyntaxKind.PreIncrementExpression && inc.Kind() != SyntaxKind.PreDecrementExpression)
                    {
                        continue;
                    }

                    if (!IsFieldReference(inc.Operand, field, model, ct))
                    {
                        continue;
                    }

                    hasAnyWrite = true;

                    if (inc.Kind() == SyntaxKind.PreDecrementExpression)
                    {
                        if (!IsGuardedDecrementSite(inc, field, decrementAmount: 1m, model, ct))
                        {
                            return false;
                        }
                    }
                }

                foreach (var assignment in tds.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    ct.ThrowIfCancellationRequested();

                    var leftSymbol = model.GetSymbolInfo(assignment.Left, ct).Symbol;
                    if (leftSymbol is not IFieldSymbol lhs || !SymbolEqualityComparer.Default.Equals(lhs, field))
                    {
                        continue;
                    }

                    hasAnyWrite = true;

                    if (IsConstantZero(assignment.Right, model, ct))
                    {
                        hasZeroInit = true;
                        continue;
                    }

                    if (assignment.Kind() == SyntaxKind.AddAssignmentExpression || assignment.Kind() == SyntaxKind.SubtractAssignmentExpression)
                    {
                        var k = TryGetNonNegativeConstant(assignment.Right, model, ct);
                        if (k is null)
                        {
                            return false;
                        }

                        if (assignment.Kind() == SyntaxKind.SubtractAssignmentExpression)
                        {
                            if (!IsGuardedDecrementSite(assignment, field, k.Value, model, ct))
                            {
                                return false;
                            }
                        }

                        continue;
                    }

                    if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
                    {
                        return false;
                    }

                    ExpressionSyntax rhs = Unparen(assignment.Right);
                    if (rhs is BinaryExpressionSyntax bin && (bin.Kind() == SyntaxKind.AddExpression || bin.Kind() == SyntaxKind.SubtractExpression))
                    {
                        var leftIsField = IsFieldReference(bin.Left, field, model, ct);
                        var rightIsField = IsFieldReference(bin.Right, field, model, ct);
                        if (leftIsField ^ rightIsField)
                        {
                            var other = leftIsField ? bin.Right : bin.Left;
                            if (TryGetNonNegativeConstant(other, model, ct) is decimal)
                            {
                                // Note: for subtract, require field - k (not k - field)
                                if (bin.Kind() == SyntaxKind.SubtractExpression && !leftIsField)
                                {
                                    return false;
                                }

                                if (bin.Kind() == SyntaxKind.SubtractExpression)
                                {
                                    if (!IsGuardedDecrementSite(assignment, field, TryGetNonNegativeConstant(other, model, ct)!.Value, model, ct))
                                    {
                                        return false;
                                    }
                                }

                                continue;
                            }
                        }
                    }

                    return false;
                }
            }

            return hasZeroInit && hasAnyWrite;
        }

        private static bool IsGuardedDecrementSite(SyntaxNode decrementSite, IFieldSymbol field, decimal decrementAmount, SemanticModel model, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (decrementAmount <= 0m)
            {
                return true;
            }

            // If we're inside an explicit guard like: if (_count > 0) { _count--; }
            var statement = decrementSite.FirstAncestorOrSelf<StatementSyntax>();
            if (statement != null)
            {
                foreach (var ifStmt in statement.AncestorsAndSelf().OfType<IfStatementSyntax>())
                {
                    if (!ifStmt.Statement.Span.Contains(statement.Span))
                    {
                        continue;
                    }

                    if (ConditionImpliesFieldAtLeast(ifStmt.Condition, field, decrementAmount, model, ct))
                    {
                        return true;
                    }
                }
            }

            // If we have a dominating early-exit guard immediately before the decrement:
            // if (_count == 0) throw/return; _count--;
            if (!TryGetStatementInEnclosingBlock(decrementSite, out var block, out var stmt))
            {
                return false;
            }

            var statementIndex = block.Statements.IndexOf(stmt);
            if (statementIndex < 0)
            {
                return false;
            }

            var maxToScan = Math.Min(statementIndex, 12);
            for (var i = 1; i <= maxToScan; i++)
            {
                ct.ThrowIfCancellationRequested();
                var prev = block.Statements[statementIndex - i];
                if (prev is IfStatementSyntax ifStmt && StatementAlwaysExits(ifStmt.Statement) && ConditionPreventsFieldUnderflow(ifStmt.Condition, field, decrementAmount, model, ct))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetStatementInEnclosingBlock(SyntaxNode syntax, out BlockSyntax block, out StatementSyntax statement)
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

        private static bool StatementAlwaysExits(StatementSyntax stmt)
        {
            return stmt switch
            {
                ThrowStatementSyntax => true,
                ReturnStatementSyntax => true,
                BlockSyntax b => b.Statements.Count > 0 && b.Statements.All(StatementAlwaysExits),
                _ => false
            };
        }

        private static bool ConditionImpliesFieldAtLeast(ExpressionSyntax condition, IFieldSymbol field, decimal amount, SemanticModel model, CancellationToken ct)
        {
            condition = condition switch
            {
                ParenthesizedExpressionSyntax p => p.Expression,
                _ => condition
            };

            if (condition is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalAndExpression } and)
            {
                return ConditionImpliesFieldAtLeast(and.Left, field, amount, model, ct) || ConditionImpliesFieldAtLeast(and.Right, field, amount, model, ct);
            }

            if (condition is not BinaryExpressionSyntax bin)
            {
                return false;
            }

            var opKind = bin.Kind();
            var leftIsField = IsFieldReference(bin.Left, field, model, ct);
            var rightIsField = IsFieldReference(bin.Right, field, model, ct);

            if (amount == 1m)
            {
                // if (field > 0) / if (field != 0) / if (field >= 1)
                if (leftIsField && TryGetNonNegativeConstant(bin.Right, model, ct) is decimal c1)
                {
                    if (opKind == SyntaxKind.GreaterThanExpression && c1 == 0m) return true;
                    if (opKind == SyntaxKind.NotEqualsExpression && c1 == 0m) return true;
                    if (opKind == SyntaxKind.GreaterThanOrEqualExpression && c1 >= 1m) return true;
                }
                if (rightIsField && TryGetNonNegativeConstant(bin.Left, model, ct) is decimal c2)
                {
                    if (opKind == SyntaxKind.LessThanExpression && c2 == 0m) return true; // 0 < field
                    if (opKind == SyntaxKind.NotEqualsExpression && c2 == 0m) return true;
                }
            }

            if (leftIsField && TryGetNonNegativeConstant(bin.Right, model, ct) is decimal c)
            {
                if (opKind == SyntaxKind.GreaterThanOrEqualExpression && c >= amount) return true;
                if (opKind == SyntaxKind.GreaterThanExpression && c >= amount - 1m) return true;
            }

            if (rightIsField && TryGetNonNegativeConstant(bin.Left, model, ct) is decimal cL)
            {
                // c <= field  (amount: field >= c)
                if (opKind == SyntaxKind.LessThanOrEqualExpression && cL >= amount) return true;
                if (opKind == SyntaxKind.LessThanExpression && cL >= amount - 1m) return true;
            }

            return false;
        }

        private static bool ConditionPreventsFieldUnderflow(ExpressionSyntax condition, IFieldSymbol field, decimal amount, SemanticModel model, CancellationToken ct)
        {
            condition = condition switch
            {
                ParenthesizedExpressionSyntax p => p.Expression,
                _ => condition
            };

            if (condition is not BinaryExpressionSyntax bin)
            {
                return false;
            }

            var opKind = bin.Kind();

            // if (field == 0) exit;  for amount==1
            if (amount == 1m && opKind == SyntaxKind.EqualsExpression)
            {
                if (IsFieldReference(bin.Left, field, model, ct) && IsZeroLiteral(bin.Right, model, ct)) return true;
                if (IsZeroLiteral(bin.Left, model, ct) && IsFieldReference(bin.Right, field, model, ct)) return true;
            }

            // if (field < amount) exit;
            if (opKind == SyntaxKind.LessThanExpression)
            {
                if (IsFieldReference(bin.Left, field, model, ct) && TryGetNonNegativeConstant(bin.Right, model, ct) is decimal c && c >= amount)
                {
                    return true;
                }
            }

            // if (field <= amount-1) exit;
            if (opKind == SyntaxKind.LessThanOrEqualExpression)
            {
                if (IsFieldReference(bin.Left, field, model, ct) && TryGetNonNegativeConstant(bin.Right, model, ct) is decimal c && c >= amount - 1m)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsZeroLiteral(ExpressionSyntax expr, SemanticModel model, CancellationToken ct)
        {
            var constant = model.GetConstantValue(expr, ct);
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

        private static bool HasZeroInitializer(IFieldSymbol field, Compilation compilation, CancellationToken ct)
        {
            foreach (var decl in field.DeclaringSyntaxReferences)
            {
                ct.ThrowIfCancellationRequested();
                if (decl.GetSyntax(ct) is VariableDeclaratorSyntax vds && vds.Initializer != null)
                {
                    var model = compilation.GetSemanticModel(vds.SyntaxTree);
                    if (IsConstantZero(vds.Initializer.Value, model, ct))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsConstantZero(ExpressionSyntax expr, SemanticModel model, CancellationToken ct)
        {
            var c = model.GetConstantValue(expr, ct);
            if (!c.HasValue || c.Value is null)
            {
                return false;
            }

            try
            {
                return Convert.ToDecimal(c.Value) == 0m;
            }
            catch
            {
                return false;
            }
        }

        private static ExpressionSyntax Unparen(ExpressionSyntax expr)
        {
            while (expr is ParenthesizedExpressionSyntax p)
            {
                expr = p.Expression;
            }

            return expr;
        }

        private static bool IsFieldReference(ExpressionSyntax expr, IFieldSymbol field, SemanticModel model, CancellationToken ct)
        {
            var symbol = model.GetSymbolInfo(expr, ct).Symbol;
            return symbol is IFieldSymbol f && SymbolEqualityComparer.Default.Equals(f, field);
        }

        private static decimal? TryGetNonNegativeConstant(ExpressionSyntax expr, SemanticModel model, CancellationToken ct)
        {
            var constant = model.GetConstantValue(expr, ct);
            if (!constant.HasValue || constant.Value is null)
            {
                return null;
            }

            try
            {
                var value = Convert.ToDecimal(constant.Value);
                return value >= 0m ? value : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUnsignedNumericType(ITypeSymbol type)
        {
            return type.SpecialType is
                SpecialType.System_Byte or
                SpecialType.System_UInt16 or
                SpecialType.System_UInt32 or
                SpecialType.System_UInt64 or
                SpecialType.System_Char;
        }
    }
}
