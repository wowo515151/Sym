// Copyright Warren Harding 2026
using System;
using System.Security.Cryptography;
using System.Text;
using Sym.Atoms;

namespace HAMM
{
    public static class MemoryContentEncoding
    {
        public const string HashPrefix = "ContentHash:";
        public const int DefaultMaxInlineChars = 2048;

        public static Symbol EncodeContentSymbol(string content, int maxInlineChars = DefaultMaxInlineChars)
        {
            content ??= string.Empty;
            if (content.Length <= maxInlineChars) return new Symbol(content);
            var hash = ComputeHash(content);
            return new Symbol(HashPrefix + hash);
        }

        public static bool IsHashedSymbol(Symbol symbol)
        {
            if (symbol == null) return false;
            return symbol.Name.StartsWith(HashPrefix, StringComparison.Ordinal);
        }

        public static string ComputeHash(string content)
        {
            content ??= string.Empty;
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(content);
            var hashBytes = sha.ComputeHash(bytes);
            return Convert.ToHexString(hashBytes);
        }
    }
}
