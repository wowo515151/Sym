// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Threading;

namespace SymSolvers.CSharpAnalysis
{
    public class CSharpSemanticLowerer
    {
        public List<(IExpression Expression, CSharpExpressionMetadata Metadata)> Lower(
            SemanticModel semanticModel,
            SyntaxNode root,
            CancellationToken ct = default)
        {
            var results = new List<(IExpression Expression, CSharpExpressionMetadata Metadata)>();
            var loweredSpans = new HashSet<(int Start, int Length)>();
            const int MaxEmittedExpressionsPerTree = 5000;
            
            // 1. Traverse all executable operations if possible
            var blocks = root.DescendantNodes().Where(n => n is BaseMethodDeclarationSyntax || n is LocalFunctionStatementSyntax || n is AnonymousFunctionExpressionSyntax || n is GlobalStatementSyntax);
            foreach (var block in blocks)
            {
                ct.ThrowIfCancellationRequested();
                if (results.Count >= MaxEmittedExpressionsPerTree)
                {
                    break;
                }
                var op = semanticModel.GetOperation(block, ct);
                var symbol = semanticModel.GetDeclaredSymbol(block, ct) as IMethodSymbol;
                LowerRecursive(op, results, symbol, loweredSpans, ct, MaxEmittedExpressionsPerTree);
            }

            // 2. Targeted fallback: cover top-level statements / initializers that may not appear as method blocks.
            // Avoid the prior "scan every ExpressionSyntax" fallback, which is too expensive on large files.
            if (results.Count < MaxEmittedExpressionsPerTree)
            {
                var candidates = root.DescendantNodes().Where(static n =>
                    n is BinaryExpressionSyntax ||
                    n is InvocationExpressionSyntax ||
                    n is ObjectCreationExpressionSyntax ||
                    n is ArrayCreationExpressionSyntax ||
                    n is ElementAccessExpressionSyntax ||
                    n is AssignmentExpressionSyntax ||
                    n is PrefixUnaryExpressionSyntax ||
                    n is PostfixUnaryExpressionSyntax ||
                    n is CastExpressionSyntax);

                foreach (var node in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    if (results.Count >= MaxEmittedExpressionsPerTree)
                    {
                        break;
                    }

                    var spanKey = (node.SpanStart, node.Span.Length);
                    if (loweredSpans.Contains(spanKey))
                    {
                        continue;
                    }

                    var candidateOp = semanticModel.GetOperation(node, ct);
                    if (candidateOp is null)
                    {
                        continue;
                    }

                    var lowered = LowerExpression(candidateOp, null);
                    if (lowered != null && ShouldEmitLoweredExpression(lowered))
                    {
                        results.Add((lowered, CreateMetadata(candidateOp)));
                        loweredSpans.Add(spanKey);
                    }
                }
            }

            // Detect unsafe code
            if (root.DescendantNodesAndSelf().Any(n => n.IsKind(SyntaxKind.UnsafeStatement)) || 
                root.DescendantTokens().Any(t => t.IsKind(SyntaxKind.UnsafeKeyword)))
            {
                results.Add((new Symbol("cs_unsafe_block"), new CSharpExpressionMetadata(CSharpNumericType.Unknown, CSharpOverflowContext.Unspecified, "", 0, 0, "unsafe", null)));
            }

            return results;
        }

        private void LowerRecursive(
            IOperation? operation,
            List<(IExpression Expression, CSharpExpressionMetadata Metadata)> results,
            IMethodSymbol? currentMethod,
            HashSet<(int Start, int Length)> loweredSpans,
            CancellationToken ct,
            int maxEmittedExpressions)
        {
            if (operation == null) return;
            ct.ThrowIfCancellationRequested();
            if (results.Count >= maxEmittedExpressions)
            {
                return;
            }

            // Try to lower this specific operation if it is a relevant numeric expression
            var lowered = LowerExpression(operation, currentMethod);
            if (lowered != null && ShouldEmitLoweredExpression(lowered))
            {
                var metadata = CreateMetadata(operation);
                results.Add((lowered, metadata));
                loweredSpans.Add((operation.Syntax.SpanStart, operation.Syntax.Span.Length));

                if (results.Count >= maxEmittedExpressions)
                {
                    return;
                }
            }

            // ALWAYS recurse into children to find sub-expressions (e.g. index access inside assignment)
            // Note: IOperation.Children is obsolete, use ChildOperations
            foreach (var child in operation.ChildOperations)
            {
                if (results.Count >= maxEmittedExpressions)
                {
                    return;
                }

                LowerRecursive(child, results, currentMethod, loweredSpans, ct, maxEmittedExpressions);
            }
        }

        private static bool ShouldEmitLoweredExpression(IExpression lowered)
        {
            // Keep the lowered input set focused so the EGraph doesn't explode on large codebases.
            // Sub-expressions still get represented via the emitted function trees.
            if (lowered is Function)
            {
                return true;
            }

            if (lowered is Symbol symbol && symbol.Name.StartsWith("cs_", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private CSharpExpressionMetadata CreateMetadata(IOperation operation)
        {
            var syntax = operation.Syntax;
            var lineSpan = syntax.GetLocation().GetLineSpan();
            
            return new CSharpExpressionMetadata(
                GetNumericType(operation.Type),
                GetOverflowContext(operation),
                lineSpan.Path,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                syntax.ToString(),
                operation.ConstantValue.HasValue ? operation.ConstantValue.Value : null
            );
        }

        private CSharpNumericType GetNumericType(ITypeSymbol? type)
        {
            if (type == null) return CSharpNumericType.Unknown;
            return type.SpecialType switch
            {
                SpecialType.System_Int32 => CSharpNumericType.I32,
                SpecialType.System_UInt32 => CSharpNumericType.U32,
                SpecialType.System_Int64 => CSharpNumericType.I64,
                SpecialType.System_UInt64 => CSharpNumericType.U64,
                SpecialType.System_Single => CSharpNumericType.F32,
                SpecialType.System_Double => CSharpNumericType.F64,
                SpecialType.System_Decimal => CSharpNumericType.Decimal,
                SpecialType.System_IntPtr => CSharpNumericType.NativeInt,
                SpecialType.System_UIntPtr => CSharpNumericType.NativeUInt,
                _ => CSharpNumericType.Unknown
            };
        }

        private CSharpOverflowContext GetOverflowContext(IOperation operation)
        {
            if (operation is IBinaryOperation bin) return bin.IsChecked ? CSharpOverflowContext.Checked : CSharpOverflowContext.Unchecked;
            if (operation is IUnaryOperation un) return un.IsChecked ? CSharpOverflowContext.Checked : CSharpOverflowContext.Unchecked;
            if (operation is IConversionOperation conv) return conv.IsChecked ? CSharpOverflowContext.Checked : CSharpOverflowContext.Unchecked;
            
            return CSharpOverflowContext.Unspecified;
        }

        public IExpression? LowerExpression(IOperation operation, IMethodSymbol? currentMethod = null)
        {
            if (operation == null) return null;
            if (operation is IVariableDeclaratorOperation declaratorWithoutType)
            {
                return LowerVariableDeclarator(declaratorWithoutType, currentMethod);
            }
            if (operation.Type == null) return null;

            // Constants
            if (operation.ConstantValue.HasValue)
            {
                 if (operation.ConstantValue.Value is null)
                 {
                     return new Symbol("null");
                 }

                 if (IsNumeric(operation.Type))
                 {
                     // Special case: if it's a binary XOR, we want to keep structure for analysis (CSMATH009)
                     if (operation is not IBinaryOperation { OperatorKind: BinaryOperatorKind.ExclusiveOr })
                     {
                         try {
                             var val = Convert.ToDecimal(operation.ConstantValue.Value);
                             return new Sym.Atoms.Number(val);
                         } catch { return null; }
                     }
                 }
                 else if (operation.Type.SpecialType == SpecialType.System_String)
                 {
                     var val = operation.ConstantValue.Value as string;
                     if (val != null) return new Symbol($"str:{val}");
                 }
            }

            // If not constant, check structure
            // We only care about numeric expressions usually.
            // Exception: Invocation might be void or non-numeric but we still want to detect recursion?
            // But currently IsNumeric filters everything.
            // If the method returns void/bool, IsNumeric is false.
            // CalculateDeterminantRecursive returns T (Numeric). So it passes.
            
            // Bypass IsNumeric for ObjectCreation and ArrayCreation (Security/Math checks)
            if (operation is IObjectCreationOperation)
            {
                 return LowerObjectCreation((IObjectCreationOperation)operation, currentMethod);
            }
            if (operation is IArrayCreationOperation)
            {
                 return LowerArrayCreation((IArrayCreationOperation)operation, currentMethod);
            }
            if (operation is IArrayElementReferenceOperation)
            {
                 return LowerArrayElementReference((IArrayElementReferenceOperation)operation, currentMethod);
            }

            if (operation is IBinaryOperation binOp && (binOp.OperatorKind == BinaryOperatorKind.Equals || binOp.OperatorKind == BinaryOperatorKind.NotEquals))
            {
                return LowerBinary(binOp, currentMethod);
            }

            if (!IsNumeric(operation.Type) && 
                operation.Type?.SpecialType != SpecialType.System_Boolean && 
                operation.Type?.SpecialType != SpecialType.System_String &&
                operation is not IInvocationOperation) return null; 

            return operation switch
            {
                IBinaryOperation bin => LowerBinary(bin, currentMethod),
                IUnaryOperation un => LowerUnary(un, currentMethod),
                IConversionOperation conv => LowerConversion(conv, currentMethod),
                ILocalReferenceOperation local => new Symbol(local.Local.Name!),
                IParameterReferenceOperation param => new Symbol(param.Parameter.Name!),
                IFieldReferenceOperation field => new Symbol(field.Field.Name!),
                IPropertyReferenceOperation prop => LowerPropertyReference(prop, currentMethod),
                IInvocationOperation inv => LowerInvocation(inv, currentMethod),
                IObjectCreationOperation newObj => LowerObjectCreation(newObj, currentMethod),
                IArrayCreationOperation newArr => LowerArrayCreation(newArr, currentMethod),
                IArrayElementReferenceOperation arrayRef => LowerArrayElementReference(arrayRef, currentMethod),
                ICompoundAssignmentOperation compound => LowerCompoundAssignment(compound, currentMethod),
                IAssignmentOperation assignment => LowerAssignment(assignment, currentMethod),
                IIncrementOrDecrementOperation incDec => LowerIncrementOrDecrement(incDec, currentMethod),
                _ => null
            };
        }

        // Guard proving sometimes needs to invert a comparison without being able to synthesize
        // a new Roslyn IBinaryOperation. Keep comparison-name generation centralized here.
        public IExpression? LowerBinaryComparison(
            IOperation leftOperand,
            IOperation rightOperand,
            BinaryOperatorKind operatorKind,
            IMethodSymbol? currentMethod = null)
        {
            var left = LowerExpression(leftOperand, currentMethod);
            var right = LowerExpression(rightOperand, currentMethod);
            if (left == null || right == null) return null;

            var typeSuffix = GetTypeSuffix(leftOperand.Type);
            if (typeSuffix == null) return null;

            var op = operatorKind switch
            {
                BinaryOperatorKind.Equals => "eq",
                BinaryOperatorKind.NotEquals => "neq",
                BinaryOperatorKind.LessThan => "lt",
                BinaryOperatorKind.LessThanOrEqual => "lte",
                BinaryOperatorKind.GreaterThan => "gt",
                BinaryOperatorKind.GreaterThanOrEqual => "gte",
                _ => null
            };

            if (op is null) return null;
            return new Function($"cs_{op}_{typeSuffix}", new[] { left, right });
        }

        private IExpression? LowerVariableDeclarator(IVariableDeclaratorOperation declarator, IMethodSymbol? currentMethod)
        {
            if (declarator.Symbol == null || declarator.Initializer == null)
            {
                return null;
            }

            var value = LowerExpression(declarator.Initializer.Value, currentMethod);
            if (value == null)
            {
                return null;
            }

            // Emit declarations with initializers as assignment facts so analysis can reason about local values.
            return new Function("cs_assign", new IExpression[] { new Symbol(declarator.Symbol.Name), value });
        }

        private IExpression? LowerPropertyReference(IPropertyReferenceOperation prop, IMethodSymbol? currentMethod)
        {
            var property = prop.Property;
            var type = property.ContainingType;
            
            if (type != null && (type.Name == "DateTime" || type.Name == "DateTimeOffset"))
            {
                // Handle static Now, Ticks, etc.
                string name = $"{type.Name}.{property.Name}";
                if (prop.Instance != null)
                {
                    var instance = LowerExpression(prop.Instance, currentMethod);
                    if (instance != null) return new Function("cs_prop_get", new[] { instance, new Symbol(property.Name!) });
                }
                return new Symbol(name);
            }

            return new Symbol(property.Name!);
        }

        private IExpression? LowerIncrementOrDecrement(IIncrementOrDecrementOperation incDec, IMethodSymbol? currentMethod)
        {
            var target = LowerExpression(incDec.Target, currentMethod);
            if (target == null) return null;

            var typeSuffix = GetTypeSuffix(incDec.Type);
            if (typeSuffix == null) return null;

            string op = incDec.Kind == OperationKind.Increment ? "inc" : "dec";
            string opName = $"cs_{op}_{typeSuffix}_{(incDec.IsChecked ? "checked" : "unchecked")}";

            return new Function(opName, new[] { target });
        }

        private IExpression? LowerAssignment(IAssignmentOperation assignment, IMethodSymbol? currentMethod)
        {
            var target = LowerExpression(assignment.Target, currentMethod);
            var value = LowerExpression(assignment.Value, currentMethod);
            if (target == null || value == null) return null;

            // Detect static state modification
            bool isStatic = false;
            string? targetName = null;

            if (assignment.Target is IFieldReferenceOperation fieldRef && fieldRef.Field.IsStatic)
            {
                isStatic = true;
                targetName = fieldRef.Field.Name;
            }
            else if (assignment.Target is IPropertyReferenceOperation propRef && propRef.Property.IsStatic)
            {
                isStatic = true;
                targetName = propRef.Property.Name;
            }

            if (isStatic && targetName != null)
            {
                return new Function("cs_static_assignment", new[] { new Symbol(targetName) });
            }

            return new Function("cs_assign", new[] { target, value });
        }

        private IExpression? LowerCompoundAssignment(ICompoundAssignmentOperation compound, IMethodSymbol? currentMethod)
        {
            var left = LowerExpression(compound.Target, currentMethod);
            var right = LowerExpression(compound.Value, currentMethod);
            if (left == null || right == null) return null;

            var typeSuffix = GetTypeSuffix(compound.Type);
            if (typeSuffix == null) return null;

            string? op = compound.OperatorKind switch
            {
                BinaryOperatorKind.Add => "add",
                BinaryOperatorKind.Subtract => "sub",
                BinaryOperatorKind.Multiply => "mul",
                BinaryOperatorKind.Divide => "div",
                BinaryOperatorKind.Remainder => "mod",
                _ => null
            };

            if (op == null) return null;

            string opName;
            bool isChecked = compound.IsChecked;
            if (op == "div" || op == "mod")
            {
                opName = $"cs_{op}_{typeSuffix}";
            }
            else
            {
                opName = $"cs_{op}_{typeSuffix}_{(isChecked ? "checked" : "unchecked")}";
            }

            return new Function(opName, new[] { left, right });
        }

        private IExpression? LowerArrayElementReference(IArrayElementReferenceOperation arrayRef, IMethodSymbol? currentMethod)
        {
            var args = new List<IExpression>();
            var array = LowerExpression(arrayRef.ArrayReference, currentMethod) ?? new Symbol(arrayRef.ArrayReference.Type?.Name ?? "arr");
            args.Add(array);
            
            foreach (var index in arrayRef.Indices)
            {
                var expr = LowerExpression(index, currentMethod) ?? new Symbol("idx");
                args.Add(expr);
            }
            
            return new Function("cs_array_get", args.ToArray());
        }

        private IExpression? LowerArrayCreation(IArrayCreationOperation creation, IMethodSymbol? currentMethod)
        {
            var elementType = creation.Type is IArrayTypeSymbol arrType ? arrType.ElementType.ToString() : "unknown";
            
            var args = new List<IExpression>();
            args.Add(new Symbol(elementType ?? "unknown"));
            
            foreach (var dimension in creation.DimensionSizes)
            {
                var expr = LowerExpression(dimension, currentMethod);
                if (expr != null) args.Add(expr);
            }
            
            return new Function("cs_new_array", args.ToArray());
        }

        private IExpression? LowerObjectCreation(IObjectCreationOperation creation, IMethodSymbol? currentMethod)
        {
            if (creation.Type == null) return null;
            
            var typeName = creation.Type.ToString(); // e.g. "System.Random"
            
            // Normalize generic types? e.g. List<T> -> List
            // For now, keep full string for precise matching.
            
            var args = new List<IExpression>();
            // Add type name as first arg
            args.Add(new Symbol(typeName ?? "unknown"));
            
            foreach(var arg in creation.Arguments)
            {
                var expr = LowerExpression(arg.Value, currentMethod);
                if (expr != null) args.Add(expr);
            }
            
            return new Function("cs_new", args.ToArray());
        }

        private IExpression? LowerBinary(IBinaryOperation binary, IMethodSymbol? currentMethod)
        {
            var left = LowerExpression(binary.LeftOperand, currentMethod);
            var right = LowerExpression(binary.RightOperand, currentMethod);
            if (left == null || right == null) return null;

            string? opName = GetBinaryOpName(binary);
            if (opName == null) return null;

            return new Function(opName, new[] { left, right });
        }

        private string? GetBinaryOpName(IBinaryOperation binary)
        {
            bool isComparison =
                binary.OperatorKind == BinaryOperatorKind.Equals ||
                binary.OperatorKind == BinaryOperatorKind.NotEquals ||
                binary.OperatorKind == BinaryOperatorKind.LessThan ||
                binary.OperatorKind == BinaryOperatorKind.LessThanOrEqual ||
                binary.OperatorKind == BinaryOperatorKind.GreaterThan ||
                binary.OperatorKind == BinaryOperatorKind.GreaterThanOrEqual;
            var typeSymbol = isComparison ? binary.LeftOperand.Type : binary.Type;
            var typeSuffix = GetTypeSuffix(typeSymbol);
            if (typeSuffix == null) return null;

            string? op = binary.OperatorKind switch
            {
                BinaryOperatorKind.Add => "add",
                BinaryOperatorKind.Subtract => "sub",
                BinaryOperatorKind.Multiply => "mul",
                BinaryOperatorKind.Divide => "div",
                BinaryOperatorKind.Remainder => "mod",
                BinaryOperatorKind.LeftShift => "shl",
                BinaryOperatorKind.RightShift => "shr",
                BinaryOperatorKind.And => "and",
                BinaryOperatorKind.Or => "or",
                BinaryOperatorKind.ExclusiveOr => "xor",
                BinaryOperatorKind.Equals => "eq",
                BinaryOperatorKind.NotEquals => "neq",
                BinaryOperatorKind.LessThan => "lt",
                BinaryOperatorKind.LessThanOrEqual => "lte",
                BinaryOperatorKind.GreaterThan => "gt",
                BinaryOperatorKind.GreaterThanOrEqual => "gte",
                _ => null
            };

            if (op == null) return null;

            if (op == "div" || op == "mod" || op == "xor" || op == "shl" || op == "shr" || op == "and" || op == "or" || isComparison)
            {
                return $"cs_{op}_{typeSuffix}";
            }

            // Default unspecified to unchecked to match most rules
            var context = GetOverflowContext(binary);
            string contextStr = context == CSharpOverflowContext.Checked ? "checked" : "unchecked";

            // Normalize unsigned arithmetic to "unchecked" for analysis stability.
            // The rule libraries model wrap-around hazards on unsigned arithmetic; Roslyn's IsChecked can be influenced
            // by compilation overflow settings, and we want consistent lowering for security/math diagnostics.
            if (typeSuffix is "u32" or "u64" or "nuint")
            {
                contextStr = "unchecked";
            }
            return $"cs_{op}_{typeSuffix}_{contextStr}";
        }

        private IExpression? LowerUnary(IUnaryOperation unary, IMethodSymbol? currentMethod)
        {
            var operand = LowerExpression(unary.Operand, currentMethod);
            if (operand == null) return null;
            
            var typeSuffix = GetTypeSuffix(unary.Type);
            if (typeSuffix == null) return null;

            string? op = unary.OperatorKind switch
            {
                UnaryOperatorKind.Minus => "neg",
                _ => null
            };

            if (op == null) return null;

            var context = GetOverflowContext(unary);
            string contextStr = context == CSharpOverflowContext.Checked ? "checked" : "unchecked";
            return new Function($"cs_{op}_{typeSuffix}_{contextStr}", new[] { operand });
        }

        private IExpression? LowerConversion(IConversionOperation conversion, IMethodSymbol? currentMethod)
        {
            var operand = LowerExpression(conversion.Operand, currentMethod);
            if (operand == null) return null;

            var targetType = GetTypeSuffix(conversion.Type);
            var sourceType = GetTypeSuffix(conversion.Operand.Type);

            if (targetType == null || sourceType == null) return null;

            var context = GetOverflowContext(conversion);
            string contextStr = context == CSharpOverflowContext.Checked ? "checked" : "unchecked";
            return new Function($"cs_conv_{sourceType}_to_{targetType}_{contextStr}", new[] { operand });
        }

        private IExpression? LowerInvocation(IInvocationOperation invocation, IMethodSymbol? currentMethod)
        {
            if (currentMethod != null && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, currentMethod))
            {
                return new Function("cs_recursion_detected", new[] { new Symbol(currentMethod.Name) });
            }

            var type = invocation.TargetMethod.ContainingType;
            if (type == null) return null;

            // Use full name for security types to avoid ambiguity
            string prefix = (type.Name == "Math" || type.Name == "MathF") ? "cs_math" : "cs_call";
            
            if (prefix == "cs_math")
            {
                string typeSuffix = "unknown";
                if (invocation.Arguments.Length > 0)
                {
                    typeSuffix = GetTypeSuffix(invocation.Arguments[0].Value.Type) ?? "unknown";
                }
                string methodName = $"{invocation.TargetMethod.Name}_{typeSuffix}";
                
                var mathArgs = new List<IExpression>();
                foreach (var arg in invocation.Arguments)
                {
                    var expr = LowerExpression(arg.Value, currentMethod);
                    if (expr == null) continue;
                    mathArgs.Add(expr);
                }
                return new Function($"{prefix}_{methodName}", mathArgs.ToArray());
            }
            else
            {
                var callArgs = new List<IExpression>();
                // Use full name for precise matching in rules
                callArgs.Add(new Symbol($"{type.ToString()}.{invocation.TargetMethod.Name}"));
                foreach (var arg in invocation.Arguments)
                {
                    var expr = LowerExpression(arg.Value, currentMethod) ?? new Symbol(arg.Value.Type?.Name ?? "arg");
                    callArgs.Add(expr);
                }
                return new Function("cs_call", callArgs.ToArray());
            }
        }

        private bool IsInterestingInvocationType(ITypeSymbol? type)
        {
            return true; // Lower all invocations now to be safe.
        }

        private string? GetTypeSuffix(ITypeSymbol? type)
        {
            if (type == null) return null;
            
            if (type.TypeKind == TypeKind.TypeParameter) return "gen";

            return type.SpecialType switch
            {
                SpecialType.System_Int32 => "i32",
                SpecialType.System_UInt32 => "u32",
                SpecialType.System_Int64 => "i64",
                SpecialType.System_UInt64 => "u64",
                SpecialType.System_Single => "f32",
                SpecialType.System_Double => "f64",
                SpecialType.System_Decimal => "dec",
                SpecialType.System_IntPtr => "nint",
                SpecialType.System_UIntPtr => "nuint",
                SpecialType.System_String => "str",
                SpecialType.System_Object => "obj",
                SpecialType.System_Boolean => "bool",
                _ => type.TypeKind == TypeKind.Class ? "obj" : null
            };
        }

        private bool IsNumeric(ITypeSymbol? type)
        {
            var suffix = GetTypeSuffix(type);
            return suffix != null && suffix != "str" && suffix != "obj" && suffix != "bool";
        }
    }
}
