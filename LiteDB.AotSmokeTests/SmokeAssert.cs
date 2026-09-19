using System;

#nullable enable
namespace LiteDB.AotSmokeTests
{
    /// <summary>
    /// Shared assertion and reporting helpers used by every smoke scenario. The helpers only
    /// print fixed text so the transcript stays byte-identical across regular, trimmed, and
    /// Native AOT builds.
    /// </summary>
    internal static class SmokeAssert
    {
        public static void RunScenario(string name, Action scenario)
        {
            Console.WriteLine($"[SCENARIO] {name}");
            scenario();
            Console.WriteLine($"[PASS] {name}");
            Console.WriteLine();
        }

        public static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Asserts a single named runtime-feature check. The name identifies the failing API in the
        /// transcript without printing any value that could differ between publish modes.
        /// </summary>
        public static void RequireCheck(string check, bool condition)
        {
            Require(condition, $"The Native AOT runtime feature check '{check}' failed.");
        }

        public static void RequireThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }
    }
}
