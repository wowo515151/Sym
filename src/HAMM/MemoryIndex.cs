using System;
using System.Collections.Generic;
using Sym.Core;
using Sym.Atoms;
using Sym.Operations;
using System.Linq;

namespace HAMM
{
    /// <summary>
    /// Manages auxiliary indices for HAMM to support fast traversal and disjointness checks.
    /// </summary>
    public class MemoryIndex
    {
        /// <summary>
        /// Maximum length of a symbol name to be included in the auxiliary indices.
        /// Prevents large payloads (like raw file content) from poisoning the index performance.
        /// </summary>
        public const int MaxSymbolLength = 128;

        // Adjacency / Inverted Index: Symbol -> List of Facts containing it.
        private readonly Dictionary<string, HashSet<MemoryFact>> _subjectIndex = new Dictionary<string, HashSet<MemoryFact>>();

        // Inequality / Disjoint Sets: Stores pairs of canonical expressions that are explicitly distinct.
        // We use normalized string pairs to ensure stability across EGraph rebuilds.
        private readonly HashSet<(string, string)> _disjointPairs = new HashSet<(string, string)>();

        /// <summary>
        /// Indexes a fact for fast symbol-based retrieval.
        /// </summary>
        public void Add(MemoryFact fact)
        {
            var symbols = new HashSet<string>();
            Harvest(fact, symbols);

            foreach (var symbol in symbols)
            {
                if (!_subjectIndex.TryGetValue(symbol, out var list))
                {
                    list = new HashSet<MemoryFact>();
                    _subjectIndex[symbol] = list;
                }
                list.Add(fact);
            }
        }

        /// <summary>
        /// Removes a fact from the index.
        /// </summary>
        public void Remove(MemoryFact fact)
        {
            var symbols = new HashSet<string>();
            Harvest(fact, symbols);

            foreach (var symbol in symbols)
            {
                if (_subjectIndex.TryGetValue(symbol, out var list))
                {
                    list.Remove(fact);
                    if (list.Count == 0) _subjectIndex.Remove(symbol);
                }
            }
        }

        public void Clear()
        {
            _subjectIndex.Clear();
            _disjointPairs.Clear();
        }

        /// <summary>
        /// Harvests all valid indexable symbols from a fact, including its expression and tags.
        /// </summary>
        public void Harvest(MemoryFact fact, HashSet<string> symbols)
        {
            if (fact == null) return;
            
            // Include Tags explicitly (assumed to be small/reusable)
            foreach (var tag in fact.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    symbols.Add(tag);
                }
            }

            HarvestSymbols(fact.Expression, symbols);
        }

        /// <summary>
        /// Retrieves all facts related to a specific symbol (Adjacency).
        /// </summary>
        public IEnumerable<MemoryFact> GetRelated(string symbol)
        {
            if (_subjectIndex.TryGetValue(symbol, out var list))
            {
                return list;
            }
            return Enumerable.Empty<MemoryFact>();
        }

        /// <summary>
        /// Records that two expressions are distinct.
        /// </summary>
        public void AddDisjoint(string canonicalExpr1, string canonicalExpr2)
        {
            if (canonicalExpr1 == canonicalExpr2) return;
            
            var pair = string.CompareOrdinal(canonicalExpr1, canonicalExpr2) < 0 
                ? (canonicalExpr1, canonicalExpr2) 
                : (canonicalExpr2, canonicalExpr1);
            _disjointPairs.Add(pair);
        }

        /// <summary>
        /// Checks if two expressions (or their representatives) are known to be disjoint.
        /// </summary>
        public bool AreDisjoint(string canonicalExpr1, string canonicalExpr2)
        {
            if (canonicalExpr1 == canonicalExpr2) return false;
            
            var pair = string.CompareOrdinal(canonicalExpr1, canonicalExpr2) < 0 
                ? (canonicalExpr1, canonicalExpr2) 
                : (canonicalExpr2, canonicalExpr1);
            return _disjointPairs.Contains(pair);
        }

        public IEnumerable<(string, string)> GetDisjointPairs() => _disjointPairs;

        private void HarvestSymbols(IExpression expr, HashSet<string> symbols)
        {
            if (expr is Symbol s)
            {
                // Only index symbols that aren't giant blobs of data
                if (s.Name.Length <= MaxSymbolLength && !MemoryContentEncoding.IsHashedSymbol(s))
                {
                    symbols.Add(s.Name);
                }
            }
            else if (expr is Operation op)
            {
                foreach (var arg in op.Arguments) HarvestSymbols(arg, symbols);
            }
        }
    }
}
