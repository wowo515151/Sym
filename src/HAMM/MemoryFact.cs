// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using Sym.Core;

namespace HAMM
{
    public enum ReActType
    {
        Thought,
        Action,
        Observation,
        Generic
    }

    public enum MemoryContentType
    {
        Fact,
        Summary,
        Artifact
    }

    public enum FactKind
    {
        Assertion,
        Directive,
        Summary,
        ArtifactPointer,
        ToolTrace,
        SystemEvent,
        Thought,
        Action,
        Noise
    }

    public enum IngestSourceType
    {
        User,
        Tool,
        Rule,
        System
    }

    public enum MemoryRetentionPolicy
    {
        Ephemeral,
        Normal,
        Pinned
    }

    public enum StalenessPolicy
    {
        HardExpire,
        SoftExpire,
        ContextBound,
        AlwaysCurrent
    }

    public enum ValidationState
    {
        Unverified,
        PartiallyVerified,
        Verified,
        Refuted
    }

    /// <summary>
    /// Represents a single fact in the Heuristic Associative Memory Model.
    /// </summary>
    public class MemoryFact
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public IExpression Expression { get; }
        public DateTime CreatedAt { get; set; }
        public double Potency { get; set; } = 1.0;
        public double Certainty { get; set; } = 1.0;
        public string Scope { get; set; } = "Global";
        public ReActType Type { get; set; } = ReActType.Generic;
        
        /// <summary>
        /// Measures the "reach" or influence of this fact across the agent network.
        /// </summary>
        public double Reach { get; set; } = 1.0;

        /// <summary>
        /// Estimate of token size for context window management.
        /// </summary>
        public int Tokens { get; set; } = 0;

        /// <summary>
        /// Canonical content hash used for deduplication and integrity checks.
        /// </summary>
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>
        /// Stable dedup key after normalization.
        /// </summary>
        public string SemanticKey { get; set; } = string.Empty;

        /// <summary>
        /// Normalized text used for hashing and equality.
        /// </summary>
        public string CanonicalText { get; set; } = string.Empty;

        /// <summary>
        /// Number of repeated ingests merged into this fact.
        /// </summary>
        public int OccurrenceCount { get; set; } = 1;

        /// <summary>
        /// Quality score of the fact (0.0 to 1.0).
        /// </summary>
        public double QualityScore { get; set; } = 1.0;

        /// <summary>
        /// Size in bytes of the canonical expression text.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Coarse content classification for recall and storage policy.
        /// </summary>
        public MemoryContentType ContentType { get; set; } = MemoryContentType.Fact;

        /// <summary>
        /// Fine-grained classification of the fact nature.
        /// </summary>
        public FactKind Kind { get; set; } = FactKind.Assertion;

        /// <summary>
        /// Retention policy for maintenance and archiving decisions.
        /// </summary>
        public MemoryRetentionPolicy Retention { get; set; } = MemoryRetentionPolicy.Normal;

        /// <summary>
        /// Priority modifier for recall ranking (higher = more important).
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Semantic markers used for high-performance indexing and recall.
        /// </summary>
        public List<string> Tags { get; } = new List<string>();

        /// <summary>
        /// Flexible metadata storage for custom attributes.
        /// </summary>
        public Dictionary<string, object> Metadata { get; } = new Dictionary<string, object>();

        public List<MemoryFact> Dependencies { get; } = new List<MemoryFact>();
        public bool IsInvalidated { get; set; } = false;

        /// <summary>
        /// Short code explaining why the fact was invalidated.
        /// </summary>
        public string? InvalidationReason { get; set; }

        public bool IsAmbiguous { get; set; } = false;
        public bool IsSuperseded { get; set; } = false;
        public DateTime LastDecayUpdate { get; set; }

        /// <summary>
        /// ID of a group of conflicting facts.
        /// </summary>
        public string? ConflictGroupId { get; set; }

        // --- New Features ---

        /// <summary>
        /// Provenance: The source of ingestion.
        /// </summary>
        public IngestSourceType SourceType { get; set; } = IngestSourceType.User;

        /// <summary>
        /// Provenance: The name of the rule or agent that generated this fact.
        /// </summary>
        public string Source { get; set; } = "User";

        /// <summary>
        /// ID of the batch or operation that ingested this fact.
        /// </summary>
        public string? IngestOperationId { get; set; }

        /// <summary>
        /// Provenance: The specific rule name if applicable.
        /// </summary>
        public string? RuleName { get; set; }

        public DateTime FirstSeenAtUtc { get; set; }
        public DateTime LastSeenAtUtc { get; set; }

        /// <summary>
        /// Temporal: The previous version of this fact (e.g. before an update).
        /// </summary>
        public MemoryFact? PreviousVersion { get; set; }

        /// <summary>
        /// Temporal: The next version of this fact (if superseded).
        /// </summary>
        public MemoryFact? NextVersion { get; set; }

        // --- v5 Features ---

        public DateTime ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }
        public List<string> SupersedesFactIds { get; } = new List<string>();
        public Dictionary<string, string> ContextTags { get; } = new Dictionary<string, string>();
        public StalenessPolicy Staleness { get; set; } = StalenessPolicy.AlwaysCurrent;

        public double TrustScore { get; set; } = 1.0;
        public double SourceReliability { get; set; } = 1.0;
        public ValidationState Validation { get; set; } = ValidationState.Unverified;
        public DateTime? LastVerificationUtc { get; set; }

        public bool IsQuarantined { get; set; } = false;
        public int SelfCitationCount { get; set; } = 0;

        // --- v6 Integrity ---
        public PayloadIntegrityState IntegrityState { get; set; } = PayloadIntegrityState.Ok;
        public PayloadSource PayloadSource { get; set; } = PayloadSource.FactFile;
        public DateTime? LastIntegrityRepairUtc { get; set; }
        public long PayloadRevision { get; set; } = 0;
        public int? OriginalTokens { get; set; } // For drift tracking

        public MemoryFact(IExpression expression)
        {
            Expression = expression;
            CreatedAt = DateTime.UtcNow;
            FirstSeenAtUtc = CreatedAt;
            LastSeenAtUtc = CreatedAt;
            LastDecayUpdate = CreatedAt;
            ValidFromUtc = CreatedAt;
        }

    }
}
