// Copyright Warren Harding 2026
// Base solver strategy providing a consistent "shape" for strategies.
namespace Sym.Core;

/// <summary>
/// Provides a common base for solver strategies exposing a Name.
/// </summary>
public abstract class BaseSolverStrategy : ISolverStrategy, INamedSolverStrategy
{
    public virtual string Name => GetType().Name;

    public abstract SolveResult Solve(IExpression? problem, SolveContext context);
}
