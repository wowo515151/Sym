namespace Sym.Core;

public static class SolverStrategyExtensions
{
    public static string GetStableName(this ISolverStrategy strategy)
    {
        if (strategy is null) return "<null>";
        return (strategy as INamedSolverStrategy)?.Name ?? strategy.GetType().Name;
    }
}
