using System;
using System.Collections.Generic;
using System.IO;

namespace AGIMynd
{
    internal class DefaultFileSystem : IFileSystem
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public IEnumerable<string> GetFiles(string path, string searchPattern, SearchOption searchOption) => Directory.GetFiles(path, searchPattern, searchOption);

        public bool FileExists(string path) => File.Exists(path);

        public DateTime GetCreationTimeUtc(string path) => new FileInfo(path).CreationTimeUtc;
    }
}
