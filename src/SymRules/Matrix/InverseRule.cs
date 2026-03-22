using System;

namespace SymRules.Matrix
{
    /// <summary>
    /// Provides a minimal, stable matrix inversion helper for small numeric 2x2 matrices.
    /// </summary>
    public static class InverseRule
    {
        public static object Invert(object matrix)
        {
            if (matrix is null) throw new ArgumentNullException(nameof(matrix));

            if (matrix is double[,] d) return Invert2x2(d);
            if (matrix is float[,] f) return Invert2x2(ToDouble(f));
            if (matrix is decimal[,] m) return Invert2x2(ToDouble(m));

            throw new ArgumentException("Matrix type not supported. Provide a 2x2 numeric array.", nameof(matrix));
        }

        private static double[,] Invert2x2(double[,] m)
        {
            if (m.GetLength(0) != 2 || m.GetLength(1) != 2)
            {
                throw new NotSupportedException("Only 2x2 matrices are supported by InverseRule.");
            }

            double a = m[0, 0];
            double b = m[0, 1];
            double c = m[1, 0];
            double d = m[1, 1];

            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12)
            {
                throw new InvalidOperationException("Matrix is singular and cannot be inverted.");
            }

            double invDet = 1.0 / det;

            return new double[,]
            {
                { d * invDet, -b * invDet },
                { -c * invDet, a * invDet }
            };
        }

        private static double[,] ToDouble(float[,] m)
        {
            var result = new double[m.GetLength(0), m.GetLength(1)];
            for (int i = 0; i < m.GetLength(0); i++)
            {
                for (int j = 0; j < m.GetLength(1); j++)
                {
                    result[i, j] = m[i, j];
                }
            }
            return result;
        }

        private static double[,] ToDouble(decimal[,] m)
        {
            var result = new double[m.GetLength(0), m.GetLength(1)];
            for (int i = 0; i < m.GetLength(0); i++)
            {
                for (int j = 0; j < m.GetLength(1); j++)
                {
                    result[i, j] = (double)m[i, j];
                }
            }
            return result;
        }
    }
}
