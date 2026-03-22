using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AGIMynd;

namespace AGIMynd.Tests
{
    [TestClass]
    public class BufferFileLoggerTests
    {
        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task SingleEventProducesSingleLog()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                string memoryRoot = tempDir;
                string? recorded = null;
                var logger = new BufferFileLogger(memoryRoot, s => recorded = s);

                var filePath = Path.Combine(memoryRoot, "ToTools", "test.txt");
                var dir = Path.GetDirectoryName(filePath);
                Assert.IsNotNull(dir);
                Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, "hello");

                logger.NotifyFileChanged(filePath);
                // allow debounce + read
                await Task.Delay(400);

                Assert.IsNotNull(recorded);
                StringAssert.Contains(recorded, "Buffer file (ToTools): test.txt");
                StringAssert.Contains(recorded, "hello");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task RapidMultipleEventsProduceSingleLog()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                string memoryRoot = tempDir;
                int count = 0;
                var logger = new BufferFileLogger(memoryRoot, s => count++);

                var filePath = Path.Combine(memoryRoot, "FromTools", "test2.txt");
                var dir = Path.GetDirectoryName(filePath);
                Assert.IsNotNull(dir);
                Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, "world");

                // fire multiple notifications quickly
                logger.NotifyFileChanged(filePath);
                logger.NotifyFileChanged(filePath);
                logger.NotifyFileChanged(filePath);

                await Task.Delay(600);

                Assert.AreEqual(1, count, "Expected only one log call for rapid events");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [TestMethod]
        [Timeout(10000, CooperativeCancellation = true)]
        public async Task TmpFilesAreIgnored()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                string memoryRoot = tempDir;
                int count = 0;
                var logger = new BufferFileLogger(memoryRoot, s => count++);

                var filePath = Path.Combine(memoryRoot, "ToTools", "tempfile.txt.tmp");
                var dir = Path.GetDirectoryName(filePath);
                Assert.IsNotNull(dir);
                Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, "tmp");

                logger.NotifyFileChanged(filePath);
                await Task.Delay(400);

                Assert.AreEqual(0, count);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
