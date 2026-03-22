# COBRA Phase 1 Optimization Audit - Next Safe Targets

**Date:** 2026-03-15
**Focus:** Upstream Pruning in Candidate Scoring Pipeline
**Context:** After compatibility-class pruning; baseline 6899ms -> 5773ms (MatchCandidateBuild)

---

## EXECUTIVE SUMMARY

Three clear avoidable upstream work patterns identified:

1. **HIGHEST IMPACT & SAFEST:** Double-encoding of rule properties in BuildScoredRuleMap()
2. **HIGH IMPACT:** Redundant IsOneLevelDirectPattern() evaluation on all rules
3. **MODERATE IMPACT:** Early filtering of zero-candidate rules before class pruning

**Recommendation:** Option 1 (Rule property cache) - eliminates redundant pattern analysis with zero semantic impact.

---

## FINDING 1: Double-Encoding of Rule Properties in BuildScoredRuleMap

### Location
File: src/SymCobra/Regions/CobraNodeMatchCandidatePlanner.cs
Method: BuildScoredRuleMap() (lines 232-266)

### The Problem

Rules are analyzed TWICE to extract arity and directness flags:
- First time: During batch encoding (lines 91-98 in CUDA path, lines 209-211 in CPU fallback)
- Second time: In BuildScoredRuleMap (lines 249-250) for scoring phase

For N rules with M average positive candidates:
- Arities calculated: 2 times
- IsOneLevelDirectPattern() called: 2 times
- Pattern.ToDisplayString() called: 1 time (only in final sort, acceptable)

Total waste: 2xN pattern traversals in scoring phase alone.

### Evidence in Code

Lines 91-98 (CobraNodeMatchCandidatePlanner, CUDA batch build):
- batchRuleArities.Add(GetRuleArity(rule))
- batchDirectWildcardFlags.Add(CobraNodeMatchEncoding.IsOneLevelDirectPattern(...))

Lines 249-250 (BuildScoredRuleMap, for rules with positive candidates):
- arities.Add(rules[ruleIndex].Pattern is Operation op ? op.Arguments.Count : 0)
- directFlags.Add(CobraNodeMatchEncoding.IsOneLevelDirectPattern(...) ? 1 : 0)

### Safe Optimization

Introduce RuleEncodingCache struct:

struct RuleEncodingCache {
    public int HeadCode;
    public int Arity;
    public bool IsDirectPattern;
}

Cache built ONCE during batch encoding, reused in BuildScoredRuleMap.

### Why This Is Safe

- Pure caching transformation - identical logic, computed once instead of 2x
- No change to output or rule ordering
- Reuses existing Property extractions
- Public contracts unchanged
- Fallback path also benefits

### Expected Impact

- MatchCandidateBuild: -50ms to -120ms (1-2% overall)
- Based on current 5773ms MatchCandidateBuild timing

---

## FINDING 2: Redundant IsOneLevelDirectPattern() in Two Planners

### Location
- CobraDirectMatchPlanner.cs line 72: var directRules = classRules.Where(CanUseDirectFlatOperationMatch).ToList()
- CobraNodeMatchCandidatePlanner.cs lines 96, 250: Same check repeated

### The Problem

Same rule pattern checked up to 3 times:
1. DirectMatchPlanner.Build() filters rules (line 72)
2. Batch encoding in NodeMatchCandidatePlanner (line 96)
3. BuildScoredRuleMap scoring (line 250)

### Safe Optimization

Cache directness flag once at solver iteration start, thread through both planners.

### Why This Is Safe

- Pure reuse of computed value
- Same rule always has same directness
- Heuristic is deterministic

### Expected Impact

- Incremental to Finding 1: -20ms to -40ms additional
- Combined impact: -70ms to -160ms on MatchCandidateBuild

---

## FINDING 3: Pre-Filtering Before NodeMatchCandidatePlanner

### Location
File: src/SymSolvers/CobraSolverStrategy.cs lines 114-127

### The Problem

Current order:
1. Build rule compatibility plan (head/arity checks)
2. Separate into direct vs compatibility rules
3. Filter classes with rules
4. Build node candidates (expensive CUDA scoring)
5. Filter rules with ZERO candidates (discovered post-CUDA)
6. Final class filter

Issue: Step 4 processes rules that Step 5 will discard.

### Safe Optimization

Add heuristic pre-filter between steps 2-3:
- Check if rule head bucket overlaps with class head buckets
- Only rules with possible matches proceed to expensive CUDA

Heuristic is conservative: never removes rules that could match.

### Why This Is Safe

- Heuristic is necessary condition for any match
- Conservative: worst case is all rules pass
- Still does exact matching in expensive phase
- Fallback-compatible

### Expected Impact

- MatchCandidateBuild: -50ms to -150ms (depends on rule/class set)
- Typical: 3-5% improvement
- Best case: 10-20% (when many rules have zero-matching classes)

---

## IMPLEMENTATION RANKING

### OPTION 1: Rule Property Cache [IMMEDIATE PRIORITY]

Safety:       5/5 (Pure caching)
Complexity:   2/5 (One struct + dict)
Impact:       4/5 (-50-120ms)
Risk:         Minimal (no semantic change)

Files: CobraNodeMatchCandidatePlanner.cs only

Test: Verify identical EligibleNodesByClass across old/new paths

### OPTION 2: Directness Cache [QUICK FOLLOW-UP]

Safety:       4/5 (Requires cache coordination)
Complexity:   2/5 (Thin wrapper dict)
Impact:       3/5 (-20-40ms additional)

Combine with Option 1

### OPTION 3: Early Heuristic Filtering [DEFER]

Safety:       4/5 (Conservative but adds branch)
Complexity:   3/5 (New filter method)
Impact:       4/5 (-50-150ms)

Defer until Options 1-2 benchmarked.
Reason: Adds logic; verify simpler wins first.

---

## REGRESSION TEST (FOR OPTION 1)

[TestMethod]
public void CobraNodeMatchCandidatePlanner_WithCache_ProducesIdenticalResults()
{
    // Build without cache (old path)
    var uncachedPlan = BuildWithoutCache(graph, classIds, rules);
    
    // Build with cache (new path)  
    var cachedPlan = BuildWithCache(graph, classIds, rules);
    
    // Verify identical outputs
    Assert.AreEqual(
        uncachedPlan.EligibleNodesByClass.Count,
        cachedPlan.EligibleNodesByClass.Count);
    
    foreach (var (classId, eligibleRules) in uncachedPlan.EligibleNodesByClass)
    {
        CollectionAssert.AreEqual(
            eligibleRules.Keys.OrderBy(r => r.ToString()).ToArray(),
            cachedPlan.EligibleNodesByClass[classId].Keys
                .OrderBy(r => r.ToString()).ToArray());
    }
}

---

## SUMMARY

**Next Safe Target:** Rule Property Cache (Option 1)

- **Where:** BuildScoredRuleMap() redundant pattern analysis
- **What:** Cache rule arity + directness flags computed during batching
- **Why Safe:** Pure caching, no semantic change, zero regression risk
- **Impact:** -50-120ms on MatchCandidateBuild (1-2% overall)
- **Lines:** ~30 lines, one new struct, minimal coupling
- **Benchmark Target:** 6800ms baseline (after 1-2% gain from Option 1)

Expected timeline: 1-2 hours implementation + testing