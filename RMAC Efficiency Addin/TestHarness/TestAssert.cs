#if TEST_HARNESS
using System;
using System.Collections.Generic;

namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Assertion helpers for self-test cases. Each helper throws
    /// <see cref="TestAssertException"/> on failure with a message describing what was expected
    /// versus what was received. The runner catches the exception and records the message.
    ///
    /// Named <c>TestAssert</c> rather than <c>Assert</c> to avoid collision with
    /// <c>System.Diagnostics.Debug.Assert</c> and various testing frameworks.
    /// </summary>
    internal static class TestAssert
    {
        public static void Equal<T>(T expected, T actual, string? message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new TestAssertException(
                    message ?? $"Expected '{Fmt(expected)}' but got '{Fmt(actual)}'.");
            }
        }

        public static void NotEqual<T>(T notExpected, T actual, string? message = null)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            {
                throw new TestAssertException(
                    message ?? $"Expected value to NOT equal '{Fmt(notExpected)}' but it did.");
            }
        }

        public static void True(bool condition, string? message = null)
        {
            if (!condition)
                throw new TestAssertException(message ?? "Expected condition to be true.");
        }

        public static void False(bool condition, string? message = null)
        {
            if (condition)
                throw new TestAssertException(message ?? "Expected condition to be false.");
        }

        public static void Null(object? value, string? message = null)
        {
            if (value != null)
                throw new TestAssertException(message ?? $"Expected null but got '{Fmt(value)}'.");
        }

        public static void NotNull(object? value, string? message = null)
        {
            if (value == null)
                throw new TestAssertException(message ?? "Expected non-null value.");
        }

        public static void Contains(string expectedSubstring, string? actual, string? message = null)
        {
            if (actual == null || !actual.Contains(expectedSubstring))
            {
                throw new TestAssertException(
                    message ?? $"Expected string to contain '{expectedSubstring}' but got '{Fmt(actual)}'.");
            }
        }

        public static void StartsWith(string expectedPrefix, string? actual, string? message = null)
        {
            if (actual == null || !actual.StartsWith(expectedPrefix))
            {
                throw new TestAssertException(
                    message ?? $"Expected string to start with '{expectedPrefix}' but got '{Fmt(actual)}'.");
            }
        }

        public static void HasLengthAtMost(int max, string? actual, string? message = null)
        {
            if (actual == null)
                throw new TestAssertException(message ?? "Expected non-null string.");
            if (actual.Length > max)
            {
                throw new TestAssertException(
                    message ?? $"Expected string length <= {max} but got {actual.Length} ('{actual}').");
            }
        }

        private static string Fmt(object? value)
        {
            return value switch
            {
                null => "<null>",
                string s => s,
                _ => value.ToString() ?? "<null>"
            };
        }
    }

    internal sealed class TestAssertException : Exception
    {
        public TestAssertException(string message) : base(message) { }
    }
}
#endif
