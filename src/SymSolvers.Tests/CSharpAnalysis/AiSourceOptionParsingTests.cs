using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SymSolvers;
using SymSolvers.CSharpAnalysis;

namespace SymSolvers.Tests.CSharpAnalysis
{
    [TestClass]
    public class AiSourceOptionParsingTests
    {
        [TestMethod]
        [Timeout(10000)]
        public void FromAdditionalData_ReadsExternalSourceKey()
        {
            var options = CSharpMathBugAnalyzerOptions.FromAdditionalData(
                new Dictionary<string, object>
                {
                    [SolverOptionKeys.CSharpSecurityPrioritizeExternalSources] = true
                });

            Assert.IsTrue(options.PrioritizeUserSources, "External-source key should drive PrioritizeUserSources for compatibility.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void FromAdditionalData_ReadsLegacyUserSourceKey()
        {
            var options = CSharpMathBugAnalyzerOptions.FromAdditionalData(
                new Dictionary<string, object>
                {
                    [SolverOptionKeys.CSharpSecurityPrioritizeUserSources] = true
                });

            Assert.IsTrue(options.PrioritizeUserSources, "Legacy user-source key must remain supported.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void FromAdditionalData_ReadsRepoScanInfoPath()
        {
            const string expectedPath = "RepoScanInfo.txt";
            var options = CSharpMathBugAnalyzerOptions.FromAdditionalData(
                new Dictionary<string, object>
                {
                    [SolverOptionKeys.CSharpSecurityRepoScanInfoPath] = expectedPath
                });

            Assert.AreEqual(expectedPath, options.RepoScanInfoPath, "RepoScanInfo path should be read from additional data.");
        }
    }
}
