// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public sealed record VDesktop(Guid Id, string Name, int Index);

/// <summary>
/// Funky Desktops (test): every Windows virtual desktop shows its own set of desktop icons.
/// Windows shows ONE desktop folder on all virtual desktops, so icons that don't belong to the current desktop get the
/// "hidden" attribute — files are never moved, renamed or deleted. Only icons WE hid are ever un-hidden.
/// The current virtual desktop is read from the registry (no undocumented calls).
/// </summary>
public static class Desktops
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    /// <summary>Where the list of icons WE hid is kept (tests point it elsewhere).</summary>
    public static string StateDir { get; set; } = Path.Combine(AppConfig.DataDir, "desktops");
    private static string StatePath => Path.Combine(StateDir, "hidden.json");

    /// <summary>The user's desktop folder (may live in OneDrive). Tests point it elsewhere.</summary>
    public static string Root { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static List<VDesktop> List()
    {
        var list = new List<VDesktop>();
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        if (k?.GetValue("VirtualDesktopIDs") is not byte[] ids) return list;
        for (var i = 0; i + 16 <= ids.Length; i += 16)
        {
            var id = new Guid(ids.AsSpan(i, 16));
            using var d = Registry.CurrentUser.OpenSubKey($@"{Key}\Desktops\{{{id}}}");
            list.Add(new VDesktop(id, d?.GetValue("Name") as string ?? "", list.Count));
        }
        return list;
    }

    public static Guid? Current()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue("CurrentVirtualDesktop") is byte[] { Length: 16 } b ? new Guid(b) : null;
    }

    /// <summary>Desktop items we may manage: everything except system files and items the player hid himself.</summary>
    public static List<string> Items()
    {
        var hiddenByUs = LoadState();
        var list = new List<string>();
        foreach (var e in new DirectoryInfo(Root).EnumerateFileSystemInfos())
        {
            if (e.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            if ((e.Attributes & FileAttributes.System) != 0) continue;
            if ((e.Attributes & FileAttributes.Hidden) != 0 && !hiddenByUs.Contains(e.Name)) continue; // hidden by the player
            list.Add(e.Name);
        }
        return list.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Shows only what belongs to this desktop: hides the names in <paramref name="hide"/>, un-hides the rest we hid.</summary>
    public static int Apply(IEnumerable<string> hide)
    {
        var want = new HashSet<string>(hide, StringComparer.OrdinalIgnoreCase);
        var state = LoadState();
        var changed = 0;
        foreach (var e in new DirectoryInfo(Root).EnumerateFileSystemInfos())
        {
            try
            {
                var ours = state.Contains(e.Name);
                var isHidden = (e.Attributes & FileAttributes.Hidden) != 0;
                if (want.Contains(e.Name) && !isHidden)
                {
                    state.Add(e.Name); SaveState(state); // remember BEFORE changing — so it can always be undone
                    e.Attributes |= FileAttributes.Hidden;
                    changed++;
                }
                else if (!want.Contains(e.Name) && ours && isHidden)
                {
                    e.Attributes &= ~FileAttributes.Hidden;
                    state.Remove(e.Name); SaveState(state);
                    changed++;
                }
            }
            catch { /* a file in use or a cloud placeholder that refuses — skip it */ }
        }
        // names we hid that no longer exist (renamed / deleted by the player)
        if (state.RemoveWhere(n => !File.Exists(Path.Combine(Root, n)) && !Directory.Exists(Path.Combine(Root, n))) > 0) SaveState(state);
        if (changed > 0) Refresh();
        return changed;
    }

    /// <summary>Un-hides every icon Funky Desktops hid. Always safe to call.</summary>
    public static int ShowAll() => Apply(Array.Empty<string>());

    public static bool HasHidden => LoadState().Count > 0;

    private static HashSet<string> LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                return new HashSet<string>(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StatePath)) ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveState(HashSet<string> s)
    {
        Directory.CreateDirectory(StateDir);
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s.ToList()));
        File.Move(tmp, StatePath, overwrite: true);
    }

    private static void Refresh()
    {
        var p = Marshal.StringToHGlobalUni(Root);
        try { SHChangeNotify(0x00001000, 0x0005, p, IntPtr.Zero); } // SHCNE_UPDATEDIR, SHCNF_PATHW
        finally { Marshal.FreeHGlobal(p); }
    }

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
