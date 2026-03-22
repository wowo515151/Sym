using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;

namespace AGIMynd.Tests
{
    [TestClass]
    public class BufferManagerTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcessPlansAsync_CopiesFileToExternal()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var plans = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var responses = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
            try
            {
                var mgr = new BufferManager(tempRoot, plans, responses);

                var srcDir = Path.Combine(tempRoot, "ToTools");
                Directory.CreateDirectory(srcDir);
                var src = Path.Combine(srcDir, "plan1.txt");
                File.WriteAllText(src, "plan-content");

                await mgr.ProcessPlansAsync();

                var dest = Path.Combine(plans, "plan1.txt");
                Assert.IsTrue(File.Exists(dest));
                Assert.AreEqual("plan-content", File.ReadAllText(dest));
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
                try { Directory.Delete(plans, true); } catch { }
                try { Directory.Delete(responses, true); } catch { }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcessPlansAsync_IgnoresTmpFiles()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var plans = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var responses = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
            try
            {
                var mgr = new BufferManager(tempRoot, plans, responses);

                var srcDir = Path.Combine(tempRoot, "ToTools");
                Directory.CreateDirectory(srcDir);
                var src = Path.Combine(srcDir, "editing.txt.tmp");
                File.WriteAllText(src, "partial");

                await mgr.ProcessPlansAsync();

                var dest = Path.Combine(plans, "editing.txt.tmp");
                Assert.IsFalse(File.Exists(dest));
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
                try { Directory.Delete(plans, true); } catch { }
                try { Directory.Delete(responses, true); } catch { }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcessResponsesAsync_CopiesExternalToMemory()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var plans = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var responses = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
            try
            {
                var mgr = new BufferManager(tempRoot, plans, responses);

                Directory.CreateDirectory(responses);
                var ext = Path.Combine(responses, "resp1.txt");
                File.WriteAllText(ext, "answer");

                await mgr.ProcessResponsesAsync();

                var mem = Path.Combine(tempRoot, "FromTools", "resp1.txt");
                Assert.IsTrue(File.Exists(mem));
                Assert.AreEqual("answer", File.ReadAllText(mem));
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
                try { Directory.Delete(plans, true); } catch { }
                try { Directory.Delete(responses, true); } catch { }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcessResponseFileAsync_IgnoresTmpAndCopies()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var plans = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var responses = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
            try
            {
                var mgr = new BufferManager(tempRoot, plans, responses);

                Directory.CreateDirectory(responses);
                var ext = Path.Combine(responses, "resp2.txt");
                File.WriteAllText(ext, "hello2");

                var extTmp = Path.Combine(responses, "resp2.txt.tmp");
                File.WriteAllText(extTmp, "tmpdata");

                await mgr.ProcessResponseFileAsync(extTmp);
                var memTmp = Path.Combine(tempRoot, "FromTools", "resp2.txt.tmp");
                Assert.IsFalse(File.Exists(memTmp));

                await mgr.ProcessResponseFileAsync(ext);
                var mem = Path.Combine(tempRoot, "FromTools", "resp2.txt");
                Assert.IsTrue(File.Exists(mem));
                Assert.AreEqual("hello2", File.ReadAllText(mem));
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
                try { Directory.Delete(plans, true); } catch { }
                try { Directory.Delete(responses, true); } catch { }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task ProcessPlansAsync_PrunesMemoryToLimit()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var plans = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var responses = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
            try
            {
                var mgr = new BufferManager(tempRoot, plans, responses);

                var srcDir = Path.Combine(tempRoot, "ToTools");
                Directory.CreateDirectory(srcDir);
                // create more than buffer size (10)
                for (int i = 0; i < 15; i++)
                {
                    File.WriteAllText(Path.Combine(srcDir, $"f{i}.txt"), i.ToString());
                    // space creation times
                    await Task.Delay(5);
                }

                await mgr.ProcessPlansAsync();

                var remaining = Directory.GetFiles(srcDir).Length;
                Assert.IsLessThanOrEqualTo(remaining, 10, $"Expected <=10 files after pruning but found {remaining}");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
                try { Directory.Delete(plans, true); } catch { }
                try { Directory.Delete(responses, true); } catch { }
            }
        }
    }
}
