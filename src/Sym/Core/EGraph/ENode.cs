// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sym.Atoms;
using Sym.Operations;

namespace Sym.Core.EGraph
{
    /// <summary>
    /// Represents a node in the E-Graph.
    /// An ENode is defined by its operator (Head) and the list of EClass IDs it points to as children.
    /// </summary>
    public readonly struct ENode : IEquatable<ENode>
    {
        public string Head { get; }
        public ImmutableList<int> Children { get; }

        public ENode(string head, ImmutableList<int> children)
        {
            Head = head;
            Children = children;
        }

        public static string GetHead(IExpression expr)
        {
            string head = expr.Head;
            return head;
        }

        public static IExpression CreateExpression(string head, ImmutableList<IExpression> children)
        {
            return ExpressionFactory.Create(head, children);
        }

        public bool Equals(ENode other)
        {
            if (Head != other.Head) return false;
            if (Children.Count != other.Children.Count) return false;

            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i] != other.Children[i]) return false;
            }
            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is ENode other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(Head);
            foreach (var child in Children)
            {
                hashCode.Add(child);
            }
            return hashCode.ToHashCode();
        }

        public override string ToString()
        {
            if (Children.IsEmpty) return Head;
            return $"{Head}({string.Join(", ", Children)})";
        }
    }
}
