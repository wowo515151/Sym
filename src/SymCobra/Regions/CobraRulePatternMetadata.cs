using System.Collections.Generic;
using Sym.Atoms;
using Sym.Core;
using Sym.Core.EGraph;
using Sym.Operations;

namespace SymCobra.Regions;

internal readonly record struct CobraRulePatternMetadata(
    int HeadCode,
    int HeadBucket,
    int Arity,
    bool IsWildcard,
    bool IsOneLevelDirectPattern,
    IReadOnlyList<CobraNodeMatchEncoding.FlatArgumentInfo> FlatArgumentInfos,
    int[] NestedAtomMasks,
    int[] NestedConstraintMasks,
    int[] NestedTopLevelReferenceMasks)
{
    public int WildcardFlag => IsWildcard ? 1 : 0;

    public int DirectPatternFlag => IsOneLevelDirectPattern ? 1 : 0;
}

internal static class CobraRulePatternMetadataCache
{
    public static CobraRulePatternMetadata GetOrAdd(IDictionary<Rule, CobraRulePatternMetadata> cache, Rule rule)
    {
        if (!cache.TryGetValue(rule, out var metadata))
        {
            metadata = new CobraRulePatternMetadata(
                rule.Pattern is Wild ? 0 : CobraNodeMatchEncoding.EncodeHeadCode(ENode.GetHead(rule.Pattern)),
                rule.Pattern is Wild ? 0 : CobraNodeMatchEncoding.EncodeHeadBucket(ENode.GetHead(rule.Pattern)),
                rule.Pattern is Operation op ? op.Arguments.Count : 0,
                rule.Pattern is Wild,
                CobraNodeMatchEncoding.IsOneLevelDirectPattern(rule.Pattern),
                CobraNodeMatchEncoding.BuildFlatArgumentInfos(rule.Pattern),
                CobraNodeMatchEncoding.BuildNestedAtomBucketMasks(rule.Pattern),
                CobraNodeMatchEncoding.BuildNestedConstraintMasks(rule.Pattern),
                CobraNodeMatchEncoding.BuildNestedTopLevelReferenceMasks(rule.Pattern));
            cache[rule] = metadata;
        }

        return metadata;
    }
}
