// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FunkyLauncher.Services;

/// <summary>
/// Starts a program WITHOUT administrator rights even when the launcher has them (0.1.17, support #55/#57).
/// Wabbajack's window refuses to run when the process token's owner is the Administrators group
/// (Wabbajack 4.3.2.2, WindowsDesktop.IsElevated). That is so for "Run as administrator", and also for every program
/// when UAC is off or the player uses the built-in "Administrator" account — then Explorer itself is elevated, so
/// starting through explorer.exe does not help. Here the token is made like "runas /trustlevel:0x20000" (Windows SAFER
/// "normal user": the Administrators group becomes deny-only), its owner is set to the player's own account and its
/// integrity to Medium; then the program is started with it.
/// </summary>
public static class LowRights
{
    /// <summary>The same test Wabbajack makes: the token owner is Administrators (or the token is in that role).</summary>
    public static bool IsAdminToken()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            if (id.Owner is { } owner && owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)) return true;
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>Starts <paramref name="exe"/> as a normal user. False = Windows did not allow it (the reason is logged).</summary>
    public static bool TryStart(string exe, string? workDir, out string error)
    {
        error = "";
        IntPtr level = IntPtr.Zero, token = IntPtr.Zero;
        try
        {
            if (!SaferCreateLevel(SAFER_SCOPEID_USER, SAFER_LEVELID_NORMALUSER, SAFER_LEVEL_OPEN, out level, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SaferCreateLevel");
            if (!SaferComputeTokenFromLevel(level, IntPtr.Zero, out token, 0, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SaferComputeTokenFromLevel");

            // owner = the player's own account (Wabbajack looks at the owner first)
            using (var me = WindowsIdentity.GetCurrent())
            {
                var sid = new byte[me.User!.BinaryLength];
                me.User.GetBinaryForm(sid, 0);
                var pSid = Marshal.AllocHGlobal(sid.Length);
                try
                {
                    Marshal.Copy(sid, 0, pSid, sid.Length);
                    var owner = new TOKEN_OWNER { Owner = pSid };
                    if (!SetTokenInformation(token, TokenOwner, ref owner, Marshal.SizeOf<TOKEN_OWNER>()))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(owner)");
                }
                finally { Marshal.FreeHGlobal(pSid); }
            }
            SetMediumIntegrity(token); // best effort: a normal program runs at Medium

            if (OwnerIsAdmins(token)) throw new InvalidOperationException("the new token is still owned by Administrators");

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            var cmd = new System.Text.StringBuilder($"\"{exe}\"");
            var ok = CreateProcessAsUser(token, exe, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_PROCESS_GROUP,
                IntPtr.Zero, workDir, ref si, out var pi);
            if (!ok)
            {
                var first = Marshal.GetLastWin32Error();
                // the same token through the other Windows door (works for an elevated launcher, it holds the impersonate privilege)
                ok = CreateProcessWithTokenW(token, 0, exe, cmd, CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, workDir, ref si, out pi);
                if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcess* (first error {first})");
            }
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (level != IntPtr.Zero) SaferCloseLevel(level);
        }
    }

    /// <summary>For the test tool: the owner of a running process's token is Administrators.</summary>
    public static bool OwnerIsAdmins(IntPtr token)
    {
        GetTokenInformation(token, TokenOwner, IntPtr.Zero, 0, out var len);
        var buf = Marshal.AllocHGlobal(len);
        try
        {
            if (!GetTokenInformation(token, TokenOwner, buf, len, out _)) return false;
            var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buf));
            return sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static void SetMediumIntegrity(IntPtr token)
    {
        if (!ConvertStringSidToSid("S-1-16-8192", out var sid)) return;
        try
        {
            var tml = new TOKEN_MANDATORY_LABEL { Label = new SID_AND_ATTRIBUTES { Sid = sid, Attributes = SE_GROUP_INTEGRITY } };
            var size = Marshal.SizeOf<TOKEN_MANDATORY_LABEL>() + GetLengthSid(sid);
            SetTokenInformation(token, TokenIntegrityLevel, ref tml, size);
        }
        finally { LocalFree(sid); }
    }

    // ---------- Win32 ----------

    private const uint SAFER_SCOPEID_USER = 2, SAFER_LEVELID_NORMALUSER = 0x20000, SAFER_LEVEL_OPEN = 1;
    private const int TokenOwner = 4, TokenIntegrityLevel = 25;
    private const uint SE_GROUP_INTEGRITY = 0x20;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x400, CREATE_NEW_PROCESS_GROUP = 0x200;

    [StructLayout(LayoutKind.Sequential)] private struct TOKEN_OWNER { public IntPtr Owner; }
    [StructLayout(LayoutKind.Sequential)] private struct SID_AND_ATTRIBUTES { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct TOKEN_MANDATORY_LABEL { public SID_AND_ATTRIBUTES Label; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SaferCreateLevel(uint scope, uint level, uint flags, out IntPtr handle, IntPtr reserved);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SaferComputeTokenFromLevel(IntPtr level, IntPtr inToken, out IntPtr outToken, uint flags, IntPtr reserved);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SaferCloseLevel(IntPtr level);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(IntPtr token, int cls, ref TOKEN_OWNER info, int len);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(IntPtr token, int cls, ref TOKEN_MANDATORY_LABEL info, int len);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int len, out int retLen);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool ConvertStringSidToSid(string sid, out IntPtr pSid);
    [DllImport("advapi32.dll")] private static extern int GetLengthSid(IntPtr sid);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr token, string app, System.Text.StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string app, System.Text.StringBuilder cmd, uint flags,
        IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
}
