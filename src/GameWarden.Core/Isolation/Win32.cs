using System.Runtime.InteropServices;

namespace GameWarden.Core.Isolation;

/// <summary>Win32 calls for the run-as user (ADR-0008). Exercised by hand on the VM (ADR-0012).</summary>
internal static unsafe partial class Win32
{
    public const int Logon32LogonBatch = 4;
    public const int Logon32ProviderDefault = 0;
    public const int ErrorLogonFailure = 1326;

    public const uint CreateUnicodeEnvironment = 0x00000400;
    public const uint CreateNoWindow = 0x08000000;

    public const int ProfileNoUi = 0x1;

    public const uint NerrSuccess = 0;
    public const uint NerrUserNotFound = 2221;
    public const uint UserPrivUser = 1;
    public const uint UfScript = 0x0001;
    public const uint UfPasswdCantChange = 0x0040;
    public const uint UfDontExpirePasswd = 0x10000;

    public const uint PolicyAllAccess = 0x000F0FFF;
    public const uint SePrivilegeEnabled = 0x2;
    public const uint TokenAdjustPrivileges = 0x20;
    public const uint TokenQuery = 0x8;

    // ----- logon, profile, process ---------------------------------------------------------

    [LibraryImport("advapi32.dll", EntryPoint = "LogonUserW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LogonUser(string userName, string domain, string password, int logonType, int logonProvider, out nint token);

    [LibraryImport("userenv.dll", EntryPoint = "LoadUserProfileW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LoadUserProfile(nint token, ProfileInfo* profileInfo);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnloadUserProfile(nint token, nint profile);

    [LibraryImport("userenv.dll", EntryPoint = "DeleteProfileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteProfile(string sid, nint profilePath, nint computerName);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateEnvironmentBlock(out nint environment, nint token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyEnvironmentBlock(nint environment);

    [LibraryImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcessAsUser(
        nint token,
        char* applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        char* currentDirectory,
        StartupInfo* startupInfo,
        ProcessInformation* processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential)]
    public struct ProfileInfo
    {
        public int Size;
        public int Flags;
        public char* UserName;
        public char* ProfilePath;
        public char* DefaultPath;
        public char* ServerName;
        public char* PolicyPath;
        public nint Profile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfo
    {
        public int Size;
        public char* Reserved;
        public char* Desktop;
        public char* Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    // ----- privileges ----------------------------------------------------------------------

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint desiredAccess, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, TokenPrivileges* newState, int bufferLength, nint previousState, nint returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TokenPrivileges
    {
        public int PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    // ----- local accounts ------------------------------------------------------------------

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint NetUserAdd(string? serverName, uint level, void* buffer, out uint parameterError);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint NetUserSetInfo(string? serverName, string userName, uint level, void* buffer, out uint parameterError);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint NetUserGetInfo(string? serverName, string userName, uint level, out nint buffer);

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint NetUserDel(string? serverName, string userName);

    [LibraryImport("netapi32.dll")]
    public static partial uint NetApiBufferFree(nint buffer);

    [StructLayout(LayoutKind.Sequential)]
    public struct UserInfo1
    {
        public char* Name;
        public char* Password;
        public uint PasswordAge;
        public uint Privilege;
        public char* HomeDir;
        public char* Comment;
        public uint Flags;
        public char* ScriptPath;
    }

    // ----- user rights (LSA) ---------------------------------------------------------------

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaOpenPolicy(LsaUnicodeString* systemName, LsaObjectAttributes* objectAttributes, uint desiredAccess, out nint policyHandle);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaAddAccountRights(nint policyHandle, byte* accountSid, LsaUnicodeString* userRights, uint countOfRights);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaRemoveAccountRights(nint policyHandle, byte* accountSid, [MarshalAs(UnmanagedType.U1)] bool allRights, LsaUnicodeString* userRights, uint countOfRights);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaClose(nint objectHandle);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaNtStatusToWinError(uint status);

    [StructLayout(LayoutKind.Sequential)]
    public struct LsaUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LsaObjectAttributes
    {
        public uint Length;
        public nint RootDirectory;
        public LsaUnicodeString* ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor;
        public nint SecurityQualityOfService;
    }
}
