using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace GameWarden.Contracts.Runner;

/// <summary>
/// The process that serves a connected Runner pipe, as the operating system reports it, so Core can check that the
/// pipe really belongs to the Runner it started (OQ-26). Windows and the Linux development platform (ADR-0016).
/// </summary>
internal static class PipeServerProcess
{
    public static int Of(NamedPipeClientStream pipe)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsPipeServerProcess.Of(pipe.SafePipeHandle);
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxPipeServerProcess.Of(pipe.SafePipeHandle);
        }

        throw new PlatformNotSupportedException("The Runner pipe's server process can only be checked on Windows and Linux (ADR-0016).");
    }
}

/// <summary>The pipe server's process id from <c>GetNamedPipeServerProcessId</c> (02 §4.2).</summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsPipeServerProcess
{
    public static int Of(SafePipeHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var serverPid))
        {
            throw new IOException($"Cannot read the server process of the Runner pipe (error {Marshal.GetLastPInvokeError()}).");
        }

        return (int)serverPid;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}

/// <summary>
/// The peer's process id from <c>SO_PEERCRED</c>: on Linux a .NET pipe is a Unix domain socket, and the client's
/// <see cref="SafePipeHandle"/> holds the connected socket's file descriptor. For a client the peer credentials are
/// those of the process that called <c>listen()</c>: the Runner that created the pipe.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class LinuxPipeServerProcess
{
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    public static int Of(SafePipeHandle pipe)
    {
        var added = false;
        try
        {
            pipe.DangerousAddRef(ref added);
            var credentials = default(PeerCredentials);
            var length = (uint)sizeof(PeerCredentials);
            if (getsockopt((int)pipe.DangerousGetHandle(), SolSocket, SoPeerCred, &credentials, &length) != 0)
            {
                throw new IOException($"Cannot read the peer of the Runner pipe (errno {Marshal.GetLastPInvokeError()}).");
            }

            return credentials.Pid;
        }
        finally
        {
            if (added)
            {
                pipe.DangerousRelease();
            }
        }
    }

    /// <summary><c>struct ucred</c> from <c>&lt;sys/socket.h&gt;</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PeerCredentials
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int getsockopt(int socket, int level, int optionName, void* optionValue, uint* optionLength);
}
