using System.Runtime.CompilerServices;

namespace GameWarden.Tests;

/// <summary>What the test tiers of ADR-0016 need to know about the operating system they run on.</summary>
public static class TestPlatform
{
    public const string WindowsOnly = "Tests a Windows adapter: runs on Windows only (ADR-0016, tier Windows).";

    public const string LinuxOnly = "Tests an adapter of the development platform: runs on Linux only (ADR-0016).";

    /// <summary>An executable built next to the tests: <c>FakeGame.exe</c> on Windows, <c>FakeGame</c> elsewhere.</summary>
    public static string Executable(string name) =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? name + ".exe" : name);
}

/// <summary>A test of a Windows adapter (tier <c>Windows</c>): skipped on other operating systems, so <c>dotnet test</c> stays green there.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = TestPlatform.WindowsOnly;
        }
    }
}

/// <summary>A test of an adapter of the Linux development platform (ADR-0016): skipped on other operating systems.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = TestPlatform.LinuxOnly;
        }
    }
}

/// <summary>A data-driven test of a Windows adapter (tier <c>Windows</c>): skipped on other operating systems.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = TestPlatform.WindowsOnly;
        }
    }
}
