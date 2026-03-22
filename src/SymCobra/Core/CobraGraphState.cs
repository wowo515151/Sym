using System;
using System.Collections.Generic;
using System.Linq;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;
using SymCobra.Regions;

namespace SymCobra.Core;

public class CobraGraphState
{
    private readonly List<CobraClassRecord> _classes = new();
    private readonly List<CobraNodeRecord> _nodes = new();
    private readonly List<int> _parents = new();
    private readonly Dictionary<(string Head, string ChildrenKey), int> _hashCons = new();
    private bool _suspendLegacyTracking;

    public CobraGraphSyncState SyncState { get; } = new();

    public IReadOnlyList<CobraClassRecord> Classes => _classes;
    public IReadOnlyList<CobraNodeRecord> Nodes => _nodes;

    public int ClassCount => _classes.Count;
    public int NodeCount => _nodes.Count;

    public int AddClass()
    {
        int classId = _classes.Count;
        _classes.Add(new CobraClassRecord(classId));
        _parents.Add(classId);
        MarkLegacyClassDirty(classId);
        SyncState.Epoch++;
        return classId;
    }

    public void EnsureClassCount(int count)
    {
        bool added = false;
        while (_classes.Count < count)
        {
            int newId = _classes.Count;
            _classes.Add(new CobraClassRecord(newId));
            _parents.Add(newId);
            MarkLegacyClassDirty(newId);
            added = true;
        }

        if (added)
        {
            SyncState.Epoch++;
        }
    }

    public int AddClass(int classId)
    {
        EnsureClassCount(classId + 1);
        return classId;
    }

    public int AddExpression(IExpression expression)
    {
        if (expression is null) throw new ArgumentNullException(nameof(expression));

        int[] children = expression is Operation operation
            ? operation.Arguments.Select(AddExpression).ToArray()
            : Array.Empty<int>();

        string head = ENode.GetHead(expression);
        int headCode = CobraNodeMatchEncoding.EncodeHeadCode(head);
        string? literal = expression switch
        {
            Symbol symbol => $"Sym:{symbol.Name}",
            Number number => $"Num:{number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            _ when headCode == 8 || headCode == 9 => head,
            _ => null
        };

        int classId = AddClass();
        int nodeId = AddNode(head, headCode, children, classId, literal);
        int canonicalClassId = Find(_nodes[nodeId].ClassId);
        ApplyExpressionMetadata(canonicalClassId, expression);
        return canonicalClassId;
    }

    private string GetChildrenKey(int[] children)
    {
        if (children == null || children.Length == 0) return string.Empty;
        return string.Join(",", children.Select(Find));
    }

    public int AddNode(string head, int headCode, int[] canonicalChildIds, int classId, string? literal = null)
    {
        for (int i = 0; i < canonicalChildIds.Length; i++)
        {
            canonicalChildIds[i] = Find(canonicalChildIds[i]);
        }

        string childrenKey = GetChildrenKey(canonicalChildIds);
        if (_hashCons.TryGetValue((head, childrenKey), out int existingNodeId))
        {
            int existingClassId = _nodes[existingNodeId].ClassId;
            Union(classId, existingClassId);
            return existingNodeId;
        }

        int nodeId = _nodes.Count;
        var node = new CobraNodeRecord(head, headCode, canonicalChildIds, classId, literal);
        _nodes.Add(node);
        _hashCons[(head, childrenKey)] = nodeId;

        GetClass(classId).NodeIds.Add(nodeId);
        foreach (int childId in canonicalChildIds)
        {
            GetClass(childId).ParentNodeIds.Add(nodeId);
        }

        MarkLegacyClassDirty(classId);
        SyncState.Epoch++;
        return nodeId;
    }

    public CobraClassRecord GetClass(int classId)
    {
        return _classes[Find(classId)];
    }

    public CobraNodeRecord GetNode(int nodeId)
    {
        return _nodes[nodeId];
    }

    public bool HasMaterializedNodes(int classId)
    {
        int rootId = Find(classId);
        return _classes[rootId].NodeIds.Count > 0;
    }

    public int Find(int classId)
    {
        int root = classId;
        while (root != _parents[root])
        {
            root = _parents[root];
        }

        int curr = classId;
        while (curr != root)
        {
            int next = _parents[curr];
            _parents[curr] = root;
            curr = next;
        }

        return root;
    }

    public void Union(int classId1, int classId2)
    {
        int root1 = Find(classId1);
        int root2 = Find(classId2);
        if (root1 == root2)
        {
            return;
        }

        if (ShouldSwapUnionRoots(root1, root2))
        {
            (root1, root2) = (root2, root1);
        }

        MergeClassRecords(root1, root2);
        _parents[root2] = root1;
        SyncState.DirtyRebuildClasses.Add(root1);
        MarkLegacyClassDirty(root1);
        MarkLegacyClassDirty(root2);
        SyncState.Epoch++;
    }

    public int[] GetParentSnapshot()
    {
        int[] snapshot = new int[_parents.Count];
        for (int i = 0; i < _parents.Count; i++)
        {
            snapshot[i] = _parents[i];
        }

        return snapshot;
    }

    public EGraphRepairSnapshot GetRepairSnapshot()
    {
        var candidates = new List<EGraphRepairCandidate>();
        var childStarts = new List<int>();
        var childCounts = new List<int>();
        var childIds = new List<int>();

        for (int classId = 0; classId < _classes.Count; classId++)
        {
            if (Find(classId) != classId)
            {
                continue;
            }

            foreach (int nodeId in _classes[classId].NodeIds)
            {
                var node = _nodes[nodeId];
                childStarts.Add(childIds.Count);
                childCounts.Add(node.CanonicalChildIds.Length);
                foreach (int childId in node.CanonicalChildIds)
                {
                    childIds.Add(childId);
                }

                candidates.Add(new EGraphRepairCandidate(
                    classId,
                    new ENode(node.Head, System.Collections.Immutable.ImmutableList.CreateRange(node.CanonicalChildIds))));
            }
        }

        return new EGraphRepairSnapshot(
            GetParentSnapshot(),
            childStarts.ToArray(),
            childCounts.ToArray(),
            childIds.ToArray(),
            candidates);
    }

    public void Rebuild(
        IReadOnlyList<int>? prioritizedRootIds,
        IReadOnlyList<IReadOnlyList<EGraphRepairCandidate>>? exactCandidateGroups,
        System.Threading.CancellationToken ct = default)
    {
        bool usedExactCandidates = false;
        while (SyncState.DirtyRebuildClasses.Count > 0 || (!usedExactCandidates && exactCandidateGroups is { Count: > 0 }))
        {
            ct.ThrowIfCancellationRequested();
            SyncState.DirtyRebuildClasses.Clear();

            if (!usedExactCandidates && exactCandidateGroups is { Count: > 0 })
            {
                Repair(prioritizedRootIds, exactCandidateGroups, ct);
                usedExactCandidates = true;
            }
            else
            {
                Repair(prioritizedRootIds, exactCandidateGroups: null, ct);
            }
        }

        for (int classId = 0; classId < _parents.Count; classId++)
        {
            Find(classId);
        }

        ReindexActiveGraph();
    }

    public void MarkLegacyGraphSynchronized()
    {
        SyncState.LastLegacySyncedClassCount = _classes.Count;
        SyncState.LastLegacySyncedNodeCount = _nodes.Count;
        SyncState.DirtyLegacyClassIds.Clear();
        SyncState.LastFullSyncEpoch = SyncState.Epoch;
    }

    public void SyncFromLegacyGraph(EGraph legacyGraph)
    {
        _suspendLegacyTracking = true;
        try
        {
            _classes.Clear();
            _nodes.Clear();
            _parents.Clear();
            _hashCons.Clear();

            for (int classId = 0; classId < legacyGraph.ClassCount; classId++)
            {
                var eClass = legacyGraph.GetClass(classId);
                var record = new CobraClassRecord(classId)
                {
                    RepresentativeNodeId = eClass.Parent,
                    Generation = eClass.Generation
                };

                foreach (var metadata in eClass.Metadata)
                {
                    record.Metadata[metadata.Key] = metadata.Value;
                }

                if (eClass.Data is Shape shape && shape.IsValid)
                {
                    record.Metadata["shape"] = shape;
                }

                _classes.Add(record);
                _parents.Add(eClass.Parent);
            }

            foreach (int classId in legacyGraph.GetRootIds())
            {
                var eClass = legacyGraph.GetClass(classId);
                foreach (var node in eClass.Nodes)
                {
                    int headCode = CobraNodeMatchEncoding.EncodeHeadCode(node.Head);
                    string? literal = headCode is 8 or 9 ? node.Head : null;
                    int[] children = new int[node.Children.Count];
                    for (int i = 0; i < children.Length; i++)
                    {
                        children[i] = legacyGraph.Find(node.Children[i]);
                    }

                    AddNode(node.Head, headCode, children, classId, literal);
                }
            }
        }
        finally
        {
            _suspendLegacyTracking = false;
        }

        SyncState.DirtyLegacyClassIds.Clear();
        SyncState.LastLegacySyncedClassCount = _classes.Count;
        SyncState.LastLegacySyncedNodeCount = _nodes.Count;
        SyncState.Epoch++;
        SyncState.LastFullSyncEpoch = SyncState.Epoch;
    }

    public void SyncClassMetadataFromLegacy(EGraph legacyGraph, IReadOnlyList<int>? classIds = null)
    {
        if (legacyGraph is null) throw new ArgumentNullException(nameof(legacyGraph));

        IEnumerable<int> sourceIds = classIds ?? Enumerable.Range(0, Math.Min(_classes.Count, legacyGraph.ClassCount));
        var seenRoots = new HashSet<int>();
        bool changed = false;

        foreach (int classId in sourceIds)
        {
            if (classId < 0 || classId >= legacyGraph.ClassCount || classId >= _classes.Count)
            {
                continue;
            }

            int cobraRoot = Find(classId);
            if (!seenRoots.Add(cobraRoot))
            {
                continue;
            }

            int legacyRoot = legacyGraph.Find(classId);
            var legacyClass = legacyGraph.GetClass(legacyRoot);
            var cobraClass = _classes[cobraRoot];

            if (cobraClass.Generation != legacyClass.Generation)
            {
                cobraClass.Generation = legacyClass.Generation;
                changed = true;
            }

            foreach (var metadata in legacyClass.Metadata)
            {
                if (metadata.Value is double value &&
                    (!cobraClass.Metadata.TryGetValue(metadata.Key, out object? existing) || !Equals(existing, value)))
                {
                    cobraClass.Metadata[metadata.Key] = value;
                    changed = true;
                }
            }

            if (legacyClass.Data is Shape shape && shape.IsValid)
            {
                if (!cobraClass.Metadata.TryGetValue("shape", out object? existingShape) || !Equals(existingShape, shape))
                {
                    cobraClass.Metadata["shape"] = shape;
                    changed = true;
                }
            }
            else if (cobraClass.Metadata.Remove("shape"))
            {
                changed = true;
            }
        }

        if (changed)
        {
            SyncState.Epoch++;
        }
    }

    public void SyncLegacyGraphFromCobra(EGraph legacyGraph, bool forceFull = false)
    {
        if (legacyGraph is null) throw new ArgumentNullException(nameof(legacyGraph));

        if (forceFull)
        {
            SyncState.DirtyLegacyClassIds.Clear();
            for (int classId = 0; classId < _classes.Count; classId++)
            {
                SyncState.DirtyLegacyClassIds.Add(classId);
            }

            SyncState.LastLegacySyncedNodeCount = 0;
        }

        while (legacyGraph.ClassCount < _classes.Count)
        {
            legacyGraph.AddClass();
        }

        foreach (int classId in SyncState.DirtyLegacyClassIds.OrderBy(static id => id))
        {
            int cobraRoot = Find(classId);
            int legacyRoot = legacyGraph.Find(classId);
            if (cobraRoot != legacyRoot)
            {
                legacyGraph.Union(cobraRoot, classId);
            }

            CopyClassMetadataToLegacy(legacyGraph, classId);
            if (cobraRoot != classId)
            {
                CopyClassMetadataToLegacy(legacyGraph, cobraRoot);
            }
        }

        for (int nodeId = Math.Max(0, SyncState.LastLegacySyncedNodeCount); nodeId < _nodes.Count; nodeId++)
        {
            var node = _nodes[nodeId];
            var eNode = new ENode(node.Head, System.Collections.Immutable.ImmutableList.CreateRange(node.CanonicalChildIds));
            int legacyClassId = legacyGraph.AddNode(eNode);
            MirrorLegacyAliasClass(legacyClassId, Find(node.ClassId));
            legacyGraph.Union(Find(node.ClassId), legacyClassId);
        }

        SyncState.LastLegacySyncedClassCount = _classes.Count;
        SyncState.LastLegacySyncedNodeCount = _nodes.Count;
        SyncState.DirtyLegacyClassIds.Clear();
        SyncState.LastFullSyncEpoch = SyncState.Epoch;
    }

    public void SyncToLegacyGraph(EGraph legacyGraph)
    {
        SyncLegacyGraphFromCobra(legacyGraph);
    }

    public bool UpdateClassShape(int classId, Shape shape)
    {
        int rootId = Find(classId);
        var cobraClass = _classes[rootId];
        bool hadShape = cobraClass.Metadata.TryGetValue("shape", out object? existingShapeObject) &&
                        existingShapeObject is Shape existingShape &&
                        existingShape.IsValid;
        if (hadShape && Equals(existingShapeObject, shape))
        {
            return false;
        }

        cobraClass.Metadata["shape"] = shape;
        cobraClass.Generation++;
        MarkLegacyClassDirty(rootId);
        SyncState.Epoch++;
        return true;
    }

    private void MergeClassRecords(int survivingRootId, int absorbedRootId)
    {
        var surviving = _classes[survivingRootId];
        var absorbed = _classes[absorbedRootId];
        bool survivingHadNodes = surviving.NodeIds.Count > 0;
        HashSet<int>? survivingNodeIds = surviving.NodeIds.Count > 8 ? surviving.NodeIds.ToHashSet() : null;
        HashSet<int>? survivingParentNodeIds = surviving.ParentNodeIds.Count > 8 ? surviving.ParentNodeIds.ToHashSet() : null;

        foreach (int nodeId in absorbed.NodeIds)
        {
            if (survivingNodeIds?.Add(nodeId) ?? !surviving.NodeIds.Contains(nodeId))
            {
                surviving.NodeIds.Add(nodeId);
            }

            var node = _nodes[nodeId];
            node.ClassId = survivingRootId;
            _nodes[nodeId] = node;
        }

        foreach (int parentNodeId in absorbed.ParentNodeIds)
        {
            if (survivingParentNodeIds?.Add(parentNodeId) ?? !surviving.ParentNodeIds.Contains(parentNodeId))
            {
                surviving.ParentNodeIds.Add(parentNodeId);
            }
        }

        foreach (var metadata in absorbed.Metadata)
        {
            surviving.Metadata[metadata.Key] = metadata.Value;
        }

        if (!survivingHadNodes && absorbed.NodeIds.Count > 0)
        {
            surviving.RepresentativeNodeId = absorbed.RepresentativeNodeId;
        }

        surviving.Generation = Math.Max(surviving.Generation, absorbed.Generation);
    }

    private bool ShouldSwapUnionRoots(int candidateRoot1, int candidateRoot2)
    {
        var class1 = _classes[candidateRoot1];
        var class2 = _classes[candidateRoot2];

        if (class1.NodeIds.Count != class2.NodeIds.Count)
        {
            return class1.NodeIds.Count < class2.NodeIds.Count;
        }

        if (class1.Generation != class2.Generation)
        {
            return class1.Generation < class2.Generation;
        }

        return candidateRoot1 > candidateRoot2;
    }

    private void ApplyExpressionMetadata(int classId, IExpression expression)
    {
        if (_suspendLegacyTracking)
        {
            return;
        }

        if (expression.Shape.IsValid && !expression.Shape.IsWildcardShape)
        {
            GetClass(classId).Metadata["shape"] = expression.Shape;
            MarkLegacyClassDirty(classId);
        }
    }

    private void MarkLegacyClassDirty(int classId)
    {
        if (_suspendLegacyTracking || classId < 0 || classId >= _classes.Count)
        {
            return;
        }

        SyncState.DirtyLegacyClassIds.Add(Find(classId));
    }

    private void Repair(
        IReadOnlyList<int>? prioritizedRootIds,
        IReadOnlyList<IReadOnlyList<EGraphRepairCandidate>>? exactCandidateGroups,
        System.Threading.CancellationToken ct)
    {
        var todo = new List<(int ClassId, int NodeId, CobraNodeRecord NewNode)>();
        var visitedClasses = new HashSet<int>();

        if (exactCandidateGroups is { Count: > 0 })
        {
            var groupedTodo = new List<List<(int ClassId, int NodeId, CobraNodeRecord NewNode)>>();
            foreach (var group in exactCandidateGroups)
            {
                ct.ThrowIfCancellationRequested();
                if (group == null || group.Count == 0)
                {
                    continue;
                }

                var todoGroup = new List<(int ClassId, int NodeId, CobraNodeRecord NewNode)>();
                foreach (var candidate in group)
                {
                    ct.ThrowIfCancellationRequested();
                    int rootId = Find(candidate.ClassId);
                    if (_parents[rootId] != rootId)
                    {
                        continue;
                    }

                    int nodeId = FindMatchingNodeId(rootId, candidate.Node);
                    if (nodeId < 0)
                    {
                        continue;
                    }

                    var node = _nodes[nodeId];
                    var canonical = CanonicalizeNode(node);
                    if (!NodesMatch(node, canonical))
                    {
                        todoGroup.Add((rootId, nodeId, canonical));
                    }
                }

                if (todoGroup.Count > 0)
                {
                    groupedTodo.Add(todoGroup);
                }
            }

            ApplyGroupedRepairs(groupedTodo);
            return;
        }

        if (prioritizedRootIds != null)
        {
            foreach (int prioritizedId in prioritizedRootIds)
            {
                ct.ThrowIfCancellationRequested();
                int rootId = Find(prioritizedId);
                if (_parents[rootId] != rootId || !visitedClasses.Add(rootId))
                {
                    continue;
                }

                foreach (int nodeId in _classes[rootId].NodeIds.ToArray())
                {
                    var node = _nodes[nodeId];
                    var canonical = CanonicalizeNode(node);
                    if (!NodesMatch(node, canonical))
                    {
                        todo.Add((rootId, nodeId, canonical));
                    }
                }
            }
        }

        for (int classId = 0; classId < _classes.Count; classId++)
        {
            ct.ThrowIfCancellationRequested();
            int rootId = Find(classId);
            if (_parents[rootId] != rootId || !visitedClasses.Add(rootId))
            {
                continue;
            }

            foreach (int nodeId in _classes[rootId].NodeIds.ToArray())
            {
                var node = _nodes[nodeId];
                var canonical = CanonicalizeNode(node);
                if (!NodesMatch(node, canonical))
                {
                    todo.Add((rootId, nodeId, canonical));
                }
            }
        }

        foreach (var item in todo)
        {
            ApplyRepairGroup([item]);
        }
    }

    private void ApplyGroupedRepairs(IReadOnlyList<List<(int ClassId, int NodeId, CobraNodeRecord NewNode)>> groupedTodo)
    {
        foreach (var group in groupedTodo)
        {
            if (group.Count == 0)
            {
                continue;
            }

            ApplyRepairGroup(group);
        }
    }

    private void ApplyRepairGroup(IReadOnlyList<(int ClassId, int NodeId, CobraNodeRecord NewNode)> group)
    {
        var rootsToMerge = new List<int>(group.Count);
        for (int i = 0; i < group.Count; i++)
        {
            var (classId, nodeId, _) = group[i];
            int currentClassId = Find(classId);
            DetachNode(currentClassId, nodeId);
            rootsToMerge.Add(currentClassId);
        }

        var targetNode = group[0].NewNode;
        int anchorRoot;
        if (TryFindCanonicalNode(targetNode.Head, targetNode.CanonicalChildIds, out int existingNodeId))
        {
            anchorRoot = Find(_nodes[existingNodeId].ClassId);
        }
        else
        {
            anchorRoot = Find(rootsToMerge[0]);
            AddRepairNode(anchorRoot, targetNode);
        }

        for (int i = 0; i < rootsToMerge.Count; i++)
        {
            int nextRoot = Find(rootsToMerge[i]);
            if (nextRoot == anchorRoot)
            {
                continue;
            }

            anchorRoot = UnionForRepair(anchorRoot, nextRoot);
        }
    }

    private CobraNodeRecord CanonicalizeNode(CobraNodeRecord node)
    {
        if (node.CanonicalChildIds.Length == 0)
        {
            return node;
        }

        bool changed = false;
        int[] canonicalChildIds = new int[node.CanonicalChildIds.Length];
        for (int i = 0; i < node.CanonicalChildIds.Length; i++)
        {
            int canonicalChildId = Find(node.CanonicalChildIds[i]);
            canonicalChildIds[i] = canonicalChildId;
            changed |= canonicalChildId != node.CanonicalChildIds[i];
        }

        return changed
            ? new CobraNodeRecord(node.Head, node.HeadCode, canonicalChildIds, node.ClassId, node.Literal)
            : node;
    }

    private int FindMatchingNodeId(int rootId, ENode node)
    {
        foreach (int nodeId in _classes[rootId].NodeIds)
        {
            var cobraNode = _nodes[nodeId];
            if (!string.Equals(cobraNode.Head, node.Head, StringComparison.Ordinal) ||
                cobraNode.CanonicalChildIds.Length != node.Children.Count)
            {
                continue;
            }

            bool matches = true;
            for (int i = 0; i < cobraNode.CanonicalChildIds.Length; i++)
            {
                if (cobraNode.CanonicalChildIds[i] != node.Children[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return nodeId;
            }
        }

        return -1;
    }

    private bool TryFindCanonicalNode(string head, int[] canonicalChildIds, out int nodeId)
    {
        for (int classId = 0; classId < _classes.Count; classId++)
        {
            if (Find(classId) != classId)
            {
                continue;
            }

            foreach (int candidateNodeId in _classes[classId].NodeIds)
            {
                var candidate = _nodes[candidateNodeId];
                if (!string.Equals(candidate.Head, head, StringComparison.Ordinal))
                {
                    continue;
                }

                var candidateCanonical = CanonicalizeNode(candidate);
                if (candidateCanonical.CanonicalChildIds.Length != canonicalChildIds.Length)
                {
                    continue;
                }

                bool matches = true;
                for (int i = 0; i < canonicalChildIds.Length; i++)
                {
                    if (candidateCanonical.CanonicalChildIds[i] != canonicalChildIds[i])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    nodeId = candidateNodeId;
                    return true;
                }
            }
        }

        nodeId = -1;
        return false;
    }

    private static bool NodesMatch(CobraNodeRecord left, CobraNodeRecord right)
    {
        if (!string.Equals(left.Head, right.Head, StringComparison.Ordinal) ||
            left.CanonicalChildIds.Length != right.CanonicalChildIds.Length)
        {
            return false;
        }

        for (int i = 0; i < left.CanonicalChildIds.Length; i++)
        {
            if (left.CanonicalChildIds[i] != right.CanonicalChildIds[i])
            {
                return false;
            }
        }

        return true;
    }

    private void DetachNode(int classId, int nodeId)
    {
        var cobraClass = _classes[classId];
        cobraClass.NodeIds.Remove(nodeId);
        (string Head, string ChildrenKey)? keyToRemove = null;
        foreach (var kvp in _hashCons)
        {
            if (kvp.Value == nodeId)
            {
                keyToRemove = kvp.Key;
                break;
            }
        }

        if (keyToRemove.HasValue)
        {
            _hashCons.Remove(keyToRemove.Value);
        }

        foreach (int childId in _nodes[nodeId].CanonicalChildIds)
        {
            _classes[Find(childId)].ParentNodeIds.Remove(nodeId);
        }

        if (cobraClass.RepresentativeNodeId == nodeId)
        {
            cobraClass.RepresentativeNodeId = cobraClass.NodeIds.Count > 0 ? cobraClass.NodeIds[0] : classId;
        }

        MarkLegacyClassDirty(classId);
        SyncState.Epoch++;
    }

    private int AddRepairNode(int classId, CobraNodeRecord node)
    {
        int nodeId = _nodes.Count;
        node.ClassId = classId;
        _nodes.Add(node);
        _classes[classId].NodeIds.Add(nodeId);
        foreach (int childId in node.CanonicalChildIds)
        {
            _classes[Find(childId)].ParentNodeIds.Add(nodeId);
        }

        _classes[classId].RepresentativeNodeId = nodeId;
        MarkLegacyClassDirty(classId);
        SyncState.Epoch++;
        return nodeId;
    }

    private int UnionForRepair(int classId1, int classId2)
    {
        int root1 = Find(classId1);
        int root2 = Find(classId2);
        if (root1 == root2)
        {
            return root1;
        }

        if (_classes[root1].NodeIds.Count < _classes[root2].NodeIds.Count)
        {
            (root1, root2) = (root2, root1);
        }

        int survivingGeneration = _classes[root1].Generation;
        MergeClassRecords(root1, root2);
        _parents[root2] = root1;
        _classes[root1].Generation = survivingGeneration + 1;
        SyncState.DirtyRebuildClasses.Add(root1);
        MarkLegacyClassDirty(root1);
        MarkLegacyClassDirty(root2);
        SyncState.Epoch++;
        return root1;
    }

    private void ReindexActiveGraph()
    {
        _hashCons.Clear();
        foreach (var cobraClass in _classes)
        {
            cobraClass.ParentNodeIds.Clear();
        }

        for (int classId = 0; classId < _classes.Count; classId++)
        {
            if (Find(classId) != classId)
            {
                _classes[classId].NodeIds.Clear();
                continue;
            }

            var activeNodeIds = _classes[classId].NodeIds.Distinct().ToList();
            _classes[classId].NodeIds = activeNodeIds;
            _classes[classId].RepresentativeNodeId = activeNodeIds.Count > 0 ? activeNodeIds[0] : classId;

            foreach (int nodeId in activeNodeIds)
            {
                var node = CanonicalizeNode(_nodes[nodeId]);
                node.ClassId = classId;
                _nodes[nodeId] = node;
                _hashCons[(node.Head, string.Join(",", node.CanonicalChildIds))] = nodeId;

                foreach (int childId in node.CanonicalChildIds)
                {
                    _classes[Find(childId)].ParentNodeIds.Add(nodeId);
                }
            }
        }
    }

    private void MirrorLegacyAliasClass(int aliasClassId, int rootId)
    {
        if (aliasClassId < 0)
        {
            return;
        }

        EnsureClassCount(aliasClassId + 1);
        int canonicalRoot = Find(rootId);
        if (aliasClassId == canonicalRoot)
        {
            return;
        }

        var aliasClass = _classes[aliasClassId];
        if (aliasClass.NodeIds.Count != 0)
        {
            return;
        }

        _parents[aliasClassId] = canonicalRoot;
    }

    private void CopyClassMetadataToLegacy(EGraph legacyGraph, int classId)
    {
        if (classId < 0 || classId >= _classes.Count || classId >= legacyGraph.ClassCount)
        {
            return;
        }

        var legacyClass = legacyGraph.GetClass(classId);
        var cobraClass = _classes[classId];
        legacyClass.Generation = Math.Max(legacyClass.Generation, cobraClass.Generation);

        foreach (var metadata in cobraClass.Metadata)
        {
            if (metadata.Value is double value)
            {
                legacyClass.Metadata[metadata.Key] = value;
            }
        }

        if (cobraClass.Metadata.TryGetValue("shape", out var shape) && shape is Shape cobraShape && cobraShape.IsValid)
        {
            legacyClass.Data = cobraShape;
        }
    }
}
