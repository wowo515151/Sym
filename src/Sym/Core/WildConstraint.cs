// Copyright Warren Harding 2026
using System;

namespace Sym.Core
{
    /// <summary>
    /// Defines constraints for wildcard pattern matching.
    /// </summary>
    public enum WildConstraint
    {
        /// <summary>
        /// No specific constraint on the matched expression.
        /// </summary>
        None,
        /// <summary>
        /// The matched expression must be a scalar (Shape.IsScalar is true).
        /// </summary>
        Scalar,
        /// <summary>
        /// The matched expression must be a constant (currently, only Sym.Atoms.Number).
        /// This can be extended to include symbolic constants later.
        /// </summary>
        Constant,
        /// <summary>
        /// The matched expression must be a vector (Shape.IsVector is true).
        /// </summary>
        Vector,
        /// <summary>
        /// The matched expression must be a matrix (Shape.IsMatrix is true).
        /// </summary>
        Matrix,
        /// <summary>
        /// The matched expression must be a tensor (Shape.IsTensor is true).
        /// </summary>
        Tensor
    }
}
