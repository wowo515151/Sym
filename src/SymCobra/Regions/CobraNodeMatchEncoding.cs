using System;
using System.Collections.Generic;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using SymCobra.Core;

namespace SymCobra.Regions;

internal static class CobraNodeMatchEncoding
{
    internal const int MaxDirectNestedOperationDepth = 4;
    internal const int MaxCudaStructuralDirectNestedOperationDepth = 2;
    internal const int OtherCode = 1 << 30;
    internal const int ScalarMask = 1 << 0;
    internal const int ConstantMask = 1 << 1;
    internal const int VectorMask = 1 << 2;
    internal const int MatrixMask = 1 << 3;
    internal const int TensorMask = 1 << 4;
    internal const int BucketAdd = 0;
    internal const int BucketMul = 1;
    internal const int BucketMatMul = 2;
    internal const int BucketTranspose = 3;
    internal const int BucketRelu = 4;
    internal const int BucketEquality = 5;
    internal const int BucketVector = 6;
    internal const int BucketSymbol = 7;
    internal const int BucketNumber = 8;
    internal const int BucketOther = 9;
    internal const int ExactHeadAdd = 0;
    internal const int ExactHeadTensorAdd = 1;
    internal const int ExactHeadMul = 2;
    internal const int ExactHeadMultiply = 3;
    internal const int ExactHeadTensorMul = 4;
    internal const int ExactHeadMatMul = 5;
    internal const int ExactHeadFusedMatMulAdd = 6;
    internal const int ExactHeadFusedMatMulAddRelu = 7;
    internal const int ExactHeadTranspose = 8;
    internal const int ExactHeadRelu = 9;
    internal const int ExactHeadEquality = 10;
    internal const int ExactHeadVector = 11;

    internal static int[] BuildClassConstraintMasks(EGraph graph)
    {
        var masks = new int[graph.ClassCount];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            masks[classId] = EncodeConstraintMask(graph.GetClass(classId));
        }

        return masks;
    }

    internal static int[] BuildClassConstraintMasks(CobraGraphState graphState)
    {
        var masks = new int[graphState.ClassCount];
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            masks[classId] = EncodeConstraintMask(graphState, graphState.GetClass(classId));
        }

        return masks;
    }

    internal static int[] BuildClassHeadBucketMasks(EGraph graph)
    {
        var masks = new int[graph.ClassCount];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            int mask = 0;
            foreach (var node in graph.GetClass(classId).Nodes)
            {
                mask |= 1 << EncodeHeadBucket(node.Head);
            }

            masks[classId] = mask;
        }

        return masks;
    }

    internal static int[] BuildClassHeadBucketMasks(CobraGraphState graphState)
    {
        var masks = new int[graphState.ClassCount];
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            var cobraClass = graphState.GetClass(classId);
            int mask = 0;
            foreach (int nodeId in cobraClass.NodeIds)
            {
                mask |= 1 << EncodeHeadBucket(graphState.GetNode(nodeId).Head);
            }

            masks[classId] = mask;
        }

        return masks;
    }

    internal static int[] BuildClassExactHeadMasks(EGraph graph)
    {
        var masks = new int[graph.ClassCount];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            int mask = 0;
            foreach (var node in graph.GetClass(classId).Nodes)
            {
                mask |= EncodeExactHeadMask(node.Head);
            }

            masks[classId] = mask;
        }

        return masks;
    }

    internal static int[] BuildClassExactHeadMasks(CobraGraphState graphState)
    {
        var masks = new int[graphState.ClassCount];
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            var cobraClass = graphState.GetClass(classId);
            int mask = 0;
            foreach (int nodeId in cobraClass.NodeIds)
            {
                mask |= EncodeExactHeadMask(graphState.GetNode(nodeId).Head);
            }

            masks[classId] = mask;
        }

        return masks;
    }

    internal static int[] BuildClassChildEqualityMasks(EGraph graph)
    {
        var masks = new int[graph.ClassCount];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            int mask = 0;
            foreach (var node in graph.GetClass(classId).Nodes)
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    int left = graph.Find(node.Children[i]);
                    for (int j = i + 1; j < node.Children.Count; j++)
                    {
                        if (left == graph.Find(node.Children[j]))
                        {
                            mask |= EncodeChildEqualityPairMask(i, j);
                        }
                    }
                }
            }

            masks[classId] = mask;
        }

        return masks;
    }

    internal static int[] BuildClassChildEqualityMasks(CobraGraphState graphState)
    {
        var masks = new int[graphState.ClassCount];
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            var cobraClass = graphState.GetClass(classId);
            int mask = 0;
            foreach (int nodeId in cobraClass.NodeIds)
            {
                var node = graphState.GetNode(nodeId);
                for (int i = 0; i < node.CanonicalChildIds.Length; i++)
                {
                    int left = graphState.Find(node.CanonicalChildIds[i]);
                    for (int j = i + 1; j < node.CanonicalChildIds.Length; j++)
                    {
                        if (left == graphState.Find(node.CanonicalChildIds[j]))
                        {
                            mask |= EncodeChildEqualityPairMask(i, j);
                        }
                    }
                }
            }

            masks[classId] = mask;
        }

        return masks;
    }

    internal static int[] BuildClassChildAtomBucketMasks(EGraph graph)
    {
        var masks = new int[graph.ClassCount * 4];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            foreach (var node in graph.GetClass(classId).Nodes)
            {
                int maxChildren = Math.Min(node.Children.Count, 4);
                for (int childIndex = 0; childIndex < maxChildren; childIndex++)
                {
                    int grandchildClassId = graph.Find(node.Children[childIndex]);
                    foreach (var grandchild in graph.GetClass(grandchildClassId).Nodes)
                    {
                        masks[(classId * 4) + childIndex] |= 1 << EncodeHeadBucket(grandchild.Head);
                    }
                }
            }
        }

        return masks;
    }

    internal static int[] BuildClassChildAtomBucketMasks(CobraGraphState graphState)
    {
        var masks = new int[graphState.ClassCount * 4];
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            var cobraClass = graphState.GetClass(classId);
            foreach (int nodeId in cobraClass.NodeIds)
            {
                var node = graphState.GetNode(nodeId);
                int maxChildren = Math.Min(node.CanonicalChildIds.Length, 4);
                for (int childIndex = 0; childIndex < maxChildren; childIndex++)
                {
                    int grandchildClassId = graphState.Find(node.CanonicalChildIds[childIndex]);
                    foreach (int grandchildNodeId in graphState.GetClass(grandchildClassId).NodeIds)
                    {
                        masks[(classId * 4) + childIndex] |= 1 << EncodeHeadBucket(graphState.GetNode(grandchildNodeId).Head);
                    }
                }
            }
        }

        return masks;
    }

    internal static int[] BuildClassChildConstraintMasks(EGraph graph)
    {
        var masks = new int[graph.ClassCount * 4];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            foreach (var node in graph.GetClass(classId).Nodes)
            {
                int maxChildren = Math.Min(node.Children.Count, 4);
                for (int childIndex = 0; childIndex < maxChildren; childIndex++)
                {
                    int grandchildClassId = graph.Find(node.Children[childIndex]);
                    masks[(classId * 4) + childIndex] |= EncodeConstraintMask(graph.GetClass(grandchildClassId));
                }
            }
        }

        return masks;
    }

    internal static int[] BuildClassChildConstraintMasks(CobraGraphState graphState)
    {
        var masks = new int[graphState.ClassCount * 4];
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            var cobraClass = graphState.GetClass(classId);
            foreach (int nodeId in cobraClass.NodeIds)
            {
                var node = graphState.GetNode(nodeId);
                int maxChildren = Math.Min(node.CanonicalChildIds.Length, 4);
                for (int childIndex = 0; childIndex < maxChildren; childIndex++)
                {
                    int grandchildClassId = graphState.Find(node.CanonicalChildIds[childIndex]);
                    masks[(classId * 4) + childIndex] |= EncodeConstraintMask(graphState, graphState.GetClass(grandchildClassId));
                }
            }
        }

        return masks;
    }

    internal static int[] BuildClassChildReferenceBloomMasks(EGraph graph)
    {
        var masks = new int[graph.ClassCount * 4];
        for (int classId = 0; classId < graph.ClassCount; classId++)
        {
            foreach (var node in graph.GetClass(classId).Nodes)
            {
                int maxChildren = Math.Min(node.Children.Count, 4);
                for (int childIndex = 0; childIndex < maxChildren; childIndex++)
                {
                    int grandchildClassId = graph.Find(node.Children[childIndex]);
                    masks[(classId * 4) + childIndex] |= EncodeReferenceBloomBit(grandchildClassId);
                }
            }
        }

        return masks;
    }

    internal static int[] BuildClassChildReferenceBloomMasks(CobraGraphState graphState)
    {
        var masks = new int[graphState.ClassCount * 4];
        for (int classId = 0; classId < graphState.ClassCount; classId++)
        {
            var cobraClass = graphState.GetClass(classId);
            foreach (int nodeId in cobraClass.NodeIds)
            {
                var node = graphState.GetNode(nodeId);
                int maxChildren = Math.Min(node.CanonicalChildIds.Length, 4);
                for (int childIndex = 0; childIndex < maxChildren; childIndex++)
                {
                    int grandchildClassId = graphState.Find(node.CanonicalChildIds[childIndex]);
                    masks[(classId * 4) + childIndex] |= EncodeReferenceBloomBit(grandchildClassId);
                }
            }
        }

        return masks;
    }

    internal static int EncodeHeadCode(string head)
    {
        return head switch
        {
            "Add" or "TensorAdd" => 1,
            "Mul" or "Multiply" or "TensorMul" => 2,
            "MatMul" or "FusedMatMulAdd" or "FusedMatMulAddRelu" => 3,
            "Transpose" => 4,
            "Relu" => 5,
            "Equality" => 6,
            "Vector" => 7,
            "gt" => 10,
            "lt" => 11,
            "ge" => 12,
            "le" => 13,
            "and" => 14,
            "or" => 15,
            "not" => 16,
            "eq" => 17,
            "ne" => 18,
            var value when value.StartsWith("Sym:", StringComparison.Ordinal) => 8,
            var value when value.StartsWith("Num:", StringComparison.Ordinal) => 9,
            _ => OtherCode
        };
    }

    internal static IReadOnlyList<FlatArgumentInfo> BuildFlatArgumentInfos(IExpression pattern)
    {
        if (pattern is not Operation op)
        {
            return [];
        }

        var firstPositions = new Dictionary<string, int>(StringComparer.Ordinal);
        var infos = new FlatArgumentInfo[op.Arguments.Count];
        for (int i = 0; i < op.Arguments.Count; i++)
        {
            if (op.Arguments[i] is Wild wild)
            {
                if (!firstPositions.TryGetValue(wild.Name, out int firstPosition))
                {
                    firstPosition = i;
                    firstPositions[wild.Name] = i;
                }

                infos[i] = new FlatArgumentInfo(firstPosition, EncodeConstraintMask(wild.Constraint), 0, 0, 0, 0);
            }
            else if (op.Arguments[i] is Atom atom)
            {
                infos[i] = new FlatArgumentInfo(i, 0, 1, EncodeHeadBucket(atom.Head), EncodeExactHeadMask(atom.Head), 0);
            }
            else if (op.Arguments[i] is Operation childOp)
            {
                infos[i] = new FlatArgumentInfo(
                    i,
                    0,
                    1,
                    EncodeHeadBucket(childOp.Head),
                    EncodeExactHeadMask(childOp.Head),
                    IsOneLevelDirectChildPattern(childOp) ? EncodeNestedRepeatMask(childOp) : 0);
            }
            else
            {
                infos[i] = new FlatArgumentInfo(i, 0, 2, BucketOther, 0, 0);
            }
        }

        return infos;
    }

    internal static int[] BuildNestedAtomBucketMasks(IExpression pattern)
    {
        if (pattern is not Operation op)
        {
            return [];
        }

        var masks = new int[op.Arguments.Count * 4];
        for (int i = 0; i < op.Arguments.Count; i++)
        {
            if (op.Arguments[i] is not Operation childOp || !IsOneLevelDirectChildPattern(childOp))
            {
                continue;
            }

            int maxChildren = Math.Min(childOp.Arguments.Count, 4);
            for (int childIndex = 0; childIndex < maxChildren; childIndex++)
            {
                if (childOp.Arguments[childIndex] is Atom atom)
                {
                    masks[(i * 4) + childIndex] = 1 << EncodeHeadBucket(atom.Head);
                }
            }
        }

        return masks;
    }

    internal static int[] BuildNestedConstraintMasks(IExpression pattern)
    {
        if (pattern is not Operation op)
        {
            return [];
        }

        var masks = new int[op.Arguments.Count * 4];
        for (int i = 0; i < op.Arguments.Count; i++)
        {
            if (op.Arguments[i] is not Operation childOp || !IsOneLevelDirectChildPattern(childOp))
            {
                continue;
            }

            int maxChildren = Math.Min(childOp.Arguments.Count, 4);
            for (int childIndex = 0; childIndex < maxChildren; childIndex++)
            {
                if (childOp.Arguments[childIndex] is Wild wild)
                {
                    masks[(i * 4) + childIndex] = EncodeConstraintMask(wild.Constraint);
                }
            }
        }

        return masks;
    }

    internal static int[] BuildNestedTopLevelReferenceMasks(IExpression pattern)
    {
        if (pattern is not Operation op)
        {
            return [];
        }

        var topLevelWilds = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < op.Arguments.Count; i++)
        {
            if (op.Arguments[i] is Wild wild && !topLevelWilds.ContainsKey(wild.Name))
            {
                topLevelWilds[wild.Name] = i;
            }
        }

        var masks = new int[op.Arguments.Count * 4];
        Array.Fill(masks, -1);
        for (int i = 0; i < op.Arguments.Count; i++)
        {
            if (op.Arguments[i] is not Operation childOp || !IsOneLevelDirectChildPattern(childOp))
            {
                continue;
            }

            int maxChildren = Math.Min(childOp.Arguments.Count, 4);
            for (int childIndex = 0; childIndex < maxChildren; childIndex++)
            {
                if (childOp.Arguments[childIndex] is Wild wild &&
                    topLevelWilds.TryGetValue(wild.Name, out int topLevelIndex))
                {
                    masks[(i * 4) + childIndex] = topLevelIndex;
                }
            }
        }

        return masks;
    }

    internal static bool IsOneLevelDirectPattern(IExpression pattern)
    {
        return pattern is Operation op && op.Arguments.All(IsOneLevelDirectArgument);
    }

    internal static bool IsBoundedDirectPattern(IExpression pattern, int maxNestedOperationDepth)
    {
        return pattern is Operation op && op.Arguments.All(arg => IsBoundedDirectArgument(arg, maxNestedOperationDepth));
    }

    internal static bool IsOneLevelDirectChildPattern(Operation op)
    {
        return op.Arguments.All(arg => arg is Wild || arg is Atom);
    }

    private static bool IsBoundedDirectArgument(IExpression arg, int remainingNestedOperationDepth)
    {
        return arg switch
        {
            Wild => true,
            Atom => true,
            Operation op when remainingNestedOperationDepth > 0 => op.Arguments.All(child => IsBoundedDirectArgument(child, remainingNestedOperationDepth - 1)),
            _ => false
        };
    }

    private static bool IsOneLevelDirectArgument(IExpression arg)
    {
        return arg switch
        {
            Wild => true,
            Atom => true,
            Operation op => IsOneLevelDirectChildPattern(op),
            _ => false
        };
    }

    internal static bool IsCompatible(
        int nodeHeadCode,
        int nodeArity,
        int ruleHeadCode,
        int ruleArity,
        int wildcardFlag,
        int directWildcardFlag,
        int nodeChildStart,
        int[] nodeChildIds,
        int ruleArgStart,
        int[] ruleArgGroupIds,
        int[] ruleArgConstraintMasks,
        int[] classConstraintMasks,
        int[] classHeadBucketMasks,
        int[] classExactHeadMasks,
        int[] classChildEqualityMasks,
        int[] classChildAtomBucketMasks,
        int[] classChildConstraintMasks,
        int[] classChildReferenceBloomMasks,
        int[] ruleArgKinds,
        int[] ruleArgHeadBuckets,
        int[] ruleArgExactHeadMasks,
        int[] ruleArgNestedRepeatMasks,
        int[] ruleArgNestedAtomBucketMasks,
        int[] ruleArgNestedConstraintMasks,
        int[] ruleArgNestedTopLevelReferenceMasks)
    {
        if (wildcardFlag != 0)
        {
            return true;
        }

        if (ruleHeadCode == OtherCode || nodeHeadCode == OtherCode)
        {
            return true;
        }

        if (nodeHeadCode != ruleHeadCode || nodeArity != ruleArity)
        {
            return false;
        }

        if (directWildcardFlag != 0)
        {
            for (int i = 0; i < ruleArity; i++)
            {
                int childClassId = nodeChildIds[nodeChildStart + i];
                int kind = ruleArgKinds[ruleArgStart + i];
                if (kind == 1)
                {
                    int exactHeadMask = ruleArgExactHeadMasks[ruleArgStart + i];
                    int nestedRepeatMask = ruleArgNestedRepeatMasks[ruleArgStart + i];
                    if (exactHeadMask != 0)
                    {
                        if ((classExactHeadMasks[childClassId] & exactHeadMask) == 0)
                        {
                            return false;
                        }

                        if (nestedRepeatMask != 0 &&
                            (classChildEqualityMasks[childClassId] & nestedRepeatMask) != nestedRepeatMask)
                        {
                            return false;
                        }

                        for (int childIndex = 0; childIndex < 4; childIndex++)
                        {
                            int requiredBucketMask = ruleArgNestedAtomBucketMasks[((ruleArgStart + i) * 4) + childIndex];
                            if (requiredBucketMask != 0 &&
                                (classChildAtomBucketMasks[(childClassId * 4) + childIndex] & requiredBucketMask) == 0)
                            {
                                return false;
                            }

                            int requiredConstraintMask = ruleArgNestedConstraintMasks[((ruleArgStart + i) * 4) + childIndex];
                            if (requiredConstraintMask != 0 &&
                                (classChildConstraintMasks[(childClassId * 4) + childIndex] & requiredConstraintMask) == 0)
                            {
                                return false;
                            }

                            int topLevelRefIndex = ruleArgNestedTopLevelReferenceMasks[((ruleArgStart + i) * 4) + childIndex];
                            if (topLevelRefIndex >= 0)
                            {
                                int requiredChildClassId = nodeChildIds[nodeChildStart + topLevelRefIndex];
                                int requiredBloomBit = EncodeReferenceBloomBit(requiredChildClassId);
                                if ((classChildReferenceBloomMasks[(childClassId * 4) + childIndex] & requiredBloomBit) == 0)
                                {
                                    return false;
                                }
                            }
                        }

                        continue;
                    }

                    int headBucket = ruleArgHeadBuckets[ruleArgStart + i];
                    if ((classHeadBucketMasks[childClassId] & (1 << headBucket)) == 0)
                    {
                        return false;
                    }

                    continue;
                }

                int requiredMask = ruleArgConstraintMasks[ruleArgStart + i];
                if (requiredMask != 0 && (classConstraintMasks[childClassId] & requiredMask) == 0)
                {
                    return false;
                }

                int groupId = ruleArgGroupIds[ruleArgStart + i];
                if (groupId < i && childClassId != nodeChildIds[nodeChildStart + groupId])
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static int EncodeConstraintMask(WildConstraint constraint)
    {
        return constraint switch
        {
            WildConstraint.None => 0,
            WildConstraint.Scalar => ScalarMask,
            WildConstraint.Constant => ConstantMask,
            WildConstraint.Vector => VectorMask,
            WildConstraint.Matrix => MatrixMask,
            WildConstraint.Tensor => TensorMask,
            _ => 0
        };
    }

    internal static int EncodeReferenceBloomBit(int classId)
    {
        int bucket = Math.Abs(classId % 30);
        return 1 << bucket;
    }

    internal static int EncodeExactHeadMask(string head)
    {
        int bit = head switch
        {
            "Add" => ExactHeadAdd,
            "TensorAdd" => ExactHeadTensorAdd,
            "Mul" => ExactHeadMul,
            "Multiply" => ExactHeadMultiply,
            "TensorMul" => ExactHeadTensorMul,
            "MatMul" => ExactHeadMatMul,
            "FusedMatMulAdd" => ExactHeadFusedMatMulAdd,
            "FusedMatMulAddRelu" => ExactHeadFusedMatMulAddRelu,
            "Transpose" => ExactHeadTranspose,
            "Relu" => ExactHeadRelu,
            "Equality" => ExactHeadEquality,
            "Vector" => ExactHeadVector,
            _ => -1
        };

        return bit >= 0 ? 1 << bit : 0;
    }

    internal static int EncodeChildEqualityPairMask(int leftIndex, int rightIndex)
    {
        return (leftIndex, rightIndex) switch
        {
            (0, 1) => 1 << 0,
            (0, 2) => 1 << 1,
            (0, 3) => 1 << 2,
            (1, 2) => 1 << 3,
            (1, 3) => 1 << 4,
            (2, 3) => 1 << 5,
            _ => 0
        };
    }

    private static int EncodeNestedRepeatMask(Operation childOp)
    {
        var firstPositions = new Dictionary<string, int>(StringComparer.Ordinal);
        int mask = 0;
        for (int i = 0; i < childOp.Arguments.Count; i++)
        {
            if (childOp.Arguments[i] is not Wild wild)
            {
                continue;
            }

            if (!firstPositions.TryGetValue(wild.Name, out int firstPosition))
            {
                firstPositions[wild.Name] = i;
                continue;
            }

            mask |= EncodeChildEqualityPairMask(firstPosition, i);
        }

        return mask;
    }

    internal static int EncodeConstraintMask(EClass eClass)
    {
        int mask = 0;

        if (eClass.Data is Shape shape && shape.IsValid)
        {
            if (shape.IsScalar) mask |= ScalarMask;
            if (shape.IsVector) mask |= VectorMask | TensorMask;
            if (shape.IsMatrix) mask |= MatrixMask | TensorMask;
            if (shape.IsTensor) mask |= TensorMask;
        }

        foreach (var node in eClass.Nodes)
        {
            bool isNumber = node.Head.StartsWith("Num:", StringComparison.Ordinal);
            bool isMatrix = node.Head == "Matrix" || node.Head == "MatMul" || node.Head == "FusedMatMulAdd" || node.Head == "FusedMatMulAddRelu";
            bool isVector = node.Head == "Vector";
            bool isTensorOp = node.Head == "Conv2D" || node.Head == "FusedConv2DRelu" || node.Head == "TensorAdd" || node.Head == "TensorMul" || node.Head == "Transpose" || node.Head == "Relu";
            bool isTensor = isMatrix || isVector || isTensorOp;

            if (isNumber) mask |= ConstantMask | ScalarMask;
            if (!isTensor) mask |= ScalarMask;
            if (isVector) mask |= VectorMask | TensorMask;
            if (isMatrix) mask |= MatrixMask | TensorMask;
            if (isTensorOp) mask |= TensorMask;
        }

        return mask;
    }

    internal static int EncodeConstraintMask(CobraGraphState graphState, CobraClassRecord cobraClass)
    {
        int mask = 0;

        if (cobraClass.Metadata.TryGetValue("shape", out object? shapeObject) &&
            shapeObject is Shape shape &&
            shape.IsValid)
        {
            if (shape.IsScalar) mask |= ScalarMask;
            if (shape.IsVector) mask |= VectorMask | TensorMask;
            if (shape.IsMatrix) mask |= MatrixMask | TensorMask;
            if (shape.IsTensor) mask |= TensorMask;
        }

        foreach (int nodeId in cobraClass.NodeIds)
        {
            var node = graphState.GetNode(nodeId);
            bool isNumber = node.Head.StartsWith("Num:", StringComparison.Ordinal);
            bool isMatrix = node.Head == "Matrix" || node.Head == "MatMul" || node.Head == "FusedMatMulAdd" || node.Head == "FusedMatMulAddRelu";
            bool isVector = node.Head == "Vector";
            bool isTensorOp = node.Head == "Conv2D" || node.Head == "FusedConv2DRelu" || node.Head == "TensorAdd" || node.Head == "TensorMul" || node.Head == "Transpose" || node.Head == "Relu";
            bool isTensor = isMatrix || isVector || isTensorOp;

            if (isNumber) mask |= ConstantMask | ScalarMask;
            if (!isTensor) mask |= ScalarMask;
            if (isVector) mask |= VectorMask | TensorMask;
            if (isMatrix) mask |= MatrixMask | TensorMask;
            if (isTensorOp) mask |= TensorMask;
        }

        return mask;
    }

    internal static int EncodeHeadBucket(string head)
    {
        return head switch
        {
            "Add" or "TensorAdd" => BucketAdd,
            "Mul" or "Multiply" or "TensorMul" => BucketMul,
            "MatMul" or "FusedMatMulAdd" or "FusedMatMulAddRelu" => BucketMatMul,
            "Transpose" => BucketTranspose,
            "Relu" => BucketRelu,
            "Equality" => BucketEquality,
            "Vector" => BucketVector,
            var value when value.StartsWith("Sym:", StringComparison.Ordinal) => BucketSymbol,
            var value when value.StartsWith("Num:", StringComparison.Ordinal) => BucketNumber,
            _ => BucketOther
        };
    }

    internal readonly record struct FlatArgumentInfo(int GroupId, int ConstraintMask, int Kind, int HeadBucket, int ExactHeadMask, int NestedRepeatMask);
}
