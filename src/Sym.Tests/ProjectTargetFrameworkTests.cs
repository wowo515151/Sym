using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SymTest
{
    [TestClass]
    public class ProjectTargetFrameworkTests
    {
        private static string FindSrcRoot()
        {
            var baseDir = AppContext.BaseDirectory;

            var candidates = new[]
            {
                baseDir,
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", ".."))
            };

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(Path.Combine(candidate, "Sym")) && Directory.Exists(Path.Combine(candidate, "Sym.Tests")))
                {
                    return candidate;
                }

                if (Directory.Exists(Path.Combine(candidate, "src")) && Directory.Exists(Path.Combine(candidate, "src", "Sym")))
                {
                    return Path.Combine(candidate, "src");
                }
            }

            var di = new DirectoryInfo(baseDir);
            while (di != null)
            {
                if (Directory.Exists(Path.Combine(di.FullName, "Sym")) && Directory.Exists(Path.Combine(di.FullName, "Sym.Tests")))
                {
                    return di.FullName;
                }

                if (Directory.Exists(Path.Combine(di.FullName, "src")) && Directory.Exists(Path.Combine(di.FullName, "src", "Sym")))
                {
                    return Path.Combine(di.FullName, "src");
                }

                di = di.Parent;
            }

            throw new InvalidOperationException("Repository root not found from test base directory.");
        }

        [TestMethod]
        [Timeout(10000)]
        public void AllProjects_TargetNet10()
        {
            var srcRoot = FindSrcRoot();
            var csprojFiles = Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories).ToList();
            Assert.IsTrue(csprojFiles.Count > 0, $"No csproj files found under src. Probed: {srcRoot}");

            var notNet10 = csprojFiles.Where(p =>
            {
                try
                {
                    var doc = XDocument.Load(p);
                    var tf = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "TargetFramework" || e.Name.LocalName == "TargetFrameworks");
                    return tf == null || !tf.Value.Contains("net10", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return true;
                }
            }).ToList();

            if (notNet10.Any())
            {
                Assert.Fail("Projects not targeting net10: " + string.Join(", ", notNet10));
            }
        }
    }
}
