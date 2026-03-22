//Copyright Warren Harding 2025.
using System.Collections.Generic;

namespace Sym.Core.EGraph
{
    /// <summary>
    /// Represents an Equivalence Class (E-Class) in the E-Graph.
    /// An E-Class contains a set of ENodes that are equivalent.
    /// </summary>
    public class EClass
    {
        /// <summary>
        /// The unique identifier for this E-Class.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// The set of ENodes contained in this class.
        /// </summary>
        public HashSet<ENode> Nodes { get; }

        /// <summary>
        /// The parent Id for the Union-Find structure.
        /// If Parent == Id, this class is the canonical representative.
        /// </summary>
        public int Parent { get; set; }

        /// <summary>
        /// Optional data attached to the class (e.g., for constant folding or analysis).
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// Metadata attributes for this class (e.g., Rel, Cert, Tokens).
        /// </summary>
        public Dictionary<string, double> Metadata { get; } = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The generation version of this class. Incremented on modification.
        /// </summary>
        public int Generation { get; set; }

        public EClass(int id)
        {
            Id = id;
            Parent = id;
            Nodes = new HashSet<ENode>();
            Generation = 0;
        }

        /// <summary>
        /// Adds a node to this class.
        /// </summary>
        public void AddNode(ENode node)
        {
            Nodes.Add(node);
        }
    }
}
