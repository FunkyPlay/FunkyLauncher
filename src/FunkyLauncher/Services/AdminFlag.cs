// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Diagnostics;
using Microsoft.Win32;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// The "Run this program as an administrator" flag on the game exe (Properties → Compatibility). Windows keeps it in
/// AppCompatFlags\Layers as "RUNASADMIN" (some game copies' installers set it). Mod Organizer then can't start the game:
/// "Error 740 ERROR_ELEVATION_REQUIRED" and the launcher only saw "the game did not start in 3 minutes"
/// (support #142, Neon Nights, 0.1.17; Fallout 4 F4SE the same, 15.11).
/// </summary>
public static class AdminFlag
{
    private const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string Token = "RUNASADMIN";

    /// <summary>Where the flag sits: the player's own part of the registry and/or the "all users" part.</summary>
    public sealed record Found(string Exe, bool User, bool AllUsers);

    /// <summary>The game exe of the modlist inside the game folder.</summary>
    public static string? GameExe(Modlist ml, string? gamePath) =>
        string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(ml.GameExe) ? null : Path.GetFullPath(Path.Combine(gamePath, ml.GameExePath));

    public static Found? Check(Modlist ml, string? gamePath) => GameExe(ml, gamePath) is { } exe ? Check(exe) : null;

    public static Found? Check(string exe)
    {
        var user = Has(RegistryHive.CurrentUser, RegistryView.Default, exe);
        var all = Has(RegistryHive.LocalMachine, RegistryView.Registry64, exe) || Has(RegistryHive.LocalMachine, RegistryView.Registry32, exe);
        return user || all ? new Found(exe, user, all) : null;
    }

    private static bool Has(RegistryHive hive, RegistryView view, string exe) => Value(hive, view, exe) is { } v && Tokens(v).Contains(Token);

    /// <summary>The value data for the exe (value names are full paths, any letter case).</summary>
    private static string? Value(RegistryHive hive, RegistryView view, string exe) => ValueName(hive, view, exe) is { } n ? Read(hive, view, n) : null;

    private static string? ValueName(RegistryHive hive, RegistryView view, string exe)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var k = root.OpenSubKey(Layers);
            return k?.GetValueNames().FirstOrDefault(n => string.Equals(Norm(n), Norm(exe), StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private static string? Read(RegistryHive hive, RegistryView view, string name)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var k = root.OpenSubKey(Layers);
            return k?.GetValue(name) as string;
        }
        catch { return null; }
    }

    private static string Norm(string p) { try { return Path.GetFullPath(p.Trim().Trim('"')); } catch { return p.Trim(); } }

    private static List<string> Tokens(string data) => data.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>The data without RUNASADMIN; null when nothing but "~" (or nothing) is left → the value goes away.</summary>
    internal static string? Without(string data)
    {
        var rest = Tokens(data).Where(t => !t.Equals(Token, StringComparison.OrdinalIgnoreCase)).ToList();
        return rest.All(t => t == "~") ? null : string.Join(' ', rest);
    }

    /// <summary>
    /// Takes the flag off. The player's own part needs no rights; the "all users" part needs administrator rights —
    /// Windows asks once (UAC) and reg.exe changes only this one value. Returns null when done, otherwise the reason.
    /// </summary>
    public static string? Remove(Found f)
    {
        try
        {
            if (f.User && ValueName(RegistryHive.CurrentUser, RegistryView.Default, f.Exe) is { } n)
            {
                using var k = Registry.CurrentUser.OpenSubKey(Layers, writable: true);
                if (k?.GetValue(n) is string data)
                {
                    if (Without(data) is { } rest) k.SetValue(n, rest, RegistryValueKind.String);
                    else k.DeleteValue(n, throwOnMissingValue: false);
                }
                AppLog.Write($"admin flag: removed for this user, {f.Exe}");
            }
            if (f.AllUsers)
            {
                var cmds = new List<string>();
                foreach (var (view, sw) in new[] { (RegistryView.Registry64, "/reg:64"), (RegistryView.Registry32, "/reg:32") })
                {
                    if (ValueName(RegistryHive.LocalMachine, view, f.Exe) is not { } name || Read(RegistryHive.LocalMachine, view, name) is not { } data
                        || !Tokens(data).Contains(Token)) continue;
                    var key = $"\"HKLM\\{Layers}\"";
                    cmds.Add(Without(data) is { } rest
                        ? $"reg add {key} /v \"{name}\" /t REG_SZ /d \"{rest}\" /f {sw}"
                        : $"reg delete {key} /v \"{name}\" /f {sw}");
                }
                if (cmds.Count > 0)
                {
                    var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + string.Join(" & ", cmds))
                    { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
                    p?.WaitForExit(30000);
                }
                AppLog.Write($"admin flag: all-users part, {cmds.Count} change(s), {f.Exe}");
            }
            return Check(f.Exe) == null ? null : Loc.T("admin.flag.left");
        }
        catch (System.ComponentModel.Win32Exception) { return Loc.T("admin.flag.uac"); } // "No" in the Windows rights window
        catch (Exception ex) { AppLog.Write("admin flag: " + ex.Message); return ex.Message; }
    }

    /// <summary>The steps to do it by hand (shown under the row and in the dialog).</summary>
    public static string Steps(Found f) => Loc.F("admin.flag.steps", Path.GetFileName(f.Exe), Path.GetDirectoryName(f.Exe) ?? "");
}
