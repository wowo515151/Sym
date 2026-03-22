//Copyright Warren Harding 2025.
using Sym.Core;
using System.Collections.Immutable;
using System;

namespace Sym.Atoms
{
    public sealed class Symbol : Atom
    {
        public override string Head => $"Sym:{Name}";
        public string Name { get; init; }
        private readonly Shape _shape;
        public override Shape Shape { get { return _shape; } }

        // Parameterless constructor added to support reflective instantiation in unit tests.
        public Symbol() : this(string.Empty) { }

        public Symbol(string name) : this(name, Sym.Core.Shape.Scalar) { }

        public Symbol(string name, Shape shape)
        {
            Name = name;
            _shape = shape;
        }

        public override IExpression Canonicalize()
        {
            if (Name.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return Name.Equals("true", StringComparison.Ordinal) ? this : new Symbol("true", Shape);
            }
            if (Name.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return Name.Equals("false", StringComparison.Ordinal) ? this : new Symbol("false", Shape);
            }
            return this;
        }

        public override string ToDisplayString()
        {
            if (Shape.IsScalar)
            {
                return Name;
            }
            return $"{Name}{Shape.ToDisplayString()}";
        }

        public override bool InternalEquals(IExpression other)
        {
            if (other is not Symbol otherSymbol)
            {
                return false;
            }
            return Name.Equals(otherSymbol.Name, StringComparison.Ordinal) && Shape.Equals(otherSymbol.Shape);
        }

        public override int InternalGetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(Name);
            hash.Add(Shape);
            return hash.ToHashCode();
        }
    }
}
