// Copyright Warren Harding 2026
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class FactAttribute : TestMethodAttribute
    {
    }

    public static class Assert
    {
        public static void True(bool condition, string? userMessage = null)
            => Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(condition, userMessage);

        public static void False(bool condition, string? userMessage = null)
            => Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsFalse(condition, userMessage);

        public static void Equal<T>(T expected, T actual)
            => Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected, actual);

        public static void Equal(double expected, double actual, int precision)
        {
            var delta = Math.Pow(10, -precision);
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expected, actual, delta);
        }

        public static void NotNull(object? value, string? message = null)
            => Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(value, message);

        public static void Null(object? value, string? message = null)
            => Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNull(value, message);

        public static void Empty<T>(IEnumerable<T> collection)
        {
            if (collection is null) throw new ArgumentNullException(nameof(collection));
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsFalse(collection.Any(), "Expected collection to be empty.");
        }

        public static void NotEmpty<T>(IEnumerable<T> collection)
        {
            if (collection is null) throw new ArgumentNullException(nameof(collection));
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(collection.Any(), "Expected collection to be non-empty.");
        }

        public static void Contains(string expectedSubstring, string actualString)
        {
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsNotNull(actualString);
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(actualString.Contains(expectedSubstring),
                $"Expected string to contain '{expectedSubstring}'.");
        }

        public static void Contains<T>(IEnumerable<T> collection, Func<T, bool> predicate)
        {
            if (collection is null) throw new ArgumentNullException(nameof(collection));
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsTrue(collection.Any(item => predicate(item)),
                "Expected collection to contain a matching element.");
        }

        public static void All<T>(IEnumerable<T> collection, Action<T> assertion)
        {
            if (collection is null) throw new ArgumentNullException(nameof(collection));
            if (assertion is null) throw new ArgumentNullException(nameof(assertion));
            foreach (var item in collection)
            {
                assertion(item);
            }
        }

        public static void Same(object expected, object actual)
            => Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreSame(expected, actual);

        public static TException Throws<TException>(Action action) where TException : Exception
        {
            if (action is null) throw new ArgumentNullException(nameof(action));
            return Microsoft.VisualStudio.TestTools.UnitTesting.Assert.ThrowsException<TException>(action);
        }

        public static void IsType<T>(object obj)
            => Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsInstanceOfType(obj, typeof(T));
    }
}
