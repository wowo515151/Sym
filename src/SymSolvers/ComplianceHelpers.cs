using System;

namespace SymSolvers.Internal;

internal static class ComplianceHelpers
{
    public const string CodingGuidelinesReference = "SemanticMemory\\CodingGuidelines.txt";

    public static bool IsResearchHelpPresent(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath)) return false;
        try
        {
            var path = System.IO.Path.Combine(basePath, "SemanticMemory", "ResearchHelp.txt");
            return System.IO.File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
