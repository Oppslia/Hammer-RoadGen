using System;
using System.Collections.Generic;

namespace RoadGen.Tests;

/// <summary>Tiny shared assertion helpers used by every test-area file. The RoadGen test
/// runner is dependency-free, so each assertion simply throws on failure and the runner in
/// Program.cs catches it and reports a FAIL line.</summary>
public static class Checks
{
    public static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"{message} (expected '{expected}', got '{actual}')");
        }
    }
}
