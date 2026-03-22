// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;

namespace AGIMynd
{
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        IEnumerable<string> GetFiles(string path, string searchPattern, System.IO.SearchOption searchOption);
        bool FileExists(string path);
        DateTime GetCreationTimeUtc(string path);
    }
}
