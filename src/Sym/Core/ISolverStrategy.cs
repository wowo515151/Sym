// Copyright Warren Harding 2026
namespace Sym.Core;

/// <summary>
/// Defines the contract for all solver approaches within the Sym library.
/// </summary>
public interface ISolverStrategy
{
    SolveResult Solve(IExpression? problem, SolveContext context);
}

/// <summary>
/// Optional naming contract for solver strategies.
/// Prefer this over GetType().Name for user-facing output.
/// </summary>
public interface INamedSolverStrategy
{
    string Name { get; }
}
