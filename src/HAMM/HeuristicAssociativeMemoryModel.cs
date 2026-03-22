// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using Sym.Core;
using Sym.Atoms;
using System.Linq;

namespace HAMM
{
    public enum RetrievalProfile
    {
        DefaultTask,
        Debug,
        SafetyCritical,
        Research
    }

    /// <summary>
    /// Heuristic Associative Memory Model (HAMM)
    /// </summary>
    public class HeuristicAssociativeMemoryModel : IDisposable
    {
        public MemoryStore Store { get; } = new MemoryStore();

        /// <summary>
        /// Gets or sets the current active scope for context retrieval (Cycle 34).
        /// </summary>
        public string CurrentScope { get; set; } = "Global";

        public HeuristicAssociativeMemoryModel()
        {
            Store.CompactionRequired += () => CompactionRequired?.Invoke();
        }

        /// <summary>
        /// Remembers a fact.
        /// </summary>
        public void Remember(IExpression fact, string scope = "Global", double certainty = 1.0)
        {
            Store.AddFact(fact, scope, certainty, ReActType.Generic, null);
        }

        /// <summary>
        /// Records a thought (Cycle 28).
        /// </summary>
        public void Think(IExpression thought, string scope = "Global", double certainty = 1.0)
        {
            Store.AddFact(thought, scope, certainty, ReActType.Thought, null);
        }

        /// <summary>
        /// Records an action (Cycle 28).
        /// </summary>
        public void Act(IExpression action, string scope = "Global", double certainty = 1.0)
        {
            Store.AddFact(action, scope, certainty, ReActType.Action, null);
        }

        /// <summary>
        /// Records an observation (Cycle 28).
        /// </summary>
        public void Observe(IExpression observation, string scope = "Global", double certainty = 1.0)
        {
            Store.AddFact(observation, scope, certainty, ReActType.Observation, null);
        }

        /// <summary>
        /// Recalls facts related to a query using the v5 Retrieval Pipeline.
        /// </summary>
        public IEnumerable<IExpression> Recall(IExpression query, string? scope = null, int tokenLimit = 1000, double diversityWeight = 0.5, ReActType? typeFilter = null, RetrievalProfile profile = RetrievalProfile.DefaultTask)
        {
            foreach (var fact in RecallFacts(query, scope, tokenLimit, diversityWeight, typeFilter, profile))
            {
                yield return fact.Expression;
            }
        }

        /// <summary>
        /// Recalls facts (including metadata such as token counts) using the retrieval pipeline.
        /// </summary>
        public IEnumerable<MemoryFact> RecallFacts(IExpression query, string? scope = null, int tokenLimit = 1000, double diversityWeight = 0.5, ReActType? typeFilter = null, RetrievalProfile profile = RetrievalProfile.DefaultTask)
        {
            string targetScope = scope ?? CurrentScope;

            // 1. Candidate Generation
            var options = new QueryOptions
            {
                Scope = targetScope,
                IncludeArchive = profile == RetrievalProfile.Research,
                MinQualityScore = profile == RetrievalProfile.SafetyCritical ? 0.7 : 0.3
            };
            var candidates = new List<MemoryFact>(Store.QueryV2(query, options));

            if (typeFilter.HasValue)
            {
                candidates = candidates.FindAll(f => f.Type == typeFilter.Value);
            }

            // 2. Candidate Expansion (Adjacency expansion)
            if (profile != RetrievalProfile.SafetyCritical)
            {
                var expansion = new List<MemoryFact>();
                foreach (var c in candidates.Take(5))
                {
                    if (c.Expression is Symbol s)
                    {
                        expansion.AddRange(Store.GetRelatedFacts(s.Name).Take(3));
                    }
                }
                foreach (var e in expansion)
                {
                    if (!candidates.Contains(e)) candidates.Add(e);
                }
            }

            // Normalize ordering to ensure stable tie-breaking across restarts.
            candidates = candidates.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

            // 3. Rerank by Utility
            var selected = new List<MemoryFact>();
            int currentTokens = 0;
            var now = DateTime.UtcNow;

            while (candidates.Count > 0)
            {
                MemoryFact? bestCandidate = null;
                double bestScore = double.MinValue;

                foreach (var candidate in candidates)
                {
                    // v5 Scoring: relevance, certainty, trust, potency, novelty, recency validity
                    double rel = Store.CalculateSimilarity(query, candidate.Expression);

                    // Staleness Check
                    if (candidate.ValidToUtc.HasValue && candidate.ValidToUtc.Value < now && candidate.Staleness == StalenessPolicy.HardExpire)
                        continue;

                    double trustWeight = candidate.TrustScore;
                    if (candidate.Validation == ValidationState.Verified) trustWeight *= 1.2;
                    if (candidate.Validation == ValidationState.Refuted) trustWeight *= 0.1;

                    double recencyWeight = 1.0;
                    if (candidate.Staleness == StalenessPolicy.SoftExpire && candidate.ValidToUtc.HasValue && candidate.ValidToUtc.Value < now)
                    {
                        recencyWeight = 0.5;
                    }

                    double priorityWeight = 1.0 + (Store.PriorityWeight * candidate.Priority);
                    if (priorityWeight < 0.1) priorityWeight = 0.1;

                    double typeWeight = candidate.ContentType == MemoryContentType.Artifact ? Store.ArtifactScoreMultiplier : 1.0;
                    if (candidate.ContentType == MemoryContentType.Summary) typeWeight *= 1.2;

                    // Anti-echo penalty
                    double echoPenalty = 1.0 / (1.0 + candidate.SelfCitationCount);

                    double score = (rel * candidate.Certainty * candidate.Potency * trustWeight * recencyWeight * typeWeight * priorityWeight * echoPenalty) / Math.Max(1, candidate.Tokens);

                    // Diversity penalty
                    double maxSimilarity = 0;
                    foreach (var s in selected)
                    {
                        double sim = Store.CalculateSimilarity(candidate.Expression, s.Expression);
                        if (sim > maxSimilarity) maxSimilarity = sim;
                    }
                    score -= (diversityWeight * maxSimilarity);

                    bool better = score > bestScore;
                    if (!better && Math.Abs(score - bestScore) < 1e-12 && bestCandidate != null)
                    {
                        better = string.CompareOrdinal(candidate.Id, bestCandidate.Id) < 0;
                    }

                    if (better)
                    {
                        bestScore = score;
                        bestCandidate = candidate;
                    }
                }

                if (bestCandidate == null || bestScore <= 0) break;
                candidates.Remove(bestCandidate);

                if (currentTokens + bestCandidate.Tokens <= tokenLimit)
                {
                    selected.Add(bestCandidate);
                    currentTokens += bestCandidate.Tokens;

                    if (profile == RetrievalProfile.Debug)
                    {
                        // Emit decision trace (simplified as metadata for now)
                        bestCandidate.Metadata["LastRecallScore"] = bestScore;
                    }

                    bestCandidate.SelfCitationCount++;
                }
            }

            return selected;
        }
        
        /// <summary>
        /// Recalls all known facts that are semantically equivalent to the query according to the internal EGraph.
        /// </summary>
        public IEnumerable<IExpression> RecallEquivalent(IExpression query)
        {
            return Store.GetEquivalentExpressions(query);
        }

        /// <summary>
        /// Applies rewrite rules to the memory store to infer new relationships.
        /// </summary>
        public void Reason(IEnumerable<Rule> rules)
        {
            Store.Reason(rules);
        }

        /// <summary>
        /// Simplifies an expression using the accumulated knowledge in the EGraph.
        /// </summary>
        public IExpression Simplify(IExpression expr)
        {
            return Store.Simplify(expr);
        }

        /// <summary>
        /// Defines a parent-child relationship between scopes.
        /// </summary>
        public void SetParentScope(string childScope, string parentScope)
        {
            Store.ScopeParents[childScope] = parentScope;
        }

        /// <summary>
        /// Subscribes an agent to memory updates (Cycle 11).
        /// </summary>
        public void Subscribe(IMemorySubscriber subscriber)
        {
            Store.Subscribe(subscriber);
        }

        /// <summary>
        /// Recalls facts including those in the archive (Cycle 16).
        /// </summary>
        public IEnumerable<IExpression> RecallDeep(IExpression query, string? scope = null, int limit = 10)
        {
            string targetScope = scope ?? CurrentScope;
            var candidates = new List<MemoryFact>(Store.Query(query, targetScope, includeArchive: true));
            candidates.Sort((a, b) => (b.Potency * b.Certainty).CompareTo(a.Potency * a.Certainty));

            foreach (var fact in candidates.Take(limit))
            {
                yield return fact.Expression;
            }
        }

        /// <summary>
        /// Event triggered when the memory store exceeds its compaction limit.
        /// Useful for external agents to perform summarization (Cycle 15/29).
        /// </summary>
        public event Action? CompactionRequired;

        /// <summary>
        /// Performs maintenance tasks like archiving and decay.
        /// </summary>
        public void Maintenance()
        {
            Store.Maintenance();
        }

        /// <summary>
        /// Gets the current cognitive load (token density).
        /// </summary>
        public double CognitiveLoad => Store.CognitiveLoad;

        /// <summary>
        /// Combines multiple expressions into a semantic summary.
        /// </summary>
        public void Fold(IEnumerable<IExpression> expressions, string? scope = null)
        {
            string targetScope = scope ?? CurrentScope;
            var facts = Store.GetFacts(targetScope).Where(f => expressions.Any(e => e.InternalEquals(f.Expression)));
            Store.Fold(facts, targetScope);
        }
        
        /// <summary>
        /// v5 Action Alignment Gate: Checks if an action is safe given memory context.
        /// </summary>
        public bool ValidateActionContext(IExpression action, out string? blockReason)
        {
            blockReason = null;
            
            // 1. Check for contradictory goals/policies
            // We search in "Goals" and "Pinned" scopes for potential conflicts
            var constraints = Recall(action, scope: "Goals", tokenLimit: 1000, profile: RetrievalProfile.SafetyCritical).ToList();
            
            // Simplified check: if the action is explicitly refuted or conflicts with a pinned assertion
            foreach (var fact in Store.GetFacts("Goals"))
            {
                if (fact.Validation == ValidationState.Refuted && Store.CalculateSimilarity(action, fact.Expression) > 0.8)
                {
                    blockReason = $"ActionRefutedByGoal: {fact.CanonicalText}";
                    return false;
                }
            }

            return true;
        }

        public void Dispose()
        {
            Store.Dispose();
        }
    }
}
