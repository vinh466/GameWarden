using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using GameWarden.Core.Secrets;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace GameWarden.Core.Isolation;

/// <summary>
/// The local user <c>gamewarden-run</c> that runs Runners, games and SteamCMD (ADR-0008): created on Core's first
/// start as the service, with a random password kept as a secret, "log on as a batch job" granted, interactive and
/// Remote Desktop logon denied, and hidden from the sign-in screen. Its logon token and profile stay loaded while
/// Core runs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class RunAsAccount(IOptions<GameWardenOptions> options, ISecretStore secrets, ILogger<RunAsAccount> logger) : IDisposable
{
    public const string PasswordSecret = "run-as-password";

    private const string Comment = "GameWarden: runs game servers. Managed by GameWarden; removed on uninstall.";
    private const string SignInScreenKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList";
    private const uint AccountFlags = Win32.UfScript | Win32.UfPasswdCantChange | Win32.UfDontExpirePasswd;

    private static readonly string[] Rights = ["SeBatchLogonRight", "SeDenyInteractiveLogonRight", "SeDenyRemoteInteractiveLogonRight"];

    // CreateProcessAsUser and LoadUserProfile need these; LocalSystem holds them but not all are enabled by default.
    private static readonly string[] CorePrivileges = ["SeAssignPrimaryTokenPrivilege", "SeIncreaseQuotaPrivilege", "SeBackupPrivilege", "SeRestorePrivilege"];

    private readonly object _gate = new();
    private SafeAccessTokenHandle? _token;
    private nint _profile;
    private SecurityIdentifier? _sid;

    public string UserName => options.Value.RunAsUserName;

    public SecurityIdentifier Sid => _sid ?? throw new InvalidOperationException("The run-as account is not ready.");

    /// <summary>Batch logon token: primary token for CreateProcessAsUser, and for impersonation (ADR-0009).</summary>
    public SafeAccessTokenHandle Token => _token ?? throw new InvalidOperationException("The run-as account is not ready.");

    public void Ensure()
    {
        lock (_gate)
        {
            ReleaseLogon();
            EnablePrivileges();
            var password = secrets.Get(SecretScopes.System, PasswordSecret);
            if (!Exists())
            {
                password = NewPassword();
                secrets.Set(SecretScopes.System, PasswordSecret, password); // first, so we never own an account whose password is lost
                Create(password);
                logger.LogInformation("Created the local user {User} that runs game servers (ADR-0008).", UserName);
            }
            else if (password is null)
            {
                password = ResetPassword();
            }

            SetFlags();
            _sid = (SecurityIdentifier)new NTAccount(Environment.MachineName, UserName).Translate(typeof(SecurityIdentifier));
            ChangeRights(_sid, add: true);
            HideFromSignInScreen();
            if (!TryLogon(password, out var error) && error == Win32.ErrorLogonFailure)
            {
                logger.LogWarning("The stored password of {User} is wrong; resetting it.", UserName);
                password = ResetPassword();
                TryLogon(password, out error);
            }

            if (_token is null)
            {
                throw new Win32Exception(error, $"LogonUser({UserName}, batch) failed.");
            }

            LoadProfile();
        }
    }

    /// <summary>Uninstall (08 §4): removes the user, its rights, its profile and its password.</summary>
    public void Remove()
    {
        lock (_gate)
        {
            ReleaseLogon();
            if (Exists())
            {
                var sid = (SecurityIdentifier)new NTAccount(Environment.MachineName, UserName).Translate(typeof(SecurityIdentifier));
                ChangeRights(sid, add: false);
                var status = Win32.NetUserDel(null, UserName);
                if (status != Win32.NerrSuccess)
                {
                    throw new InvalidOperationException($"NetUserDel({UserName}) failed with {status}.");
                }

                if (!Win32.DeleteProfile(sid.Value, 0, 0))
                {
                    logger.LogWarning("Could not delete the profile of {User} (error {Error}); delete C:\\Users\\{User} by hand.", UserName, Marshal.GetLastPInvokeError(), UserName);
                }
            }

            using (var key = Registry.LocalMachine.OpenSubKey(SignInScreenKey, writable: true))
            {
                key?.DeleteValue(UserName, throwOnMissingValue: false);
            }

            secrets.Delete(SecretScopes.System, PasswordSecret);
            logger.LogInformation("Removed the local user {User}.", UserName);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            ReleaseLogon();
        }
    }

    private static string NewPassword() =>
        // 40 random base64 characters plus one of each class, so any complexity policy accepts it.
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(30)) + "aA1!";

    private bool Exists()
    {
        var status = Win32.NetUserGetInfo(null, UserName, 0, out var buffer);
        if (buffer != 0)
        {
            Win32.NetApiBufferFree(buffer);
        }

        return status switch
        {
            Win32.NerrSuccess => true,
            Win32.NerrUserNotFound => false,
            _ => throw new InvalidOperationException($"NetUserGetInfo({UserName}) failed with {status}."),
        };
    }

    private void Create(string password)
    {
        fixed (char* name = UserName, secret = password, comment = Comment)
        {
            var info = new Win32.UserInfo1 { Name = name, Password = secret, Privilege = Win32.UserPrivUser, Comment = comment, Flags = AccountFlags };
            var status = Win32.NetUserAdd(null, 1, &info, out var parameter);
            if (status != Win32.NerrSuccess)
            {
                throw new InvalidOperationException($"NetUserAdd({UserName}) failed with {status} (parameter {parameter}).");
            }
        }
    }

    private string ResetPassword()
    {
        var password = NewPassword();
        secrets.Set(SecretScopes.System, PasswordSecret, password);
        fixed (char* secret = password)
        {
            var info = secret; // USER_INFO_1003 holds just the password pointer
            var status = Win32.NetUserSetInfo(null, UserName, 1003, &info, out var parameter);
            if (status != Win32.NerrSuccess)
            {
                throw new InvalidOperationException($"Resetting the password of {UserName} failed with {status} (parameter {parameter}).");
            }
        }

        return password;
    }

    private void SetFlags()
    {
        var flags = AccountFlags; // USER_INFO_1008; also re-enables the account if someone disabled it
        var status = Win32.NetUserSetInfo(null, UserName, 1008, &flags, out _);
        if (status != Win32.NerrSuccess)
        {
            logger.LogWarning("Could not set the account flags of {User} ({Status}).", UserName, status);
        }
    }

    private void ChangeRights(SecurityIdentifier sid, bool add)
    {
        var sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        var attributes = new Win32.LsaObjectAttributes { Length = (uint)sizeof(Win32.LsaObjectAttributes) };
        var status = Win32.LsaOpenPolicy(null, &attributes, Win32.PolicyAllAccess, out var policy);
        if (status != 0)
        {
            throw new Win32Exception((int)Win32.LsaNtStatusToWinError(status), "LsaOpenPolicy failed.");
        }

        try
        {
            fixed (byte* sidPointer = sidBytes)
            {
                if (!add)
                {
                    Win32.LsaRemoveAccountRights(policy, sidPointer, allRights: true, null, 0); // "not found" is fine
                    return;
                }

                foreach (var right in Rights)
                {
                    fixed (char* text = right)
                    {
                        var value = new Win32.LsaUnicodeString { Length = (ushort)(right.Length * 2), MaximumLength = (ushort)((right.Length * 2) + 2), Buffer = text };
                        status = Win32.LsaAddAccountRights(policy, sidPointer, &value, 1);
                        if (status != 0)
                        {
                            throw new Win32Exception((int)Win32.LsaNtStatusToWinError(status), $"Granting {right} to {UserName} failed.");
                        }
                    }
                }
            }
        }
        finally
        {
            Win32.LsaClose(policy);
        }
    }

    private void HideFromSignInScreen()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(SignInScreenKey);
            key.SetValue(UserName, 0, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogWarning("Could not hide {User} from the sign-in screen: {Reason}", UserName, ex.Message);
        }
    }

    private bool TryLogon(string password, out int error)
    {
        if (Win32.LogonUser(UserName, ".", password, Win32.Logon32LogonBatch, Win32.Logon32ProviderDefault, out var token))
        {
            _token = new SafeAccessTokenHandle(token);
            error = 0;
            return true;
        }

        error = Marshal.GetLastPInvokeError();
        return false;
    }

    private void LoadProfile()
    {
        fixed (char* name = UserName)
        {
            var info = new Win32.ProfileInfo { Size = sizeof(Win32.ProfileInfo), Flags = Win32.ProfileNoUi, UserName = name };
            if (Win32.LoadUserProfile(_token!.DangerousGetHandle(), &info))
            {
                _profile = info.Profile;
            }
            else
            {
                // Games that write to %USERPROFILE% may fail; the rest still works.
                logger.LogError("LoadUserProfile({User}) failed with error {Error}.", UserName, Marshal.GetLastPInvokeError());
            }
        }
    }

    private void ReleaseLogon()
    {
        if (_token is not null && _profile != 0)
        {
            Win32.UnloadUserProfile(_token.DangerousGetHandle(), _profile);
            _profile = 0;
        }

        _token?.Dispose();
        _token = null;
    }

    private void EnablePrivileges()
    {
        if (!Win32.OpenProcessToken(Win32.GetCurrentProcess(), Win32.TokenAdjustPrivileges | Win32.TokenQuery, out var token))
        {
            logger.LogWarning("OpenProcessToken failed with error {Error}.", Marshal.GetLastPInvokeError());
            return;
        }

        try
        {
            foreach (var name in CorePrivileges)
            {
                if (!Win32.LookupPrivilegeValue(null, name, out var luid))
                {
                    continue;
                }

                var privileges = new Win32.TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = Win32.SePrivilegeEnabled };
                if (!Win32.AdjustTokenPrivileges(token, false, &privileges, sizeof(Win32.TokenPrivileges), 0, 0) || Marshal.GetLastPInvokeError() != 0)
                {
                    logger.LogWarning("Could not enable {Privilege} (error {Error}).", name, Marshal.GetLastPInvokeError());
                }
            }
        }
        finally
        {
            Win32.CloseHandle(token);
        }
    }
}
