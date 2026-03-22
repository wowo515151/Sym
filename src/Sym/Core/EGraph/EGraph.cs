//Copyright Warren Harding 2025.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Sym.Atoms;
using Sym.Operations;

namespace Sym.Core.EGraph
{
    public class EGraph : IDisposable
    {
        private readonly List<EClass> _classes;
        private readonly Dictionary<ENode, int> _hashCons;
        private readonly Dictionary<int, List<(ENode Node, int ClassId)>> _parents;
        private readonly Queue<int> _worklist;
        
        private readonly ReaderWriterLockSlim _lock;

        public EGraph()
        {
            _classes = new List<EClass>();
            _hashCons = new Dictionary<ENode, int>();
            _parents = new Dictionary<int, List<(ENode, int)>>();
            _worklist = new Queue<int>();
            _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        }

        public int ClassCount
        {
            get { _lock.EnterReadLock(); try { return _classes.Count; } finally { _lock.ExitReadLock(); } }
        }

        public int NodeCount
        {
            get { _lock.EnterReadLock(); try { return _hashCons.Count; } finally { _lock.ExitReadLock(); } }
        }

        public int Add(IExpression expr)
        {
            _lock.EnterWriteLock();
            try { return AddInternal(expr); }
            finally { _lock.ExitWriteLock(); }
        }

        private int AddInternal(IExpression expr)
        {
            var children = expr is Operation op 
                ? op.Arguments.Select(AddInternal).ToImmutableList() 
                : ImmutableList<int>.Empty;
            
            int id = AddNodeInternal(new ENode(expr.Head, children));

            if (expr.Shape.IsValid && !expr.Shape.IsWildcardShape)
            {
                var eClass = _classes[FindInternal(id)];
                if (IsMoreSpecific(expr.Shape, eClass.Data as Shape))
                {
                    eClass.Data = expr.Shape;
                }
            }
            
            return id;
        }

        public int AddClass()
        {
            _lock.EnterWriteLock();
            try
            {
                int id = _classes.Count;
                _classes.Add(new EClass(id) { Generation = 1 });
                return id;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public int AddNode(ENode node)
        {
            _lock.EnterWriteLock();
            try { return AddNodeInternal(node); }
            finally { _lock.ExitWriteLock(); }
        }

        private int AddNodeInternal(ENode node)
        {
            node = Canonicalize(node);
            if (_hashCons.TryGetValue(node, out int existingId)) return FindInternal(existingId);

            int newId = _classes.Count;
            var newClass = new EClass(newId) { Generation = 1 };
            newClass.AddNode(node);
            _classes.Add(newClass);
            _hashCons[node] = newId;

            foreach (var childId in node.Children)
            {
                if (!_parents.TryGetValue(childId, out var list)) _parents[childId] = list = new List<(ENode, int)>();
                list.Add((node, newId));
            }

            return newId;
        }

        public int Find(int id)
        {
            _lock.EnterReadLock();
            try { return FindInternal(id); }
            finally { _lock.ExitReadLock(); }
        }

        private int FindInternal(int id)
        {
            int curr = id;
            while (_classes[curr].Parent != curr) curr = _classes[curr].Parent;
            return curr;
        }

        private int FindAndCompress(int id)
        {
            if (_classes[id].Parent == id) return id;
            return _classes[id].Parent = FindAndCompress(_classes[id].Parent);
        }

        private ENode Canonicalize(ENode node)
        {
            if (node.Children.IsEmpty) return node;
            return new ENode(node.Head, node.Children.Select(FindInternal).ToImmutableList());
        }

        private static bool IsMoreSpecific(Shape? newShape, Shape? existingShape)
        {
            if (newShape == null || !newShape.IsValid) return false;
            if (existingShape == null || !existingShape.IsValid) return true;
            if (existingShape.IsWildcardShape) return !newShape.IsWildcardShape;
            if (existingShape.IsScalar) return !newShape.IsScalar && !newShape.IsWildcardShape;
            return false;
        }

        public void Union(int id1, int id2)
        {
            _lock.EnterWriteLock();
            try
            {
                UnionWithMergeSemanticsInternal(id1, id2);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool UnionBatch(IReadOnlyList<IReadOnlyList<int>> groupedIds)
        {
            return UnionBatchDetailed(groupedIds, null, assumeCanonicalRoots: false).Changed;
        }

        public bool UnionBatch(IReadOnlyList<IReadOnlyList<int>> groupedIds, IReadOnlyList<int>? anchorIds)
        {
            return UnionBatchDetailed(groupedIds, anchorIds, assumeCanonicalRoots: false).Changed;
        }

        public bool UnionBatch(
            IReadOnlyList<IReadOnlyList<int>> groupedIds,
            IReadOnlyList<int>? anchorIds,
            bool assumeCanonicalRoots)
        {
            return UnionBatchDetailed(groupedIds, anchorIds, assumeCanonicalRoots).Changed;
        }

        public EGraphUnionBatchResult UnionBatchDetailed(
            IReadOnlyList<IReadOnlyList<int>> groupedIds,
            IReadOnlyList<int>? anchorIds,
            bool assumeCanonicalRoots)
        {
            _lock.EnterWriteLock();
            try
            {
                bool changed = false;
                var updatedClassIds = new List<int>();
                var updatedParentIds = new List<int>();
                var metricClassIds = new List<int>();
                var metricNodeCounts = new List<int>();
                var metricGenerations = new List<int>();
                for (int groupIndex = 0; groupIndex < groupedIds.Count; groupIndex++)
                {
                    var group = groupedIds[groupIndex];
                    if (group == null || group.Count < 2)
                    {
                        continue;
                    }

                    int requestedAnchor = anchorIds != null && groupIndex < anchorIds.Count ? anchorIds[groupIndex] : group[0];
                    int anchor = assumeCanonicalRoots ? requestedAnchor : FindInternal(requestedAnchor);
                    int[] orderedGroup = assumeCanonicalRoots
                        ? group
                            .Distinct()
                            .OrderByDescending(id => id == anchor)
                            .ThenBy(id => id)
                            .ToArray()
                        : group
                            .Select(FindInternal)
                            .Distinct()
                            .OrderByDescending(id => id == anchor)
                            .ThenBy(id => id)
                            .ToArray();

                    for (int i = 0; i < orderedGroup.Length; i++)
                    {
                        int next = orderedGroup[i];
                        if (anchor == next)
                        {
                            continue;
                        }

                        if (UnionWithMergeSemanticsInternal(anchor, next, updatedClassIds, updatedParentIds, metricClassIds, metricNodeCounts, metricGenerations))
                        {
                            changed = true;
                            anchor = FindInternal(anchor);
                        }
                    }
                }

                return new EGraphUnionBatchResult(changed, updatedClassIds, updatedParentIds, metricClassIds, metricNodeCounts, metricGenerations);
            }
            finally { _lock.ExitWriteLock(); }
        }

        private void PropagateChange(int childClassId)
        {
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(childClassId);
            visited.Add(childClassId);

            while (queue.Count > 0)
            {
                int currentChild = queue.Dequeue();
                if (_parents.TryGetValue(currentChild, out var parentsList))
                {
                    foreach (var (node, parentClassId) in parentsList)
                    {
                        int rootParent = FindInternal(parentClassId);
                        if (visited.Add(rootParent))
                        {
                            _classes[rootParent].Generation++;
                            queue.Enqueue(rootParent);
                        }
                    }
                }
            }
        }

        public void Rebuild(CancellationToken ct = default)
        {
            Rebuild(prioritizedRootIds: null, exactCandidates: null, exactCandidateGroups: null, ct);
        }

        public void Rebuild(IReadOnlyList<int>? prioritizedRootIds, CancellationToken ct = default)
        {
            Rebuild(prioritizedRootIds, exactCandidates: null, exactCandidateGroups: null, ct);
        }

        public void Rebuild(IReadOnlyList<int>? prioritizedRootIds, IReadOnlyList<EGraphRepairCandidate>? exactCandidates, CancellationToken ct = default)
        {
            Rebuild(prioritizedRootIds, exactCandidates, exactCandidateGroups: null, ct);
        }

        public void Rebuild(IReadOnlyList<int>? prioritizedRootIds, IReadOnlyList<IReadOnlyList<EGraphRepairCandidate>>? exactCandidateGroups, CancellationToken ct = default)
        {
            Rebuild(prioritizedRootIds, exactCandidates: null, exactCandidateGroups, ct);
        }

        private void Rebuild(
            IReadOnlyList<int>? prioritizedRootIds,
            IReadOnlyList<EGraphRepairCandidate>? exactCandidates,
            IReadOnlyList<IReadOnlyList<EGraphRepairCandidate>>? exactCandidateGroups,
            CancellationToken ct)
        {
            _lock.EnterWriteLock();
            try
            {
                bool usedExactCandidates = false;
                while (_worklist.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    _worklist.Dequeue(); // rootId is not strictly needed for brute-force repair
                    
                    if (!usedExactCandidates && exactCandidateGroups is { Count: > 0 })
                    {
                        Repair(prioritizedRootIds, exactCandidates: null, exactCandidateGroups, ct);
                        usedExactCandidates = true;
                    }
                    else if (!usedExactCandidates && exactCandidates is { Count: > 0 })
                    {
                        Repair(prioritizedRootIds, exactCandidates, exactCandidateGroups: null, ct);
                        usedExactCandidates = true;
                    }
                    else
                    {
                        Repair(prioritizedRootIds, exactCandidates: null, exactCandidateGroups: null, ct);
                    }
                }
                
                for (int i = 0; i < _classes.Count; i++) FindAndCompress(i);
            }
            finally { _lock.ExitWriteLock(); }
        }

        private void Repair(
            IReadOnlyList<int>? prioritizedRootIds,
            IReadOnlyList<EGraphRepairCandidate>? exactCandidates,
            IReadOnlyList<IReadOnlyList<EGraphRepairCandidate>>? exactCandidateGroups,
            CancellationToken ct)
        {
            var todo = new List<(int ClassId, ENode OldNode, ENode NewNode)>();
            var visitedClasses = new HashSet<int>();

            if (exactCandidateGroups is { Count: > 0 })
            {
                var groupedTodo = new List<List<(int ClassId, ENode OldNode, ENode NewNode)>>();
                foreach (var group in exactCandidateGroups)
                {
                    ct.ThrowIfCancellationRequested();
                    if (group == null || group.Count == 0)
                    {
                        continue;
                    }

                    var todoGroup = new List<(int ClassId, ENode OldNode, ENode NewNode)>();
                    foreach (var candidate in group)
                    {
                        ct.ThrowIfCancellationRequested();
                        int rootId = FindInternal(candidate.ClassId);
                        if (_classes[rootId].Parent != rootId) continue;

                        var canonical = Canonicalize(candidate.Node);
                        if (!candidate.Node.Equals(canonical))
                        {
                            todoGroup.Add((rootId, candidate.Node, canonical));
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
            else if (exactCandidates is { Count: > 0 })
            {
                foreach (var candidate in exactCandidates)
                {
                    ct.ThrowIfCancellationRequested();
                    int rootId = FindInternal(candidate.ClassId);
                    if (_classes[rootId].Parent != rootId) continue;

                    var canonical = Canonicalize(candidate.Node);
                    if (!candidate.Node.Equals(canonical))
                    {
                        todo.Add((rootId, candidate.Node, canonical));
                    }
                }
            }
            else
            {
                if (prioritizedRootIds != null)
                {
                    foreach (var prioritizedId in prioritizedRootIds)
                    {
                        ct.ThrowIfCancellationRequested();
                        int rootId = FindInternal(prioritizedId);
                        if (_classes[rootId].Parent != rootId || !visitedClasses.Add(rootId)) continue;

                        foreach (var node in _classes[rootId].Nodes)
                        {
                            var canonical = Canonicalize(node);
                            if (!node.Equals(canonical))
                            {
                                todo.Add((rootId, node, canonical));
                            }
                        }
                    }
                }

                for (int i = 0; i < _classes.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    int rootId = FindInternal(i);
                    if (_classes[rootId].Parent != rootId || !visitedClasses.Add(rootId)) continue;

                    foreach (var node in _classes[rootId].Nodes)
                    {
                        var canonical = Canonicalize(node);
                        if (!node.Equals(canonical))
                        {
                            todo.Add((rootId, node, canonical));
                        }
                    }
                }
            }

            if (exactCandidates is { Count: > 0 })
            {
                ApplyGroupedRepairs(todo.Select(static item => new List<(int ClassId, ENode OldNode, ENode NewNode)> { item }).ToList());
            }
            else
            {
                foreach (var (classId, oldNode, newNode) in todo)
                {
                    int currentClassId = FindInternal(classId);
                    _classes[currentClassId].Nodes.Remove(oldNode);
                    _hashCons.Remove(oldNode);

                    if (_hashCons.TryGetValue(newNode, out int existingId))
                    {
                        UnionInternal(currentClassId, existingId);
                    }
                    else
                    {
                        _hashCons[newNode] = FindInternal(currentClassId);
                        _classes[FindInternal(currentClassId)].Nodes.Add(newNode);
                    }
                }
            }
        }

        private void ApplyGroupedRepairs(IReadOnlyList<List<(int ClassId, ENode OldNode, ENode NewNode)>> groupedTodo)
        {
            foreach (var group in groupedTodo)
            {
                if (group.Count == 0)
                {
                    continue;
                }

                ApplyRepairGroup(group, group[0].NewNode);
            }
        }

        private void ApplyRepairGroup(IReadOnlyList<(int ClassId, ENode OldNode, ENode NewNode)> group, ENode targetNode)
        {
            var rootsToMerge = new List<int>(group.Count);
            for (int i = 0; i < group.Count; i++)
            {
                var (classId, oldNode, _) = group[i];
                int currentClassId = FindInternal(classId);
                _classes[currentClassId].Nodes.Remove(oldNode);
                _hashCons.Remove(oldNode);
                rootsToMerge.Add(currentClassId);
            }

            int anchorRoot;
            if (_hashCons.TryGetValue(targetNode, out int existingId))
            {
                anchorRoot = FindInternal(existingId);
            }
            else
            {
                anchorRoot = FindInternal(rootsToMerge[0]);
                _hashCons[targetNode] = anchorRoot;
                _classes[anchorRoot].Nodes.Add(targetNode);
            }

            for (int i = 0; i < rootsToMerge.Count; i++)
            {
                int nextRoot = FindInternal(rootsToMerge[i]);
                if (nextRoot == anchorRoot)
                {
                    continue;
                }

                UnionInternal(anchorRoot, nextRoot);
                anchorRoot = FindInternal(anchorRoot);
            }
        }

        private void UnionInternal(int id1, int id2)
        {
            int r1 = FindInternal(id1);
            int r2 = FindInternal(id2);
            if (r1 == r2) return;

            if (_classes[r1].Nodes.Count < _classes[r2].Nodes.Count)
            {
                int tmp = r1; r1 = r2; r2 = tmp;
            }

            _classes[r2].Parent = r1;
            foreach (var node in _classes[r2].Nodes) _classes[r1].Nodes.Add(node);
            _classes[r2].Nodes.Clear();
            _classes[r1].Generation++;
            _worklist.Enqueue(r1);
        }

        private bool UnionWithMergeSemanticsInternal(
            int id1,
            int id2,
            List<int>? updatedClassIds = null,
            List<int>? updatedParentIds = null,
            List<int>? metricClassIds = null,
            List<int>? metricNodeCounts = null,
            List<int>? metricGenerations = null)
        {
            int r1 = FindInternal(id1);
            int r2 = FindInternal(id2);
            if (r1 == r2) return false;

            if (_classes[r1].Nodes.Count < _classes[r2].Nodes.Count)
            {
                int tmp = r1; r1 = r2; r2 = tmp;
            }

            _classes[r2].Parent = r1;
            updatedClassIds?.Add(r2);
            updatedParentIds?.Add(r1);
            _classes[r1].Generation++;

            if (IsMoreSpecific(_classes[r2].Data as Shape, _classes[r1].Data as Shape))
            {
                _classes[r1].Data = _classes[r2].Data;
            }

            foreach (var kvp in _classes[r2].Metadata)
            {
                if (!_classes[r1].Metadata.ContainsKey(kvp.Key))
                {
                    _classes[r1].Metadata[kvp.Key] = kvp.Value;
                }
            }

            foreach (var node in _classes[r2].Nodes)
            {
                _classes[r1].Nodes.Add(node);
            }
            _classes[r2].Nodes.Clear();

            PropagateChange(r1);
            PropagateChange(r2);
            _worklist.Enqueue(r1);

            if (metricClassIds != null && metricNodeCounts != null && metricGenerations != null)
            {
                metricClassIds.Add(r1);
                metricNodeCounts.Add(_classes[r1].Nodes.Count);
                metricGenerations.Add(_classes[r1].Generation);

                metricClassIds.Add(r2);
                metricNodeCounts.Add(_classes[r2].Nodes.Count);
                metricGenerations.Add(_classes[r2].Generation);
            }

            return true;
        }

        public EClass GetClass(int id)
        {
            _lock.EnterReadLock();
            try { return _classes[FindInternal(id)]; }
            finally { _lock.ExitReadLock(); }
        }
        
        public List<int> GetRootIds()
        {
            _lock.EnterReadLock();
            try
            {
                var roots = new List<int>();
                for (int i = 0; i < _classes.Count; i++) if (_classes[i].Parent == i) roots.Add(i);
                return roots;
            }
            finally { _lock.ExitReadLock(); }
        }

        public int[] GetParentSnapshot()
        {
            _lock.EnterReadLock();
            try
            {
                var parents = new int[_classes.Count];
                for (int i = 0; i < _classes.Count; i++)
                {
                    parents[i] = _classes[i].Parent;
                }

                return parents;
            }
            finally { _lock.ExitReadLock(); }
        }

        public IReadOnlyList<int> GetParentClassIds(int childClassId)
        {
            _lock.EnterReadLock();
            try
            {
                int canonicalChildId = FindInternal(childClassId);
                var parentClassIds = new HashSet<int>();

                foreach (var (trackedChildId, parentsList) in _parents)
                {
                    if (FindInternal(trackedChildId) != canonicalChildId)
                    {
                        continue;
                    }

                    foreach (var (_, parentClassId) in parentsList)
                    {
                        int canonicalParentId = FindInternal(parentClassId);
                        if (canonicalParentId != canonicalChildId)
                        {
                            parentClassIds.Add(canonicalParentId);
                        }
                    }
                }

                return parentClassIds.ToArray();
            }
            finally { _lock.ExitReadLock(); }
        }

        public EGraphRepairSnapshot GetRepairSnapshot()
        {
            _lock.EnterReadLock();
            try
            {
                var parents = new int[_classes.Count];
                for (int i = 0; i < _classes.Count; i++)
                {
                    parents[i] = _classes[i].Parent;
                }

                var candidates = new List<EGraphRepairCandidate>();
                var childStarts = new List<int>();
                var childCounts = new List<int>();
                var childIds = new List<int>();

                for (int i = 0; i < _classes.Count; i++)
                {
                    if (_classes[i].Parent != i) continue;

                    foreach (var node in _classes[i].Nodes)
                    {
                        childStarts.Add(childIds.Count);
                        childCounts.Add(node.Children.Count);
                        foreach (var childId in node.Children)
                        {
                            childIds.Add(childId);
                        }

                        candidates.Add(new EGraphRepairCandidate(i, node));
                    }
                }

                return new EGraphRepairSnapshot(
                    parents,
                    childStarts.ToArray(),
                    childCounts.ToArray(),
                    childIds.ToArray(),
                    candidates);
            }
            finally { _lock.ExitReadLock(); }
        }

        public void Dispose() => _lock?.Dispose();
    }
}
