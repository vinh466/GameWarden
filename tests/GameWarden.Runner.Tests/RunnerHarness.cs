using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Contracts.Runner;

namespace GameWarden.Runner.Tests;

/// <summary>
/// Starts a real Runner (as Core will: own hidden console on Windows) running <c>FakeGame</c> in a temp folder.
/// Every Runner inherits "ignore CTRL+C" (Windows) or an ignored <c>SIGINT</c> (Linux) and must clear it itself.
/// </summary>
internal sealed partial class RunnerHarness : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // Linux: a shell ignores SIGINT, then becomes the Runner (exec keeps the pid). Like a background job, whose
    // SIGINT a non-interactive shell ignores, the Runner inherits the ignored disposition.
    private const string IgnoreSigintThenExec = "trap '' INT; exec \"$0\" \"$@\"";

    static RunnerHarness()
    {
        // Reproduce the trap from spike S1: the parent chain hands down "ignore CTRL+C".
        // Every Runner started by these tests inherits it and must clear it itself.
        if (OperatingSystem.IsWindows())
        {
            unsafe
            {
                InheritedIgnoreFlagSet = SetConsoleCtrlHandler(null, true);
            }
        }
    }

    private RunnerHarness(string root, string instanceId, Process runner)
    {
        Root = root;
        InstanceId = instanceId;
        Runner = runner;
    }

    /// <summary>Windows: the test process set the CTRL+C-ignore flag it hands down to every Runner.</summary>
    public static bool InheritedIgnoreFlagSet { get; }

    public string Root { get; }

    public string InstanceId { get; }

    public Process Runner { get; }

    public string WorldPath => Path.Combine(Root, "data", "world.txt");

    public string StateDirectory => Path.Combine(Root, "state");

    public string LogDirectory => Path.Combine(Root, "logs");

    public string ConsoleLogFile => Path.Combine(Root, "game-console.log");

    /// <param name="consoleToFile">The game writes its console to a file (like Unity's -logFile) that the Runner relays.</param>
    public static async Task<RunnerHarness> StartAsync(int lingerSeconds = 30, bool consoleToFile = false)
    {
        var instanceId = "test-" + Guid.NewGuid().ToString("N")[..10];
        var root = Path.Combine(Path.GetTempPath(), "gamewarden-tests", instanceId);
        Directory.CreateDirectory(root);
        var consoleLog = Path.Combine(root, "game-console.log");

        var spec = new LaunchSpec
        {
            InstanceId = instanceId,
            Executable = TestPlatform.Executable("FakeGame"),
            Arguments = ["--world", Path.Combine(root, "data", "world.txt"), .. consoleToFile ? new[] { "--log-file", consoleLog } : []],
            ConsoleLogFile = consoleToFile ? consoleLog : null,
            WorkingDirectory = root,
            LogDirectory = Path.Combine(root, "logs"),
            StateDirectory = Path.Combine(root, "state"),
            LingerSeconds = lingerSeconds,
        };
        var specPath = Path.Combine(root, "launchSpec.json");
        await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec, ContractJson.Options));

        var info = WithInheritedInterruptIgnored(TestPlatform.Executable("GameWarden.Runner"), "--spec", specPath);
        info.CreateNoWindow = true; // CREATE_NO_WINDOW: its own console, like Core starts it (02 §4.3)
        info.WorkingDirectory = root;
        var runner = Process.Start(info) ?? throw new InvalidOperationException("Runner did not start.");
        return new RunnerHarness(root, instanceId, runner);
    }

    /// <summary>
    /// Starts <paramref name="executable"/> so that it inherits an ignored CTRL+C: on Windows through the flag the
    /// static constructor set on this process, on Linux through a shell that ignores <c>SIGINT</c> and then execs it.
    /// </summary>
    public static ProcessStartInfo WithInheritedInterruptIgnored(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? executable : "/bin/sh") { UseShellExecute = false };
        if (!OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(IgnoreSigintThenExec);
            info.ArgumentList.Add(executable);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    public Task<RunnerClient> ConnectAsync(CancellationToken cancellationToken) =>
        RunnerClient.ConnectAsync(InstanceId, Runner.Id, Timeout, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Runner.HasExited)
            {
                Runner.Kill(entireProcessTree: true);
                await Runner.WaitForExitAsync(new CancellationTokenSource(Timeout).Token);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // Best effort cleanup.
        }

        Runner.Dispose();
        if (!OperatingSystem.IsWindows())
        {
            // A killed Runner leaves its pipe's socket file behind (the path .NET gives a pipe name on Unix).
            File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + RunnerPipe.NameFor(InstanceId)));
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A log file may still be held for a moment; the temp folder is cleaned by the OS later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public string ReadDiagnostics()
    {
        var path = Path.Combine(StateDirectory, "runner.log");
        if (!File.Exists(path))
        {
            return "(no runner.log)";
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetConsoleCtrlHandler(delegate* unmanaged<uint, int> handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
