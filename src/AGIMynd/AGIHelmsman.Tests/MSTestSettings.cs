using Microsoft.VisualStudio.TestTools.UnitTesting;

// Disable parallel execution for UI tests (FlaUI) to prevent conflicts.
[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]
