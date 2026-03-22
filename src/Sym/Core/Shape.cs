// Copyright Warren Harding 2026
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Sym.Core
{
    public sealed record Shape : IEquatable<Shape>
    {
        public static readonly Shape Scalar = new Shape(ImmutableArray<int>.Empty, true, false);
        public static readonly Shape Error = new Shape(ImmutableArray<int>.Empty, false, false);
        public static readonly Shape Wildcard = new Shape(ImmutableArray<int>.Empty, true, true); // Represents a wildcard shape

        public ImmutableArray<int> Dimensions { get; init; }
        public bool IsValid { get; init; }
        public bool IsWildcardShape { get; init; }

        // Parameterless constructor added to support reflective instantiation in unit tests.
        public Shape() : this(ImmutableArray<int>.Empty, true, false) { }

        public Shape(ImmutableArray<int> dimensions, bool isValid = true) : this(dimensions, isValid, false) { }

        private Shape(ImmutableArray<int> dimensions, bool isValid, bool isWildcardShape)
        {
            Dimensions = dimensions;
            IsValid = isValid;
            IsWildcardShape = isWildcardShape;
        }

        public bool IsScalar => Dimensions.IsEmpty && IsValid && !IsWildcardShape;
        public bool IsVector => Dimensions.Length == 1 && IsValid && !IsWildcardShape;
        public bool IsMatrix => Dimensions.Length == 2 && IsValid && !IsWildcardShape;
        public bool IsTensor => Dimensions.Length > 2 && IsValid && !IsWildcardShape;

        /// <summary>
        /// Checks if two shapes are compatible for element-wise operations (e.g., addition, element-wise multiplication) 
        /// using NumPy-style broadcasting rules.
        /// </summary>
        public bool AreDimensionsCompatibleForElementWise(Shape other)
        {
            if (!this.IsValid || this.IsWildcardShape || !other.IsValid || other.IsWildcardShape)
            {
                return false;
            }
            
            int rank1 = this.Dimensions.Length;
            int rank2 = other.Dimensions.Length;
            int maxRank = Math.Max(rank1, rank2);

            for (int i = 0; i < maxRank; i++)
            {
                int dim1 = i < rank1 ? this.Dimensions[rank1 - 1 - i] : 1;
                int dim2 = i < rank2 ? other.Dimensions[rank2 - 1 - i] : 1;

                if (dim1 != dim2 && dim1 != 1 && dim2 != 1)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Attempts to combine two shapes for an element-wise operation using broadcasting rules.
        /// Returns the resulting shape if compatible, otherwise returns <see cref="Shape.Error"/>.
        /// </summary>
        public Shape CombineForElementWise(Shape other)
        {
            if (!this.IsValid || this.IsWildcardShape || !other.IsValid || other.IsWildcardShape)
            {
                return Shape.Error;
            }

            int rank1 = this.Dimensions.Length;
            int rank2 = other.Dimensions.Length;
            int maxRank = Math.Max(rank1, rank2);
            var resultDims = new int[maxRank];

            for (int i = 0; i < maxRank; i++)
            {
                int dim1 = i < rank1 ? this.Dimensions[rank1 - 1 - i] : 1;
                int dim2 = i < rank2 ? other.Dimensions[rank2 - 1 - i] : 1;

                if (dim1 == dim2)
                {
                    resultDims[maxRank - 1 - i] = dim1;
                }
                else if (dim1 == 1)
                {
                    resultDims[maxRank - 1 - i] = dim2;
                }
                else if (dim2 == 1)
                {
                    resultDims[maxRank - 1 - i] = dim1;
                }
                else
                {
                    return Shape.Error;
                }
            }

            return new Shape(resultDims.ToImmutableArray());
        }

        public string ToDisplayString()
        {
            if (IsWildcardShape)
            {
                return "WildcardShape";
            }
            if (!IsValid)
            {
                return "ErrorShape";
            }
            if (IsScalar)
            {
                return "()";
            }
            return $"({(string.Join(", ", Dimensions.Select(d => $"{d}")))})";
        }

        // Explicitly implement IEquatable<Shape> and override Equals/GetHashCode
        public bool Equals(Shape? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            if (other is null)
            {
                return false;
            }
            // Include IsWildcardShape in comparison
            return IsValid.Equals(other.IsValid) && IsWildcardShape.Equals(other.IsWildcardShape) && Dimensions.SequenceEqual(other.Dimensions);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(IsValid);
            hash.Add(IsWildcardShape); // Include IsWildcardShape in hash calculation
            foreach (int dim in Dimensions)
            {
                hash.Add(dim);
            }
            return hash.ToHashCode();
        }
    }
}
