// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;

namespace HAMM
{
    /// <summary>
    /// Represents the persistent metadata for the HAMM memory store.
    /// </summary>
    public class HammIndexData
    {
        public int SchemaVersion { get; set; } = 6;
        public Dictionary<string, FactMetadata> Facts { get; set; } = new Dictionary<string, FactMetadata>();
        public Dictionary<string, string> ScopeParents { get; set; } = new Dictionary<string, string>();
        public HealthSnapshot? Health { get; set; }
        public Dictionary<string, object> DedupInvariants { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> PolicySnapshot { get; set; } = new Dictionary<string, object>();
        
        // v5 Snapshots
        public Dictionary<string, object> TrustSnapshot { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> ConsistencySnapshot { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> LifecycleSnapshot { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> SecuritySnapshot { get; set; } = new Dictionary<string, object>();
    }

    public class HealthSnapshot
    {
        public int TotalFacts { get; set; }
        public int ActiveFacts { get; set; }
        public int ArchivedFacts { get; set; }
        public int ActiveTokens { get; set; }
        public double CognitiveLoad { get; set; }
        public double UniqueTextRatioActive { get; set; }
        public double DuplicateRatioActive { get; set; }
        public double NoiseRatioActive { get; set; }
        public int AmbiguousActiveCount { get; set; }
        public int ConflictGroupCount { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public DateTime LastMaintenanceUtc { get; set; }

        // v6 Integrity Metrics
        public int PayloadMismatchCount { get; set; }
        public int IntegrityRepairCountLastLoad { get; set; }
        public int TokenDriftCount { get; set; }
    }

    public enum PayloadIntegrityState
    {
        Ok,
        RecoveredFromMetadata,
        RecoveredFromFile,
        Conflict
    }

    public enum PayloadSource
    {
        MetadataCanonical,
        FactFile,
        Healed
    }

    /// <summary>
    /// Metadata for a single fact, excluding the expression content.
    /// </summary>
    public class FactMetadata
    {
        public DateTime CreatedAt { get; set; }
        public double Potency { get; set; }
        public double Certainty { get; set; }
        public string Scope { get; set; } = "Global";
        public ReActType Type { get; set; }
        public double Reach { get; set; }
        public int Tokens { get; set; }
        public string ContentHash { get; set; } = string.Empty;
        public string SemanticKey { get; set; } = string.Empty;
        public string CanonicalText { get; set; } = string.Empty;
        public int OccurrenceCount { get; set; } = 1;
        public double QualityScore { get; set; } = 1.0;
        public long SizeBytes { get; set; }
        public MemoryContentType ContentType { get; set; }
        public FactKind Kind { get; set; }
        public MemoryRetentionPolicy Retention { get; set; }
        public int Priority { get; set; }
        public List<string>? Tags { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public List<string> DependencyIds { get; set; } = new List<string>();
        public bool IsInvalidated { get; set; }
        public string? InvalidationReason { get; set; }
        public bool IsAmbiguous { get; set; }
        public bool IsSuperseded { get; set; }
        public DateTime LastDecayUpdate { get; set; }
        public string? ConflictGroupId { get; set; }
        public IngestSourceType SourceType { get; set; }
        public string Source { get; set; } = "User";
        public string? IngestOperationId { get; set; }
        public string? RuleName { get; set; }
        public DateTime FirstSeenAtUtc { get; set; }
        public DateTime LastSeenAtUtc { get; set; }
        public string? PreviousVersionId { get; set; }
        public string? NextVersionId { get; set; }
        public bool IsArchived { get; set; }

        // v5 Fields
        public DateTime ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }
        public List<string> SupersedesFactIds { get; set; } = new List<string>();
        public Dictionary<string, string> ContextTags { get; set; } = new Dictionary<string, string>();
        public StalenessPolicy Staleness { get; set; }
        public double TrustScore { get; set; }
        public double SourceReliability { get; set; }
        public ValidationState Validation { get; set; }
        public DateTime? LastVerificationUtc { get; set; }
        public bool IsQuarantined { get; set; }
        public int SelfCitationCount { get; set; }

        // v6 Fields
        public PayloadIntegrityState IntegrityState { get; set; }
        public PayloadSource PayloadSource { get; set; }
        public DateTime? LastIntegrityRepairUtc { get; set; }
        public long PayloadRevision { get; set; }
    }
}
