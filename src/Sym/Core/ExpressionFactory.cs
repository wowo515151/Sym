//Copyright Warren Harding 2025.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Sym.Atoms;
using Sym.Operations;

namespace Sym.Core
{
    public static class ExpressionFactory
    {
        private static readonly Dictionary<string, Func<ImmutableList<IExpression>, IExpression>> _creators = new();

        static ExpressionFactory()
        {
            Register("Add", args => new Add(args));
            Register("Sub", args => new Subtract(args[0], args[1]));
            Register("Mul", args => new Multiply(args));
            Register("Divide", args => new Divide(args[0], args[1]));
            Register("Pow", args => new Power(args[0], args[1]));
            Register("Vector", args => new Vector(args));
            Register("Equality", args => new Equality(args[0], args[1]));
            Register("Derivative", args => new Derivative(args[0], args[1]));
            Register("Integral", args => new Integral(args[0], args[1]));
            Register("Piecewise", args => new Piecewise(args));
            Register("List", args => new ListOp(args));
            Register("Summary", args => new Summary(args));
            Register("Graph", args => new GraphOp(args));
            Register("Edge", args => new Edge(args));
            Register("DotProduct", args => new DotProduct(args[0], args[1]));
            Register("MatrixMultiply", args => new MatrixMultiply(args[0], args[1]));
            Register("Div", args => new Div(args[0], args[1]));
            Register("Grad", args => new Grad(args[0], args[1]));
            Register("Curl", args => new Curl(args[0], args[1]));
            Register("DefiniteIntegral", args => new DefiniteIntegral(args[0], (Symbol)args[1], args[2], args[3]));
            Register("Limit", args => new Limit(args[0], (Symbol)args[1], args[2]));
            Register("SeriesExpansion", args => new SeriesExpansion(args[0], (Symbol)args[1], args[2], args[3] is Number n ? (int)n.Value : 0));
            
            // Tensor Operations
            Register("MatMul", args => new MatMul(args));
            Register("TensorAdd", args => new TensorAdd(args));
            Register("TensorMul", args => new TensorMul(args));
            Register("Transpose", args => new Transpose(args));
            Register("Relu", args => new Relu(args));
            Register("Attr", args => new Attr(args));
            Register("Conv2D", args => new Conv2D(args));
            Register("FusedMatMulAdd", args => new FusedMatMulAdd(args));
            Register("FusedMatMulAddRelu", args => new FusedMatMulAddRelu(args));
            Register("FusedConv2DRelu", args => new FusedConv2DRelu(args));
            Register("Concat", args => new Concat(args));
            Register("Stack", args => new Stack(args));
            Register("Sum", args => new Sum(args));
            
            Register("Softmax", args => new Softmax(args));
            Register("RMSNorm", args => new RMSNorm(args));
            Register("Kronecker", args => new Kronecker(args));
            Register("TensorVec", args => new TensorVec(args));
            Register("vec", args => new TensorVec(args));
            Register("inverse", args => new Inverse(args));
        }

        public static void Register(string head, Func<ImmutableList<IExpression>, IExpression> creator)
        {
            _creators[head] = creator;
        }

        public static IExpression Create(string head, ImmutableList<IExpression> children)
        {
            if (head.StartsWith("Sym:"))
            {
                return new Symbol(head.Substring(4));
            }
            if (head.StartsWith("Wild:"))
            {
                return new Wild(head.Substring(5));
            }
            if (head.StartsWith("Num:"))
            {
                if (decimal.TryParse(head.Substring(4), CultureInfo.InvariantCulture, out decimal val))
                    return new Number(val);
                return new Number(0);
            }

            if (_creators.TryGetValue(head, out var creator))
            {
                return creator(children);
            }

            if (head.StartsWith("Func:"))
            {
                return new Function(head.Substring(5), children);
            }

            return new Function(head, children);
        }
    }
}
