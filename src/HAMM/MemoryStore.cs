using System;
using System.Collections.Generic;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Linq;
using System.Collections.Immutable;
using Sym.Core.EGraph;
using System.Threading;
using System.IO;
using System.Text;
using System.Text.Json;
using Sym.CSharpIO;

namespace HAMM
{
    /// <summary>
    /// Represents an agent or component subscribed to specific memory scopes (Cycle 11/27).
    /// </summary>
    public interface IMemorySubscriber
    {
        string Name { get; }
        IEnumerable<string> SubscribedScopes { get; }
        void OnFactAdded(MemoryFact fact);
        void OnFactInvalidated(MemoryFact fact);
    }

    public class AddFactRequest
    {
        public IExpression Expression { get; set; } = null!;
        public string Scope { get; set; } = "Global";
        public double Certainty { get; set; } = 1.0;
        public ReActType Type { get; set; } = ReActType.Generic;
        public IEnumerable<MemoryFact>? Dependencies { get; set; }
        public IngestSourceType SourceType { get; set; } = IngestSourceType.User;
        public string Source { get; set; } = "User";
        public string? RuleName { get; set; }
        public string? IngestOperationId { get; set; }
    }

    public class QueryOptions
    {
        public bool IncludeNoise { get; set; } = false;
        public bool IncludeDiagnostics { get; set; } = false;
        public bool IncludeArchive { get; set; } = false;
        public bool IncludeInvalidated { get; set; } = false;
        public bool IncludeSuperseded { get; set; } = false;
        public bool DedupResults { get; set; } = true;
        public double MinQualityScore { get; set; } = 0.3;
        public int MaxPerCluster { get; set; } = 1;
        public string Scope { get; set; } = "Global";
    }

    public enum ConsistencyMode
    {
        Strict,
        Lenient
    }

    public class MemoryOperation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ActorId { get; set; } = "Unknown";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string OperationType { get; set; } = "Add";
        public List<string> FactIds { get; set; } = new List<string>();
        public string? Rationale { get; set; }
    }

    /// <summary>
    /// The core store for HAMM memory facts.
    /// </summary>
    public class MemoryStore : IDisposable
    {
        private readonly List<MemoryFact> _facts = new List<MemoryFact>();
        private readonly List<MemoryFact> _archive = new List<MemoryFact>();
        private readonly List<IMemorySubscriber> _subscribers = new List<IMemorySubscriber>();
        private readonly double _decayRate = 0.1;
        private EGraph _eGraph = new EGraph();
        private bool _eGraphDirty = false;
        private readonly MemoryIndex _index = new MemoryIndex();
        private readonly Dictionary<string, MemoryFact> _hashIndex = new Dictionary<string, MemoryFact>(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryFact> _semanticIndex = new Dictionary<string, MemoryFact>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _artifacts = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        public ConsistencyMode Consistency { get; set; } = ConsistencyMode.Lenient;
        public string? CurrentRootPath { get; private set; }
        public string ActorId { get; set; } = "DefaultAgent";
        public int LastLoadRepairCount { get; private set; }
        
        public MemoryStore()
        {
            ScopeParents["Goals"] = "Global";
            ScopeParents["Concepts"] = "Global";
            ScopeParents["Operations"] = "Global";
            ScopeParents["Diagnostics"] = "Global";
            ScopeParents["Artifacts"] = "Global";
            ScopeParents["Noise"] = "Global";
            ScopeParents["HAMM"] = "Global";
            ScopeParents["Pinned"] = "Global";
        }

        /// <summary>
        /// Total capacity in tokens for context management (Cycle 19).
        /// </summary>
        public int TokenCapacity { get; set; } = 10000; // Increased default for vNext

        /// <summary>
        /// Target active tokens for load control.
        /// </summary>
        public int TargetActiveTokens { get; set; } = 8000;

        /// <summary>
        /// Hard maximum for active tokens.
        /// </summary>
        public int MaxActiveTokens { get; set; } = 15000;

        /// <summary>
        /// Threshold for gating low-quality facts.
        /// </summary>
        public double MinQualityThreshold { get; set; } = 0.3;

        /// <summary>
        /// Threshold for classifying a fact as Noise.
        /// </summary>
        public double NoiseScoreThreshold { get; set; } = 0.25;

        /// <summary>
        /// Threshold for classifying a fact as ToolTrace.
        /// </summary>
        public double ToolTraceScoreThreshold { get; set; } = 0.35;

        /// <summary>
        /// If true, path-like text does not automatically force Noise classification.
        /// </summary>
        public bool AutoNoiseByPathDisabled { get; set; } = true;
        
        /// <summary>
        /// Maximum token estimate allowed per fact before it is treated as an artifact.
        /// </summary>
        public int MaxTokensPerFact { get; set; } = 4000;

        /// <summary>
        /// Maximum number of characters allowed to inline in a Symbol before hashing.
        /// </summary>
        public int MaxInlineSymbolChars { get; set; } = MemoryContentEncoding.DefaultMaxInlineChars;

        /// <summary>
        /// Multiplier applied to recall scores for artifact facts.
        /// </summary>
        public double ArtifactScoreMultiplier { get; set; } = 0.2;

        /// <summary>
        /// Weight applied to fact priority during recall scoring.
        /// </summary>
        public double PriorityWeight { get; set; } = 0.1;

        /// <summary>
        /// Whether artifacts should be excluded from recall candidates.
        /// </summary>
        public bool ExcludeArtifactsFromRecall { get; set; } = true;
        
        /// <summary>
        /// High threshold for moving from Archive to Active (Cycle 25/35/StateInvalidation).
        /// </summary>
        public double HysteresisHigh { get; set; } = 0.8;

        /// <summary>
        /// Low threshold for moving from Archive to Active (Cycle 25/35/StateInvalidation).
        /// </summary>
        public double HysteresisLow { get; set; } = 0.3;

        /// <summary>
        /// Sharpness factor for Bayesian updates (Cycle 21).
        /// </summary>
        public double LikelihoodSharpness { get; set; } = 1.0;

        /// <summary>
        /// Rate at which fact certainty decays over time (Cycle 7).
        /// </summary>
        public double CertaintyDecayRate { get; set; } = 0.01;

        /// <summary>
        /// Maximum number of facts allowed in RAM before compaction is triggered (Cycle 29).
        /// </summary>
        public int CompactionLimit { get; set; } = 500; // Increased for vNext

        /// <summary>
        /// Event triggered when compaction is needed.
        /// </summary>
        public event Action? CompactionRequired;

        /// <summary>
        /// Gets or sets the parent scope mapping (Cycle 4/12).
        /// </summary>
        public Dictionary<string, string> ScopeParents { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Subscribes an agent to memory updates.
        /// </summary>
        public void Subscribe(IMemorySubscriber subscriber)
        {
            _lock.EnterWriteLock();
            try
            {
                if (!_subscribers.Contains(subscriber))
                    _subscribers.Add(subscriber);
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Updates a fact's certainty using Bayesian logic based on a new observation similarity (Cycle 9/21).
        /// </summary>
        public void UpdateCertainty(MemoryFact fact, double similarity)
        {
            _lock.EnterWriteLock();
            try
            {
                double likelihood = Math.Pow(similarity, LikelihoodSharpness);
                double prior = fact.Certainty;
                
                double numerator = likelihood * prior;
                double denominator = numerator + (0.5 * (1 - prior));
                
                if (denominator > 0)
                {
                    fact.Certainty = numerator / denominator;
                    fact.LastSeenAtUtc = DateTime.UtcNow;
                    fact.LastDecayUpdate = fact.LastSeenAtUtc;
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Gets the current cognitive load as token density (Cycle 19).
        /// </summary>
        public double CognitiveLoad
        {
            get
            {
                _lock.EnterReadLock();
                try 
                { 
                    var activeTokens = _facts.Where(f => !f.IsInvalidated && !f.IsSuperseded).Sum(f => f.Tokens);
                    return TokenCapacity > 0 ? (double)activeTokens / TokenCapacity : 0; 
                }
                finally { _lock.ExitReadLock(); }
            }
        }

        /// <summary>
        /// Adds a new fact to the memory store and resolves contradictions.
        /// </summary>
        public MemoryFact AddFact(IExpression expression, string scope = "Global", double certainty = 1.0, ReActType type = ReActType.Generic, IEnumerable<MemoryFact>? dependencies = null)
        {
            _lock.EnterWriteLock();
            try { return AddFactInternal(expression, scope, certainty, type, dependencies, allowDedup: true); }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Applies an observation update, invalidating superseded facts and recording rationale.
        /// </summary>
        public void ApplyObservationUpdate(IExpression observation, IEnumerable<string> supersedesFactIds, string rationale, string scope = "Global")
        {
            _lock.EnterWriteLock();
            try
            {
                var newFact = AddFactInternal(observation, scope, 1.0, ReActType.Observation, null, allowDedup: true);
                newFact.Metadata["UpdateRationale"] = rationale;
                LogOperation("ObservationUpdate", new[] { newFact.Id }, rationale);
                
                foreach (var id in supersedesFactIds)
                {
                    var oldFact = _facts.FirstOrDefault(f => f.Id == id) ?? _archive.FirstOrDefault(f => f.Id == id);
                    if (oldFact != null && !oldFact.IsSuperseded)
                    {
                        oldFact.IsSuperseded = true;
                        oldFact.NextVersion = newFact;
                        newFact.SupersedesFactIds.Add(id);
                        InvalidateFact(oldFact, 1.0);
                        oldFact.InvalidationReason = "SupersededByObservation";
                    }
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        public MemoryFact AddFactV2(AddFactRequest request)
        {
            _lock.EnterWriteLock();
            try
            {
                return AddFactInternal(request.Expression, request.Scope, request.Certainty, request.Type, request.Dependencies, 
                    allowDedup: true, request.SourceType, request.Source, request.RuleName, request.IngestOperationId);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public IEnumerable<MemoryFact> AddFactsBatch(IEnumerable<AddFactRequest> requests)
        {
            _lock.EnterWriteLock();
            try
            {
                var results = new List<MemoryFact>();
                foreach (var req in requests)
                {
                    results.Add(AddFactInternal(req.Expression, req.Scope, req.Certainty, req.Type, req.Dependencies, 
                        allowDedup: true, req.SourceType, req.Source, req.RuleName, req.IngestOperationId));
                }
                return results;
            }
            finally { _lock.ExitWriteLock(); }
        }

        private MemoryFact AddFactInternal(IExpression expression, string scope, double certainty, ReActType type, 
            IEnumerable<MemoryFact>? dependencies, bool allowDedup, 
            IngestSourceType sourceType = IngestSourceType.User, string source = "User", 
            string? ruleName = null, string? ingestOperationId = null)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            // 1. Normalize
            bool sanitized = false;
            var capturedArtifacts = new Dictionary<string, string>();
            var normalized = NormalizeExpression(expression, ref sanitized, capturedArtifacts);
            
            if (capturedArtifacts.Count > 0)
            {
                foreach (var kvp in capturedArtifacts) _artifacts[kvp.Key] = kvp.Value;
            }

            var canonicalText = GetExpressionText(normalized);

            // v5 Security Firewall
            bool isQuarantined = false;
            if (DetectSecurityRisk(canonicalText, out var riskReason))
            {
                scope = "Quarantine";
                isQuarantined = true;
            }

            var contentHash = MemoryContentEncoding.ComputeHash(canonicalText);
            var sizeBytes = Encoding.UTF8.GetByteCount(canonicalText);

            int rawTokenEstimate = EstimateTokens(normalized);
            bool isOversized = sanitized || rawTokenEstimate > MaxTokensPerFact;
            
            if (isOversized && (normalized is not Symbol s || !MemoryContentEncoding.IsHashedSymbol(s)))
            {
                _artifacts[contentHash] = canonicalText;
                normalized = new Symbol(MemoryContentEncoding.HashPrefix + contentHash);
                canonicalText = GetExpressionText(normalized);
                rawTokenEstimate = 1;
            }

            int tokenEstimate = Math.Min(MaxTokensPerFact, rawTokenEstimate);
            var contentType = DetermineContentType(normalized, isOversized);

            // Temporary fact for classification and scoring
            var fact = new MemoryFact(normalized)
            {
                Scope = scope,
                Certainty = certainty,
                Type = type,
                Tokens = tokenEstimate,
                ContentHash = contentHash,
                CanonicalText = canonicalText,
                SizeBytes = sizeBytes,
                ContentType = contentType,
                SourceType = sourceType,
                Source = source,
                RuleName = ruleName,
                IngestOperationId = ingestOperationId,
                IsQuarantined = isQuarantined,
                TrustScore = CalculateInitialTrust(sourceType, source),
                SourceReliability = CalculateInitialReliability(sourceType, source)
            };

            if (isQuarantined) fact.Metadata["QuarantineReason"] = riskReason!;

            // 2. Score
            fact.QualityScore = ComputeQualityScore(fact);


            // 3. Classify
            fact.Kind = DetermineFactKind(fact);

            // 4. Gate
            if (fact.QualityScore < MinQualityThreshold && fact.Kind != FactKind.ArtifactPointer)
            {
                fact.Retention = MemoryRetentionPolicy.Ephemeral;
            }

            // Auto-routing based on kind/content (v4)
            if (fact.Scope == "Global")
            {
                if (fact.Kind == FactKind.Noise) fact.Scope = "Noise";
                else if (fact.Kind == FactKind.Directive) fact.Scope = "Goals";
                else if (fact.Kind == FactKind.ToolTrace) fact.Scope = "Diagnostics";
                else if (fact.Kind == FactKind.ArtifactPointer) fact.Scope = "Artifacts";
                else if (fact.Expression is Operation op && (op.Head == "Define" || op.Head == "Concept" || op.Head == "Func:Define" || op.Head == "Func:Concept"))
                {
                    fact.Scope = "Concepts";
                }
            }

            // Compute SemanticKey
            fact.SemanticKey = ComputeSemanticKey(fact);

            // 5. Upsert by SemanticKey
            if (allowDedup && _semanticIndex.TryGetValue(fact.SemanticKey, out var existing) && !existing.IsInvalidated && !existing.IsSuperseded)
            {
                UpdateExistingFact(existing, certainty, type, dependencies, tokenEstimate, contentType, sizeBytes, contentHash);
                existing.OccurrenceCount++;
                existing.LastSeenAtUtc = DateTime.UtcNow;
                return existing;
            }

            // Pinned facts
            if (fact.Scope == "Concepts" || fact.Scope == "HAMM" || fact.Scope == "Goals" || fact.Scope == "Pinned")
            {
                fact.Retention = MemoryRetentionPolicy.Pinned;
            }

            if (dependencies != null)
            {
                foreach (var dep in dependencies)
                {
                    if (!fact.Dependencies.Contains(dep)) fact.Dependencies.Add(dep);
                }
                if (dependencies.Any()) fact.Source = "Inference";
            }
            
            _facts.Add(fact);
            _index.Add(fact); 
            AddHashIndex(fact);
            _semanticIndex[fact.SemanticKey] = fact;

            LogOperation("Add", new[] { fact.Id });

            ResolveContradictions(fact);
            
            if (!fact.IsInvalidated && !fact.IsSuperseded && (fact.Kind == FactKind.Assertion || fact.Kind == FactKind.Directive))
            {
                AddToEGraph(fact);
            }

            // Notify subscribers
            foreach (var sub in _subscribers)
            {
                if (sub.SubscribedScopes.Contains("Global") || sub.SubscribedScopes.Contains(fact.Scope))
                {
                    sub.OnFactAdded(fact);
                }
            }

            if (fact.IsInvalidated)
            {
                _facts.Remove(fact);
                _index.Remove(fact);
                RemoveHashIndex(fact);
                _semanticIndex.Remove(fact.SemanticKey);
            }

            // Load control
            var currentTokens = _facts.Sum(f => f.Tokens);
            var signaledCompaction = false;

            // Keep active memory near the target budget on ingest so callers do not
            // need to rely on prompt-level "run maintenance" instructions.
            if (currentTokens > TargetActiveTokens)
            {
                CompactionRequired?.Invoke();
                signaledCompaction = true;
                FastMaintenance();
                currentTokens = _facts.Sum(f => f.Tokens);
            }

            if (currentTokens > MaxActiveTokens || _facts.Count > CompactionLimit)
            {
                if (!signaledCompaction)
                {
                    CompactionRequired?.Invoke();
                }
                Maintenance();
                Compact();
            }

            return fact;
        }

        private bool IsPathOrNoisy(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            
            // Check for path-like structures
            bool hasPath = text.Contains('/') || text.Contains('\\');
            if (hasPath && !AutoNoiseByPathDisabled) return true; 
            
            // Tool output markers
            if (text.Contains("---") || text.Contains("===")) return true;
            if (text.Contains("<Tool") || text.Contains("</Tool")) return true;

            // Common noisy directories/tokens, including short path segments
            var noisy = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "bin", "obj", "node_modules", ".git", ".vs", "tmp", "temp"
            };
            
            if (noisy.Contains(text.Trim())) return true;
            
            return false;
        }

        private static MemoryContentType DetermineContentType(IExpression expr, bool isOversized)
        {
            if (expr is Summary) return MemoryContentType.Summary;
            return isOversized ? MemoryContentType.Artifact : MemoryContentType.Fact;
        }

        private static string GetExpressionText(IExpression expr)
        {
            try { return CSharpIO.FormatExpr(expr); }
            catch { return expr.ToDisplayString(); }
        }

        private void UpdateExistingFact(MemoryFact existing, double certainty, ReActType type, IEnumerable<MemoryFact>? dependencies, int tokenEstimate, MemoryContentType contentType, long sizeBytes, string contentHash)
        {
            existing.Certainty = Math.Max(existing.Certainty, certainty);
            if (type != ReActType.Generic) existing.Type = type;
            existing.Tokens = tokenEstimate;
            existing.ContentType = contentType;
            existing.SizeBytes = sizeBytes;
            existing.ContentHash = contentHash;
            existing.LastDecayUpdate = DateTime.UtcNow;
            existing.LastSeenAtUtc = DateTime.UtcNow;
            existing.Potency = 1.0;

            if (dependencies != null)
            {
                foreach (var dep in dependencies)
                {
                    if (!existing.Dependencies.Contains(dep)) existing.Dependencies.Add(dep);
                }
                if (dependencies.Any()) existing.Source = "Inference";
            }

            if (_archive.Contains(existing))
            {
                _archive.Remove(existing);
                _facts.Add(existing);
                _index.Add(existing);
            }
        }

        private void LogOperation(string type, IEnumerable<string> factIds, string? rationale = null)
        {
            if (string.IsNullOrEmpty(CurrentRootPath)) return;
            
            var op = new MemoryOperation
            {
                ActorId = ActorId,
                OperationType = type,
                FactIds = factIds.ToList(),
                Rationale = rationale
            };

            try
            {
                var logPath = Path.Combine(CurrentRootPath, "HAMM.ops.jsonl");
                var line = JsonSerializer.Serialize(op);
                File.AppendAllLines(logPath, new[] { line });
            }
            catch { /* Ignore log errors for now */ }
        }

        private void MergeInto(MemoryFact target, MemoryFact source)
        {
            target.OccurrenceCount += source.OccurrenceCount;
            if (source.FirstSeenAtUtc != default && (target.FirstSeenAtUtc == default || source.FirstSeenAtUtc < target.FirstSeenAtUtc))
                target.FirstSeenAtUtc = source.FirstSeenAtUtc;
            if (source.LastSeenAtUtc != default && (target.LastSeenAtUtc == default || source.LastSeenAtUtc > target.LastSeenAtUtc))
                target.LastSeenAtUtc = source.LastSeenAtUtc;
            
            target.Certainty = Math.Max(target.Certainty, source.Certainty);
            target.Potency = Math.Max(target.Potency, source.Potency);
            target.QualityScore = Math.Max(target.QualityScore, source.QualityScore);

            foreach (var tag in source.Tags)
            {
                if (!target.Tags.Contains(tag)) target.Tags.Add(tag);
            }

            foreach (var kvp in source.Metadata)
            {
                if (!target.Metadata.ContainsKey(kvp.Key)) target.Metadata[kvp.Key] = kvp.Value;
            }

            foreach (var dep in source.Dependencies)
            {
                if (!target.Dependencies.Contains(dep)) target.Dependencies.Add(dep);
            }

            source.IsSuperseded = true;
            source.NextVersion = target;
        }

        private string BuildHashKey(string scope, string contentHash) => $"{scope}|{contentHash}";

        private void AddHashIndex(MemoryFact fact)
        {
            if (string.IsNullOrWhiteSpace(fact.ContentHash)) return;
            _hashIndex[BuildHashKey(fact.Scope, fact.ContentHash)] = fact;
        }

        private void RemoveHashIndex(MemoryFact fact)
        {
            if (string.IsNullOrWhiteSpace(fact.ContentHash)) return;
            var key = BuildHashKey(fact.Scope, fact.ContentHash);
            if (_hashIndex.TryGetValue(key, out var existing) && ReferenceEquals(existing, fact))
            {
                _hashIndex.Remove(key);
            }
        }

        private void RebuildHashIndex()
        {
            _hashIndex.Clear();
            _semanticIndex.Clear();
            foreach (var fact in _facts.Concat(_archive))
            {
                if (fact.IsInvalidated || fact.IsSuperseded) continue;
                AddHashIndex(fact);
                _semanticIndex[fact.SemanticKey] = fact;
            }
        }

        private void RebuildIndex()
        {
            _index.Clear();
            foreach (var fact in _facts)
            {
                if (fact.IsInvalidated || fact.IsSuperseded) continue;
                _index.Add(fact);
            }
        }

        private void Compact()
        {
            _facts.RemoveAll(f => f.IsInvalidated || f.IsSuperseded);
            _archive.RemoveAll(f => f.IsInvalidated);
            RebuildIndex();
            RebuildHashIndex();
            _eGraphDirty = true;
            RebuildEGraph();
        }

        private IExpression NormalizeExpression(IExpression expr, ref bool sanitized, Dictionary<string, string> capturedArtifacts)
        {
            if (expr is Symbol s)
            {
                if (s.Name.Length > MaxInlineSymbolChars)
                {
                    sanitized = true;
                    var encoded = MemoryContentEncoding.EncodeContentSymbol(s.Name, MaxInlineSymbolChars);
                    // Extract hash from the encoded name "ContentHash:..."
                    var hash = encoded.Name.Substring(MemoryContentEncoding.HashPrefix.Length);
                    capturedArtifacts[hash] = s.Name;
                    return encoded;
                }
                return s;
            }
            if (expr is Operation op)
            {
                bool changed = false;
                var args = ImmutableList.CreateBuilder<IExpression>();
                foreach (var arg in op.Arguments)
                {
                    var normalized = NormalizeExpression(arg, ref sanitized, capturedArtifacts);
                    if (!ReferenceEquals(normalized, arg)) changed = true;
                    args.Add(normalized);
                }
                return changed ? op.WithArguments(args.ToImmutable()) : op;
            }
            return expr;
        }

        private static int EstimateTokens(IExpression expr)
        {
            long symbolChars = 0;
            int nodes = 0;
            AccumulateTokenStats(expr, ref nodes, ref symbolChars);
            var tokenEstimate = nodes + (int)Math.Ceiling(symbolChars / 4.0);
            return Math.Max(1, tokenEstimate);
        }

        private static void AccumulateTokenStats(IExpression expr, ref int nodes, ref long symbolChars)
        {
            if (expr is Symbol s)
            {
                nodes++;
                symbolChars += s.Name.Length;
                return;
            }
            if (expr is Operation op)
            {
                nodes++;
                foreach (var arg in op.Arguments)
                {
                    AccumulateTokenStats(arg, ref nodes, ref symbolChars);
                }
                return;
            }
            nodes++;
            symbolChars += expr.ToDisplayString().Length;
        }

        /// <summary>
        /// Explicitly marks two expressions as disjoint (Inequality).
        /// </summary>
        public void AddDisjoint(IExpression a, IExpression b)
        {
            _lock.EnterWriteLock();
            try
            {
                _index.AddDisjoint(CSharpIO.FormatExpr(a), CSharpIO.FormatExpr(b));
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Updates the state of a fact, creating a temporal history.
        /// </summary>
        public void UpdateState(MemoryFact oldFact, IExpression newExpr)
        {
            _lock.EnterWriteLock();
            try
            {
                if (oldFact.IsInvalidated || oldFact.IsSuperseded) return;

                var newFact = AddFactInternal(newExpr, oldFact.Scope, oldFact.Certainty, oldFact.Type, oldFact.Dependencies, allowDedup: false);
                
                // Link Temporal State
                newFact.PreviousVersion = oldFact;
                oldFact.NextVersion = newFact;
                oldFact.IsSuperseded = true;

                // Move old fact to archive immediately or let maintenance handle it?
                // Usually we want active memory to reflect NOW.
                _facts.Remove(oldFact);
                _index.Remove(oldFact);
                RemoveHashIndex(oldFact);
                _archive.Add(oldFact);
                
                if (oldFact.Expression is Equality) _eGraphDirty = true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Retrieves neighbors of a symbol (Adjacency).
        /// </summary>
        public IEnumerable<MemoryFact> GetRelatedFacts(string symbol)
        {
            _lock.EnterReadLock();
            try
            {
                return _index.GetRelated(symbol).ToList();
            }
            finally { _lock.ExitReadLock(); }
        }

        /// <summary>
        /// Retrieves all active facts across all scopes (vNext).
        /// </summary>
        public IEnumerable<MemoryFact> GetAllFacts(bool includeArchive = false)
        {
            _lock.EnterReadLock();
            try
            {
                return includeArchive ? _facts.Concat(_archive).ToList() : _facts.ToList();
            }
            finally { _lock.ExitReadLock(); }
        }
        
        private void EnsureEGraphClean()
        {
            if (_eGraphDirty) RebuildEGraph();
            else _eGraph.Rebuild();
        }

        private void AddToEGraph(MemoryFact fact)
        {
            if (fact.ContentType == MemoryContentType.Artifact) return;
            if (_eGraphDirty)
            {
                RebuildEGraph();
                return;
            }

            try
            {
                if (fact.Expression is Equality eq && fact.Certainty > 0.9) // High certainty threshold for structural merging
                {
                    if (CheckDisjointContradiction(eq.LeftOperand, eq.RightOperand))
                    {
                        // Contradiction detected via Disjoint Set!
                        // Invalidate this fact as it violates a known constraint.
                        InvalidateFact(fact, 1.0);
                        return;
                    }

                    int id = _eGraph.Add(fact.Expression);
                    int leftId = _eGraph.Add(eq.LeftOperand);
                    int rightId = _eGraph.Add(eq.RightOperand);
                    _eGraph.Union(leftId, rightId);
                }
                else
                {
                    _eGraph.Add(fact.Expression);
                }
            }
            catch (Exception)
            {
                // Fallback: If EGraph fails (e.g. type issues), just ignore EGraph integration for this fact
            }
        }

        private bool CheckDisjointContradiction(IExpression a, IExpression b)
        {
            // Check if any existing disjoint pair is violated by this union
            foreach (var pair in _index.GetDisjointPairs())
            {
                // If a is equivalent to pair.1 and b is equivalent to pair.2 (or vice versa), it's a contradiction.
                try
                {
                    var expr1 = CSharpIO.ParseExpressions(pair.Item1).FirstOrDefault();
                    var expr2 = CSharpIO.ParseExpressions(pair.Item2).FirstOrDefault();
                    if (expr1 == null || expr2 == null) continue;

                    int idA = _eGraph.Add(a);
                    int idB = _eGraph.Add(b);
                    int id1 = _eGraph.Add(expr1);
                    int id2 = _eGraph.Add(expr2);

                    int rootA = _eGraph.Find(idA);
                    int rootB = _eGraph.Find(idB);
                    int root1 = _eGraph.Find(id1);
                    int root2 = _eGraph.Find(id2);

                    if ((rootA == root1 && rootB == root2) || (rootA == root2 && rootB == root1))
                    {
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }
        
        public void RebuildEGraph()
        {
            _eGraph.Dispose();
            _eGraph = new EGraph();
            foreach(var fact in _facts)
            {
                if (!fact.IsInvalidated && !fact.IsSuperseded)
                {
                    if (fact.ContentType == MemoryContentType.Artifact) continue;
                    // Inline logic for safety:
                    try
                    {
                        if (fact.Expression is Equality eq && fact.Certainty > 0.9)
                        {
                            if (CheckDisjointContradiction(eq.LeftOperand, eq.RightOperand))
                            {
                                // Mark fact as ambiguous/invalidated during rebuild if it violates constraints
                                fact.IsAmbiguous = true;
                                continue;
                            }

                            int leftId = _eGraph.Add(eq.LeftOperand);
                            int rightId = _eGraph.Add(eq.RightOperand);
                            _eGraph.Union(leftId, rightId);
                        }
                        _eGraph.Add(fact.Expression);
                    }
                    catch { }
                }
            }
            _eGraph.Rebuild();
            _eGraphDirty = false;
        }

        /// <summary>
        /// Finds all known facts that are semantically equivalent to the given expression according to the EGraph.
        /// </summary>
        public IEnumerable<IExpression> GetEquivalentExpressions(IExpression expr)
        {
            _lock.EnterWriteLock(); // Modifies EGraph
            try
            {
                EnsureEGraphClean();
                int id;
                try 
                {
                    id = _eGraph.Add(expr); // Add query to graph to find its canonical class
                }
                catch { return new List<IExpression>(); }
                
                int root = _eGraph.Find(id);
                var result = new List<IExpression>();

                // This is O(N) over facts, but precise.
                foreach(var fact in _facts)
                {
                    if (fact.IsInvalidated || fact.IsSuperseded) continue;
                    if (fact.ContentType == MemoryContentType.Artifact) continue;
                    
                    bool match = false;
                    try
                    {
                         int factId = _eGraph.Add(fact.Expression);
                         if (_eGraph.Find(factId) == root)
                         {
                             match = true;
                         }
                    }
                    catch { }

                    if (match) result.Add(fact.Expression);
                }
                return result;
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Applies rewrite rules to the memory store's EGraph to derive new connections and potential facts.
        /// </summary>
        public void Reason(IEnumerable<Rule> rules, int limit = 1000)
        {
            _lock.EnterWriteLock();
            try
            {
                EnsureEGraphClean();
                var matches = EGraphMatcher.FindMatches(_eGraph, rules);
                int count = 0;
                
                foreach (var match in matches)
                {
                    if (count++ > limit) break;
                    
                    try
                    {
                        if (match.Rule.Condition != null)
                        {
                            var exprBindings = new Dictionary<string, IExpression>();
                            foreach (var kvp in match.Bindings)
                            {
                                // Extract the best expression for the bound class to check condition
                                exprBindings[kvp.Key] = EGraphExtract.ExtractBest(_eGraph, kvp.Value);
                            }
                            
                            if (!match.Rule.Condition(exprBindings.ToImmutableDictionary()))
                            {
                                continue;
                            }
                        }

                        int newId = EGraphInstantiator.Instantiate(_eGraph, match.Rule.Replacement, match.Bindings);
                        _eGraph.Union(match.RootClassId, newId);
                    }
                    catch { }
                }
                _eGraph.Rebuild();
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Simplifies an expression using the current EGraph state and extracts the best representation.
        /// </summary>
        public IExpression Simplify(IExpression expr)
        {
            _lock.EnterWriteLock();
            try
            {
                EnsureEGraphClean();
                try
                {
                    int id = _eGraph.Add(expr);
                    return EGraphExtract.ExtractBest(_eGraph, id);
                }
                catch 
                {
                    return expr;
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Combines multiple facts into a single summary fact (Cycle 15/33).
        /// </summary>
        public void Fold(IEnumerable<MemoryFact> facts, string scope = "Global")
        {
            _lock.EnterWriteLock();
            try
            {
                var factList = facts.ToList();
                if (factList.Count == 0) return;

                var summaryExpr = new Summary(factList.Select(f => f.Expression).ToImmutableList());
                
                double avgCert = factList.Average(f => f.Certainty);
                AddFactInternal(summaryExpr, scope, avgCert, ReActType.Generic, factList, allowDedup: false);
                
                foreach (var f in factList)
                {
                    f.IsSuperseded = true;
                    _index.Remove(f);
                    _facts.Remove(f);
                    RemoveHashIndex(f);
                    _archive.Add(f);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        private void ResolveContradictions(MemoryFact newFact)
        {
            if (newFact.Kind != FactKind.Assertion) return;
            
            if (newFact.Expression is Equality newEq)
            {
                var newKey = GetAssignmentKey(newEq.LeftOperand);
                var conflictGroup = newFact.ConflictGroupId ?? Guid.NewGuid().ToString();

                bool conflictFound = false;
                foreach (var fact in _facts)
                {
                    if (fact == newFact || fact.IsInvalidated || fact.IsSuperseded) continue;
                    if (fact.Expression is Equality eq)
                    {
                        var key = GetAssignmentKey(eq.LeftOperand);
                        if (key != newKey) continue;
                        if (!eq.RightOperand.InternalEquals(newEq.RightOperand))
                        {
                            conflictFound = true;
                            fact.ConflictGroupId = conflictGroup;
                            newFact.ConflictGroupId = conflictGroup;
                            
                            // Winner policy: highest certainty and most recent validated source remains active
                            if (newFact.Certainty > fact.Certainty || (newFact.Certainty == fact.Certainty && newFact.CreatedAt > fact.CreatedAt))
                            {
                                InvalidateFact(fact, 1.0);
                                fact.InvalidationReason = "ConflictLost";
                            }
                            else
                            {
                                InvalidateFact(newFact, 1.0);
                                newFact.InvalidationReason = "ConflictLost";
                            }
                        }
                    }
                }
                
                if (conflictFound)
                {
                    _eGraphDirty = true;
                }
            }
        }

        private static string GetAssignmentKey(IExpression expr)
        {
            try { return CSharpIO.FormatExpr(expr.Canonicalize()); }
            catch { return expr.ToDisplayString(); }
        }

        public double CalculateSimilarity(IExpression a, IExpression b)
        {
            // Pure function, no state modification. No lock needed.
            var symbolsA = new HashSet<string>();
            var symbolsB = new HashSet<string>();
            HarvestSymbols(a, symbolsA);
            HarvestSymbols(b, symbolsB);

            if (symbolsA.Count == 0 && symbolsB.Count == 0) return 1.0;
            
            int intersect = symbolsA.Count(s => symbolsB.Contains(s));
            int union = symbolsA.Count + symbolsB.Count - intersect;
            return (double)intersect / union;
        }

        public IEnumerable<MemoryFact> GetFacts(string scope = "Global")
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                var result = new List<MemoryFact>();
                var now = DateTime.UtcNow;
                var activeScopes = new HashSet<string> { "Global", scope };
                
                string current = scope;
                while (ScopeParents.TryGetValue(current, out var parent))
                {
                    activeScopes.Add(parent);
                    current = parent;
                }

                foreach (var fact in _facts)
                {
                    if (fact.IsInvalidated || fact.IsSuperseded) continue;
                    if (!activeScopes.Contains(fact.Scope)) continue;

                    // UpdateFactPotency modifies the fact object.
                    // Since we have UpgradeableReadLock, we can enter write lock if needed.
                    _lock.EnterWriteLock();
                    try
                    {
                        UpdateFactPotency(fact, now);
                    }
                    finally { _lock.ExitWriteLock(); }
                    
                    result.Add(fact);
                }
                return result;
            }
            finally { _lock.ExitUpgradeableReadLock(); }
        }

        private bool HasWilds(IExpression expr)
        {
            if (expr is Wild) return true;
            if (expr is Operation op) return op.Arguments.Any(HasWilds);
            return false;
        }

        public IEnumerable<MemoryFact> QueryV2(IExpression query, QueryOptions options)
        {
            bool hasWilds = HasWilds(query);

            if (hasWilds)
            {
                _lock.EnterWriteLock();
                try
                {
                    EnsureEGraphClean();
                    var rules = new[] { new Rule(query, new Symbol("Match")) { Name = "Query" } };
                    var matches = EGraphMatcher.FindMatches(_eGraph, rules);

                    var matchedIds = new HashSet<int>(matches.Select(m => m.RootClassId));
                    var resultSet = new HashSet<MemoryFact>();

                    if (matchedIds.Count > 0)
                    {
                        var candidates = options.IncludeArchive ? _facts.Concat(_archive) : _facts;
                        foreach (var fact in candidates)
                        {
                            try
                            {
                                int factId = _eGraph.Add(fact.Expression);
                                if (matchedIds.Contains(_eGraph.Find(factId)))
                                {
                                    resultSet.Add(fact);
                                }
                            }
                            catch { }
                        }
                    }

                    return FilterAndScoreResults(resultSet, options);
                }
                finally { _lock.ExitWriteLock(); }
            }

            _lock.EnterReadLock();
            try
            {
                var querySymbols = new HashSet<string>();
                HarvestSymbols(query, querySymbols);

                IEnumerable<MemoryFact> candidates;

                if (querySymbols.Count > 0)
                {
                    var resultSet = new HashSet<MemoryFact>();
                    foreach (var sym in querySymbols)
                    {
                        foreach (var fact in _index.GetRelated(sym))
                        {
                            resultSet.Add(fact);
                        }
                    }
                    
                    if (options.IncludeArchive || options.IncludeInvalidated || options.IncludeSuperseded)
                    {
                        // Diagnostic scan: Archive and invalidated facts are not in _index
                        foreach (var fact in _facts.Concat(_archive))
                        {
                            if (resultSet.Contains(fact)) continue;
                            var factSymbols = new HashSet<string>();
                            HarvestSymbols(fact.Expression, factSymbols);
                            if (querySymbols.Overlaps(factSymbols)) resultSet.Add(fact);
                        }
                    }
                    
                    candidates = resultSet;
                }
                else
                {
                    candidates = options.IncludeArchive ? _facts.Concat(_archive) : _facts;
                }

                return FilterAndScoreResults(candidates, options);
            }
            finally { _lock.ExitReadLock(); }
        }

        private IEnumerable<MemoryFact> FilterAndScoreResults(IEnumerable<MemoryFact> candidates, QueryOptions options)
        {
            var activeScopes = new HashSet<string> { "Global", options.Scope };
            string current = options.Scope;
            while (ScopeParents.TryGetValue(current, out var parent))
            {
                activeScopes.Add(parent);
                current = parent;
            }

            var scoredResults = new List<(MemoryFact Fact, double Score)>();
            foreach (var fact in candidates)
            {
                if (!options.IncludeInvalidated && fact.IsInvalidated) continue;
                if (!options.IncludeSuperseded && fact.IsSuperseded) continue;
                if (ExcludeArtifactsFromRecall && fact.ContentType == MemoryContentType.Artifact) continue;
                if (fact.QualityScore < options.MinQualityScore) continue;
                if (!options.IncludeNoise && fact.Kind == FactKind.Noise) continue;
                if (!options.IncludeDiagnostics && fact.Kind == FactKind.ToolTrace) continue;
                
                if (!activeScopes.Contains(fact.Scope)) continue;
                if (!options.IncludeArchive && _archive.Contains(fact)) continue; 

                double score = fact.Potency * fact.Certainty;
                if (fact.ContentType == MemoryContentType.Artifact) score *= ArtifactScoreMultiplier;
                if (fact.ContentType == MemoryContentType.Summary) score *= 1.2; // Summary boost (v4)
                score += fact.Priority * PriorityWeight;

                // Duplicate penalty
                if (fact.OccurrenceCount > 1)
                {
                    score *= 1.0 / Math.Log2(2 + fact.OccurrenceCount);
                }

                scoredResults.Add((fact, score));
            }

            var sorted = scoredResults.OrderByDescending(r => r.Score).Select(r => r.Fact).ToList();

            if (options.DedupResults)
            {
                var finalResults = new List<MemoryFact>();
                var clusterCounts = new Dictionary<string, int>();
                foreach (var fact in sorted)
                {
                    string clusterKey = fact.SemanticKey;
                    clusterCounts.TryGetValue(clusterKey, out int count);
                    if (count < options.MaxPerCluster)
                    {
                        finalResults.Add(fact);
                        clusterCounts[clusterKey] = count + 1;
                    }
                }
                return finalResults;
            }

            return sorted;
        }

        public IEnumerable<MemoryFact> Query(IExpression query, string scope = "Global", bool includeArchive = false)
        {
            return QueryV2(query, new QueryOptions { Scope = scope, IncludeArchive = includeArchive });
        }

        private void HarvestSymbols(IExpression expr, HashSet<string> symbols)
        {
            if (expr is Symbol s)
            {
                if (s.Name.Length <= MemoryIndex.MaxSymbolLength)
                {
                    if (!MemoryContentEncoding.IsHashedSymbol(s))
                    {
                        symbols.Add(s.Name);
                    }
                }
            }
            else if (expr is Operation op)
            {
                foreach (var arg in op.Arguments) HarvestSymbols(arg, symbols);
            }
        }

        internal static double PhiSigmoid(double x)
        {
            if (x > 0.8) return 1.0;
            if (x > 0.3) return x;
            return 0.0;
        }

        public void Maintenance()
        {
            DeepMaintenance();
        }

        public void FastMaintenance()
        {
            _lock.EnterWriteLock();
            try
            {
                var now = DateTime.UtcNow;
                var activeTokens = _facts.Sum(f => f.Tokens);
                
                if (activeTokens <= TargetActiveTokens) return;

                // Move ephemeral and low potency facts first
                var toMove = _facts.OrderBy(f => f.Retention == MemoryRetentionPolicy.Pinned)
                                   .ThenByDescending(f => f.Retention == MemoryRetentionPolicy.Ephemeral)
                                   .ThenBy(f => f.Potency)
                                   .ToList();

                foreach (var fact in toMove)
                {
                    if (fact.Retention == MemoryRetentionPolicy.Pinned) break;

                    _index.Remove(fact);
                    _archive.Add(fact);
                    _facts.Remove(fact);
                    if (fact.Expression is Equality) _eGraphDirty = true;

                    activeTokens -= fact.Tokens;
                    if (activeTokens <= TargetActiveTokens) break;
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void DeepMaintenance()
        {
            _lock.EnterWriteLock();
            try
            {
                var now = DateTime.UtcNow;

                // 1. Decay pass
                for (int i = _facts.Count - 1; i >= 0; i--)
                {
                    var fact = _facts[i];
                    UpdateFactPotency(fact, now);

                    // v5 Staleness Policy: Hard Expire
                    if (fact.ValidToUtc.HasValue && fact.ValidToUtc.Value < now && fact.Staleness == StalenessPolicy.HardExpire)
                    {
                        InvalidateFact(fact, 1.0);
                        fact.InvalidationReason = "StaleHardExpire";
                        continue;
                    }

                    if (fact.Retention != MemoryRetentionPolicy.Pinned && fact.Potency < HysteresisLow && !fact.IsAmbiguous)
                    {
                        _index.Remove(fact); // Active to archive
                        _archive.Add(fact);
                        _facts.RemoveAt(i);
                        if (fact.Expression is Equality) _eGraphDirty = true;
                    }
                }

                for (int i = _archive.Count - 1; i >= 0; i--)
                {
                    var fact = _archive[i];
                    UpdateFactPotency(fact, now);

                    if (fact.Potency > HysteresisHigh)
                    {
                        _facts.Add(fact);
                        _index.Add(fact); // Archive to active
                        _archive.RemoveAt(i);
                        if (fact.Expression is Equality) _eGraphDirty = true;
                    }
                    else if (fact.Certainty < 0.1)
                    {
                        RemoveHashIndex(fact);
                        _semanticIndex.Remove(fact.SemanticKey);
                        _archive.RemoveAt(i);
                    }
                }

                // 2. Contradiction consolidation & Ambiguity cleanup
                // facts with zero potency or superseded/invalidated should not be in active set
                _facts.RemoveAll(f => f.IsInvalidated || f.IsSuperseded || (f.Potency <= 0 && f.Retention != MemoryRetentionPolicy.Pinned));

                // 3. Semantic cluster compaction (v4)
                var semanticGroups = _facts.GroupBy(f => f.SemanticKey).Where(g => g.Count() > 1).ToList();
                foreach (var group in semanticGroups)
                {
                    var canonical = group.OrderByDescending(f => f.Certainty).ThenByDescending(f => f.CreatedAt).First();
                    foreach (var other in group)
                    {
                        if (other == canonical) continue;
                        MergeInto(canonical, other);
                        _index.Remove(other);
                        _archive.Add(other);
                        _facts.Remove(other);
                    }
                }

                // 4. Load control
                FastMaintenance();
            }
            finally { _lock.ExitWriteLock(); }
        }

        private void UpdateFactPotency(MemoryFact fact, DateTime now)
        {
            var ageHoursTotal = (now - fact.CreatedAt).TotalHours;
            double rawPotency = Math.Exp(-_decayRate * ageHoursTotal);
            fact.Potency = PhiSigmoid(rawPotency);

            var ageHoursDelta = (now - fact.LastSeenAtUtc).TotalHours;
            if (ageHoursDelta > 0)
            {
                fact.Certainty *= Math.Exp(-CertaintyDecayRate * ageHoursDelta);
                fact.LastSeenAtUtc = now;
                fact.LastDecayUpdate = now;
            }
        }

        public void InvalidateFact(MemoryFact fact, double impact = 1.0)
        {
            _lock.EnterWriteLock();
            try
            {
                if (fact.IsInvalidated) return;
                
                fact.Certainty *= (1 - impact);

                if (fact.Certainty <= 0.3)
                {
                    fact.IsInvalidated = true;
                    _index.Remove(fact);
                    RemoveHashIndex(fact);
                    
                    if (fact.Expression is Equality) _eGraphDirty = true;

                    foreach (var sub in _subscribers)
                    {
                        if (sub.SubscribedScopes.Contains("Global") || sub.SubscribedScopes.Contains(fact.Scope))
                        {
                            sub.OnFactInvalidated(fact);
                        }
                    }
                }

                double phiVal = PhiSigmoid(fact.Certainty);
                foreach (var dependent in _facts.FindAll(f => f.Dependencies.Contains(fact)))
                {
                    InvalidateFact(dependent, 1.0 - phiVal);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }
        
        public void Save(string rootPath)
        {
            CurrentRootPath = rootPath;
            _lock.EnterWriteLock();
            try
            {
                var factsDir = Path.Combine(rootPath, "Facts");
                if (!Directory.Exists(factsDir)) Directory.CreateDirectory(factsDir);

                var artifactsDir = Path.Combine(rootPath, "Artifacts");
                if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

                // Save Artifacts
                foreach (var kvp in _artifacts)
                {
                    var path = Path.Combine(artifactsDir, kvp.Key); // Key is Hash
                    if (!File.Exists(path))
                    {
                        File.WriteAllText(path, kvp.Value);
                    }
                }

                var indexData = new HammIndexData 
                { 
                    SchemaVersion = 6,
                    ScopeParents = new Dictionary<string, string>(ScopeParents),
                    DedupInvariants = new Dictionary<string, object> 
                    {
                        { "SemanticKeyCount", _semanticIndex.Count },
                        { "ArtifactCount", _artifacts.Count }
                    },
                    PolicySnapshot = new Dictionary<string, object>
                    {
                        { "NoiseScoreThreshold", NoiseScoreThreshold },
                        { "ToolTraceScoreThreshold", ToolTraceScoreThreshold },
                        { "TargetActiveTokens", TargetActiveTokens },
                        { "AutoNoiseByPathDisabled", AutoNoiseByPathDisabled },
                        { "Consistency", Consistency.ToString() }
                    }
                };

                // Collect all facts (Active + Archive)
                var allFacts = _facts.Concat(_archive).ToList();

                foreach (var fact in allFacts)
                {
                    // Save text file
                    var path = Path.Combine(factsDir, fact.Id + ".txt");
                    // Use CanonicalText as authoritative payload (v6)
                    var text = fact.CanonicalText;
                    if (string.IsNullOrEmpty(text)) text = CSharpIO.FormatExpr(fact.Expression);
                    
                    File.WriteAllText(path, text);

                    // Build Metadata
                    var meta = new FactMetadata
                    {
                        CreatedAt = fact.CreatedAt,
                        Potency = fact.Potency,
                        Certainty = fact.Certainty,
                        Scope = fact.Scope,
                        Type = fact.Type,
                        Reach = fact.Reach,
                        Tokens = fact.Tokens,
                        ContentHash = fact.ContentHash,
                        SemanticKey = fact.SemanticKey,
                        CanonicalText = fact.CanonicalText,
                        OccurrenceCount = fact.OccurrenceCount,
                        QualityScore = fact.QualityScore,
                        SizeBytes = fact.SizeBytes,
                        ContentType = fact.ContentType,
                        Kind = fact.Kind,
                        Retention = fact.Retention,
                        Priority = fact.Priority,
                        Tags = new List<string>(fact.Tags),
                        Metadata = new Dictionary<string, object>(fact.Metadata),
                        DependencyIds = fact.Dependencies.Select(d => d.Id).ToList(),
                        IsInvalidated = fact.IsInvalidated,
                        InvalidationReason = fact.InvalidationReason,
                        IsAmbiguous = fact.IsAmbiguous,
                        IsSuperseded = fact.IsSuperseded,
                        LastDecayUpdate = fact.LastDecayUpdate,
                        ConflictGroupId = fact.ConflictGroupId,
                        SourceType = fact.SourceType,
                        Source = fact.Source,
                        IngestOperationId = fact.IngestOperationId,
                        RuleName = fact.RuleName,
                        FirstSeenAtUtc = fact.FirstSeenAtUtc,
                        LastSeenAtUtc = fact.LastSeenAtUtc,
                        PreviousVersionId = fact.PreviousVersion?.Id,
                        NextVersionId = fact.NextVersion?.Id,
                        IsArchived = _archive.Contains(fact),
                        
                        // v5 Fields
                        ValidFromUtc = fact.ValidFromUtc,
                        ValidToUtc = fact.ValidToUtc,
                        SupersedesFactIds = new List<string>(fact.SupersedesFactIds),
                        ContextTags = new Dictionary<string, string>(fact.ContextTags),
                        Staleness = fact.Staleness,
                        TrustScore = fact.TrustScore,
                        SourceReliability = fact.SourceReliability,
                        Validation = fact.Validation,
                        LastVerificationUtc = fact.LastVerificationUtc,
                        IsQuarantined = fact.IsQuarantined,
                        SelfCitationCount = fact.SelfCitationCount,

                        // v6 Fields
                        IntegrityState = fact.IntegrityState,
                        PayloadSource = fact.PayloadSource,
                        LastIntegrityRepairUtc = fact.LastIntegrityRepairUtc,
                        PayloadRevision = fact.PayloadRevision
                    };
                    indexData.Facts[fact.Id] = meta;
                }

                // Add health snapshot
                indexData.Health = CalculateHealthReport();

                // Save index
                var json = JsonSerializer.Serialize(indexData, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(rootPath, "HAMM.index.json"), json);
            }
            finally { _lock.ExitWriteLock(); }
        }


        public HealthSnapshot GetHealthReport()
        {
            _lock.EnterReadLock();
            try { return CalculateHealthReport(); }
            finally { _lock.ExitReadLock(); }
        }

        private HealthSnapshot CalculateHealthReport()
        {
            var now = DateTime.UtcNow;
            var activeFacts = _facts.Where(f => !f.IsInvalidated && !f.IsSuperseded).ToList();
            var totalFacts = _facts.Count + _archive.Count;
            
            var activeTokens = activeFacts.Sum(f => f.Tokens);
            var cognitiveLoad = TokenCapacity > 0 ? (double)activeTokens / TokenCapacity : 0;
            
            var uniqueTexts = new HashSet<string>(activeFacts.Select(f => f.CanonicalText));
            var duplicateCount = activeFacts.Count - uniqueTexts.Count;
            
            var noiseCount = activeFacts.Count(f => f.Kind == FactKind.Noise);
            var noiseTokens = activeFacts.Where(f => f.Kind == FactKind.Noise).Sum(f => f.Tokens);

            var report = new HealthSnapshot
            {
                TotalFacts = totalFacts,
                ActiveFacts = activeFacts.Count,
                ArchivedFacts = _archive.Count,
                ActiveTokens = activeTokens,
                CognitiveLoad = cognitiveLoad,
                UniqueTextRatioActive = activeFacts.Count > 0 ? (double)uniqueTexts.Count / activeFacts.Count : 1.0,
                DuplicateRatioActive = activeFacts.Count > 0 ? (double)duplicateCount / activeFacts.Count : 0.0,
                NoiseRatioActive = activeFacts.Count > 0 ? (double)noiseCount / activeFacts.Count : 0.0,
                AmbiguousActiveCount = activeFacts.Count(f => f.IsAmbiguous),
                ConflictGroupCount = activeFacts.Select(f => f.ConflictGroupId).Where(id => id != null).Distinct().Count(),
                LastMaintenanceUtc = now,
                PayloadMismatchCount = activeFacts.Count(f => f.IntegrityState != PayloadIntegrityState.Ok),
                IntegrityRepairCountLastLoad = LastLoadRepairCount,
                TokenDriftCount = activeFacts.Sum(f => f.OriginalTokens.HasValue ? Math.Abs(f.OriginalTokens.Value - f.Tokens) : 0)
            };

            if (report.DuplicateRatioActive > 0.05) report.Warnings.Add("DuplicateSemanticKeyRateHigh");
            if (activeTokens > 0 && (double)noiseTokens / activeTokens > 0.5) report.Warnings.Add("NoiseTokenShareHigh");
            if (activeTokens > TargetActiveTokens) report.Warnings.Add("TargetActiveTokensExceeded");
            
            var recallEligible = activeFacts.Count(f => f.Kind != FactKind.Noise && f.Kind != FactKind.ToolTrace && f.ContentType != MemoryContentType.Artifact);
            if (activeFacts.Count > 10 && (double)recallEligible / activeFacts.Count < 0.2) report.Warnings.Add("RecallEligibleShareLow");

            return report;
        }

        private string ComputeSemanticKey(MemoryFact fact)
        {
            // SemanticKey = CanonicalExpression + FactKind + ScopeFamily
            // For now ScopeFamily is just the scope or its top-level parent
            string scopeFamily = fact.Scope;
            if (ScopeParents.TryGetValue(fact.Scope, out var parent)) scopeFamily = parent;

            return $"{fact.ContentHash}|{(int)fact.Kind}|{scopeFamily}";
        }

        private double ComputeQualityScore(MemoryFact fact)
        {
            // Compute QualityScore using:
            // - structure (expression vs bare atom)
            // - length/token distribution
            // - known noise patterns
            // - source type
            
            double score = 1.0;
            if (fact.Expression is Symbol) score *= 0.8; // Bare atoms slightly lower
            
            if (IsPathOrNoisy(fact.CanonicalText)) score *= 0.25;
            if (fact.Tokens < 2) score *= 0.5; // Very short
            if (fact.Tokens > 1000) score *= 0.7; // Very long (might be noisy)
            
            if (fact.SourceType == IngestSourceType.Tool) score *= 0.9;
            
            // Signal detection: command signatures boost ToolTrace eligibility
            if (fact.CanonicalText.Contains("Executing:") || fact.CanonicalText.Contains("Command:") || fact.CanonicalText.Contains("Exit Code:"))
            {
                score = Math.Max(score, ToolTraceScoreThreshold + 0.1);
            }

            return Math.Clamp(score, 0.0, 1.0);
        }

        private bool DetectSecurityRisk(string text, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var lower = text.ToLowerInvariant();

            // 1. Secret/PII patterns (simplified)
            if (lower.Contains("api_key") || lower.Contains("secret") || lower.Contains("password"))
            {
                reason = "PotentialSecretDetected";
                return true;
            }

            // 2. Prompt Injection patterns
            if (lower.Contains("ignore all previous instructions") || lower.Contains("system prompt") || lower.Contains("you are now a"))
            {
                reason = "PotentialPromptInjectionDetected";
                return true;
            }

            return false;
        }

        private double CalculateInitialTrust(IngestSourceType sourceType, string source)
        {
            return sourceType switch
            {
                IngestSourceType.System => 1.0,
                IngestSourceType.Rule => 0.95,
                IngestSourceType.User => 0.9,
                IngestSourceType.Tool => 0.8,
                _ => 0.5
            };
        }

        private double CalculateInitialReliability(IngestSourceType sourceType, string source)
        {
            return sourceType switch
            {
                IngestSourceType.System => 1.0,
                IngestSourceType.User => 0.95,
                IngestSourceType.Rule => 0.9,
                IngestSourceType.Tool => 0.85,
                _ => 0.7
            };
        }

        private FactKind DetermineFactKind(MemoryFact fact)
        {
            if (fact.Type == ReActType.Thought) return FactKind.Thought;
            if (fact.Type == ReActType.Action) return FactKind.Action;

            if (fact.ContentType == MemoryContentType.Summary) return FactKind.Summary;
            if (fact.ContentType == MemoryContentType.Artifact) return FactKind.ArtifactPointer;
            
            if (fact.Expression is Operation op)
            {
                var head = op.Head;
                if (head == "Rule" || head == "Define" || head == "Concept" ||
                    head == "Func:Rule" || head == "Func:Define" || head == "Func:Concept" ||
                    head == "Procedure" || head == "Func:Procedure" ||
                    head == "Cmd" || head == "Func:Cmd") return FactKind.Assertion;
                if (head == "Goal" || head == "Todo" ||
                    head == "Func:Goal" || head == "Func:Todo" ||
                    head == "ActivePointer" || head == "Func:ActivePointer") return FactKind.Directive;
            }

            if (fact.QualityScore < NoiseScoreThreshold) return FactKind.Noise;
            
            // Check for explicit ToolTrace indicators
            if (fact.SourceType == IngestSourceType.Tool || fact.QualityScore < ToolTraceScoreThreshold) return FactKind.ToolTrace;

            if (fact.SourceType == IngestSourceType.System) return FactKind.SystemEvent;

            return FactKind.Assertion;
        }

        public void Load(string rootPath)
        {
            CurrentRootPath = rootPath;
            _lock.EnterWriteLock();
            try
            {
                _facts.Clear();
                _archive.Clear();
                _index.Clear();
                _hashIndex.Clear();
                _semanticIndex.Clear();
                _artifacts.Clear();
                LastLoadRepairCount = 0;
                
                var artifactsDir = Path.Combine(rootPath, "Artifacts");
                if (Directory.Exists(artifactsDir))
                {
                    var artifactFiles = Directory.GetFiles(artifactsDir);
                    foreach (var file in artifactFiles)
                    {
                        var hash = Path.GetFileName(file);
                        try
                        {
                            var content = File.ReadAllText(file);
                            _artifacts[hash] = content;
                        }
                        catch { }
                    }
                }

                var factsDir = Path.Combine(rootPath, "Facts");
                if (!Directory.Exists(factsDir)) Directory.CreateDirectory(factsDir);

                var indexPath = Path.Combine(rootPath, "HAMM.index.json");
                HammIndexData indexData = new HammIndexData();
                if (File.Exists(indexPath))
                {
                    try { indexData = JsonSerializer.Deserialize<HammIndexData>(File.ReadAllText(indexPath)) ?? new HammIndexData(); }
                    catch { }
                }

                if (indexData.ScopeParents != null)
                {
                    ScopeParents.Clear();
                    foreach (var kvp in indexData.ScopeParents) ScopeParents[kvp.Key] = kvp.Value;
                }

                if (indexData.PolicySnapshot != null && indexData.PolicySnapshot.TryGetValue("Consistency", out var consistencyObj) && consistencyObj != null)
                {
                    if (Enum.TryParse<ConsistencyMode>(consistencyObj.ToString(), out var mode)) Consistency = mode;
                }

                // Collect all IDs from files and metadata
                var fileIds = new HashSet<string>();
                if (Directory.Exists(factsDir))
                {
                    foreach (var file in Directory.GetFiles(factsDir, "*.txt"))
                    {
                        fileIds.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
                
                var allIds = new HashSet<string>(fileIds);
                foreach (var id in indexData.Facts.Keys) allIds.Add(id);

                var loadedFacts = new Dictionary<string, MemoryFact>();
                var allLoadedFactsList = new List<MemoryFact>();

                foreach (var id in allIds)
                {
                    var hasMeta = indexData.Facts.TryGetValue(id, out var meta);
                    var filePath = Path.Combine(factsDir, id + ".txt");
                    var hasFile = fileIds.Contains(id);

                    // 1. Authoritative Payload Resolution
                    string authoritativePayload = string.Empty;
                    PayloadSource source = PayloadSource.FactFile;
                    
                    if (hasMeta && meta != null && !string.IsNullOrWhiteSpace(meta.CanonicalText))
                    {
                        // Defensive null-check: meta may be present in dictionary but have a null value.
                        authoritativePayload = meta.CanonicalText;
                        source = PayloadSource.MetadataCanonical;
                    }
                    else if (hasFile)
                    {
                        try { authoritativePayload = File.ReadAllText(filePath); } catch { continue; }
                        source = PayloadSource.FactFile;
                    }
                    else
                    {
                        continue; // No payload available
                    }

                    // 2. Strict Parse
                    IExpression expr;
                    try 
                    { 
                        var exprs = CSharpIO.ParseExpressionsStrict(authoritativePayload);
                        if (exprs.Count > 0) expr = exprs[0];
                        else expr = new Symbol(authoritativePayload);
                    }
                    catch 
                    { 
                        // Strict parse failed -> Fallback to literal symbol, do not truncate
                        expr = new Symbol(authoritativePayload);
                    }

                    // 3. Normalize & Recompute Derived Fields
                    bool sanitized = false;
                    var captured = new Dictionary<string, string>();
                    var normalized = NormalizeExpression(expr, ref sanitized, captured);
                    foreach(var kvp in captured) _artifacts[kvp.Key] = kvp.Value;

                    var fact = new MemoryFact(normalized) { Id = id };
                    var canonicalText = GetExpressionText(normalized);
                    var contentHash = MemoryContentEncoding.ComputeHash(canonicalText);
                    var sizeBytes = Encoding.UTF8.GetByteCount(canonicalText);
                    int rawTokenEstimate = EstimateTokens(normalized);
                    bool isOversized = sanitized || rawTokenEstimate > MaxTokensPerFact;
                    
                    fact.Tokens = Math.Min(MaxTokensPerFact, rawTokenEstimate);
                    fact.ContentHash = contentHash;
                    fact.CanonicalText = canonicalText;
                    fact.SizeBytes = sizeBytes;
                    fact.ContentType = DetermineContentType(normalized, isOversized);
                    fact.SemanticKey = ComputeSemanticKey(fact); // Requires Kind/Scope, set below

                    // 4. Integrity Check & Repair Logic
                    bool repairNeeded = false;
                    fact.PayloadSource = source;
                    
                    if (hasFile)
                    {
                        // Check for file drift
                        try 
                        {
                            var fileContent = File.ReadAllText(filePath);
                            // Normalize newlines for comparison
                            if (fileContent.Trim() != authoritativePayload.Trim())
                            {
                                repairNeeded = true;
                                fact.IntegrityState = source == PayloadSource.MetadataCanonical ? PayloadIntegrityState.RecoveredFromMetadata : PayloadIntegrityState.Conflict;
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        // Missing file but had metadata
                        repairNeeded = true;
                        fact.IntegrityState = PayloadIntegrityState.RecoveredFromMetadata;
                    }

                    if (repairNeeded)
                    {
                        fact.LastIntegrityRepairUtc = DateTime.UtcNow;
                        fact.PayloadRevision++; // Will be saved back
                        LastLoadRepairCount++;
                    }

                    bool isArchivedInMeta = false;

                    // 5. Hydrate Metadata
                    if (hasMeta && meta != null)
                    {
                        // Defensive null-check: ensure metadata object is not null before accessing its properties.
                        if (meta.Tokens != fact.Tokens) fact.OriginalTokens = meta.Tokens;
                        fact.CreatedAt = meta.CreatedAt;
                        fact.Potency = meta.Potency;
                        fact.Certainty = meta.Certainty;
                        fact.Scope = meta.Scope;
                        fact.Type = meta.Type;
                        fact.Reach = meta.Reach;
                        fact.Retention = meta.Retention;
                        fact.Priority = meta.Priority;
                        isArchivedInMeta = meta.IsArchived;
                        
                        // Re-evaluate Kind based on authoritative content? Or trust metadata?
                        // Spec says derived fields must be recomputed. Kind depends on ContentType/Quality/Source.
                        // We will trust the computation logic over old metadata for consistency.
                        fact.Kind = DetermineFactKind(fact); 
                        
                        // v6+ fields
                        if (indexData.SchemaVersion >= 6)
                        {
                            fact.PayloadRevision = Math.Max(fact.PayloadRevision, meta.PayloadRevision); // Keep max if incremented during load
                            if (meta.LastIntegrityRepairUtc.HasValue) 
                                fact.LastIntegrityRepairUtc = meta.LastIntegrityRepairUtc;
                        }

                        // v3/v4 fields migration
                        if (indexData.SchemaVersion >= 3)
                        {
                            fact.OccurrenceCount = meta.OccurrenceCount;
                            fact.QualityScore = meta.QualityScore; // Recalculate?
                            fact.InvalidationReason = meta.InvalidationReason;
                            fact.ConflictGroupId = meta.ConflictGroupId;
                            fact.SourceType = meta.SourceType;
                            fact.IngestOperationId = meta.IngestOperationId;
                            fact.FirstSeenAtUtc = meta.FirstSeenAtUtc;
                            fact.LastSeenAtUtc = meta.LastSeenAtUtc;
                        }
                        else
                        {
                            // Migration logic
                            fact.OccurrenceCount = 1;
                            fact.QualityScore = ComputeQualityScore(fact);
                            fact.SourceType = IngestSourceType.User;
                            fact.FirstSeenAtUtc = fact.CreatedAt;
                            fact.LastSeenAtUtc = fact.CreatedAt;
                        }

                        // Recalculate QualityScore to ensure it matches current logic?
                        // Ideally yes, but maybe we want to preserve historical score?
                        // Spec says "Derived Field Consistency... Metadata values become advisory".
                        // QualityScore is derived from content/source.
                        fact.QualityScore = ComputeQualityScore(fact);

                        if (meta.Tags != null) fact.Tags.AddRange(meta.Tags);
                        foreach (var kvp in meta.Metadata) fact.Metadata[kvp.Key] = kvp.Value;
                        fact.IsInvalidated = meta.IsInvalidated;
                        fact.IsAmbiguous = meta.IsAmbiguous;
                        fact.IsSuperseded = meta.IsSuperseded;
                        fact.LastDecayUpdate = meta.LastDecayUpdate;
                        fact.Source = meta.Source;
                        fact.RuleName = meta.RuleName;

                        // v5 migration and hydrate
                        if (indexData.SchemaVersion >= 5)
                        {
                            fact.ValidFromUtc = meta.ValidFromUtc;
                            fact.ValidToUtc = meta.ValidToUtc;
                            if (meta.SupersedesFactIds != null) fact.SupersedesFactIds.AddRange(meta.SupersedesFactIds);
                            if (meta.ContextTags != null) foreach (var kvp in meta.ContextTags) fact.ContextTags[kvp.Key] = kvp.Value;
                            fact.Staleness = meta.Staleness;
                            fact.TrustScore = meta.TrustScore;
                            fact.SourceReliability = meta.SourceReliability;
                            fact.Validation = meta.Validation;
                            fact.LastVerificationUtc = meta.LastVerificationUtc;
                            fact.IsQuarantined = meta.IsQuarantined;
                            fact.SelfCitationCount = meta.SelfCitationCount;
                        }
                        else
                        {
                            fact.ValidFromUtc = fact.CreatedAt;
                            fact.Staleness = StalenessPolicy.AlwaysCurrent;
                            fact.TrustScore = CalculateInitialTrust(fact.SourceType, fact.Source);
                            fact.SourceReliability = CalculateInitialReliability(fact.SourceType, fact.Source);
                            fact.Validation = ValidationState.Unverified;
                            if (fact.Scope == "Quarantine") fact.IsQuarantined = true;
                        }
                    }
                    else
                    {
                        // New file detected (Heal) or lost metadata
                        fact.Kind = DetermineFactKind(fact);
                        fact.QualityScore = ComputeQualityScore(fact);
                        fact.IntegrityState = PayloadIntegrityState.RecoveredFromFile;
                        fact.PayloadSource = PayloadSource.FactFile;
                    }
                    
                    // Recompute SemanticKey after Scope/Kind are settled
                    fact.SemanticKey = ComputeSemanticKey(fact);

                    loadedFacts[id] = fact;
                    fact.Metadata["__isArchived"] = isArchivedInMeta;
                    allLoadedFactsList.Add(fact);
                }

                // 1. Dedup Integrity (Hard Requirement)
                var semanticMap = new Dictionary<string, MemoryFact>(StringComparer.Ordinal);
                
                foreach (var fact in allLoadedFactsList)
                {
                    if (fact.IsInvalidated || fact.IsSuperseded) continue;

                    if (semanticMap.TryGetValue(fact.SemanticKey, out var canonical))
                    {
                        MergeInto(canonical, fact);
                    }
                    else
                    {
                        semanticMap[fact.SemanticKey] = fact;
                    }
                }

                // 2. Migration: Re-route misclassified records (v4)
                if (indexData.SchemaVersion < 4)
                {
                    foreach (var fact in semanticMap.Values)
                    {
                        var newKind = DetermineFactKind(fact);
                        if (newKind != fact.Kind)
                        {
                            fact.Kind = newKind;
                            if (fact.Scope == "Global" || fact.Scope == "Noise")
                            {
                                if (fact.Kind == FactKind.ToolTrace) fact.Scope = "Diagnostics";
                                else if (fact.Kind == FactKind.ArtifactPointer) fact.Scope = "Artifacts";
                                else if (fact.Kind == FactKind.Noise) fact.Scope = "Noise";
                            }
                        }
                    }
                }

                // Finalize active/archive lists
                foreach (var fact in allLoadedFactsList)
                {
                    bool isArchived = fact.Metadata.TryGetValue("__isArchived", out var arch) && (bool)arch;
                    fact.Metadata.Remove("__isArchived");

                    if (isArchived || fact.IsSuperseded || fact.IsInvalidated)
                    {
                        _archive.Add(fact);
                    }
                    else
                    {
                        _facts.Add(fact);
                        _index.Add(fact);
                    }

                    if (!fact.IsInvalidated && !fact.IsSuperseded)
                    {
                        AddHashIndex(fact);
                    }
                }

                _semanticIndex.Clear();
                foreach(var kvp in semanticMap) _semanticIndex[kvp.Key] = kvp.Value;

                // Re-link references
                foreach (var kvp in loadedFacts)
                {
                    var id = kvp.Key;
                    var fact = kvp.Value;
                    if (indexData.Facts.TryGetValue(id, out var meta))
                    {
                        foreach (var depId in meta.DependencyIds)
                        {
                            if (loadedFacts.TryGetValue(depId, out var dep)) fact.Dependencies.Add(dep);
                        }
                        if (meta.PreviousVersionId != null && loadedFacts.TryGetValue(meta.PreviousVersionId, out var prev)) fact.PreviousVersion = prev;
                        if (meta.NextVersionId != null && loadedFacts.TryGetValue(meta.NextVersionId, out var next)) fact.NextVersion = next;
                    }
                }

                // Enforce target budget on startup/load to keep recall token packing deterministic.
                if (_facts.Sum(f => f.Tokens) > TargetActiveTokens)
                {
                    FastMaintenance();
                }

                // Rebuild EGraph
                RebuildEGraph();
            }
            finally { _lock.ExitWriteLock(); }
        }


        public void Heal(string rootPath) => Load(rootPath); // Load is self-healing

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _facts.Clear();
                _archive.Clear();
                _index.Clear();
                _hashIndex.Clear();
                _semanticIndex.Clear();
                _eGraphDirty = true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void Reset(string rootPath)
        {
            _lock.EnterWriteLock();
            try
            {
                Clear();
                _artifacts.Clear();
                
                var factsDir = Path.Combine(rootPath, "Facts");
                if (Directory.Exists(factsDir)) Directory.Delete(factsDir, true);
                
                var artifactsDir = Path.Combine(rootPath, "Artifacts");
                if (Directory.Exists(artifactsDir)) Directory.Delete(artifactsDir, true);
                
                var indexPath = Path.Combine(rootPath, "HAMM.index.json");
                if (File.Exists(indexPath)) File.Delete(indexPath);
                
                var logPath = Path.Combine(rootPath, "HAMM.log");
                if (File.Exists(logPath)) File.Delete(logPath);

                Directory.CreateDirectory(factsDir);
                Directory.CreateDirectory(artifactsDir);
                
                RebuildEGraph();
            }
            finally { _lock.ExitWriteLock(); }
        }

        public void Dispose()
        {
            _lock.Dispose();
            _eGraph?.Dispose();
        }
    }
}
