// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
namespace FunkyLauncher.Core;

/// <summary>An installed modlist as the modlist tools (Tools → Modlist) see it.</summary>
public sealed record ModlistInfo(
    string Id, string Title, string Game, string InstallDir, string? SaveFolder,
    string Mo2Profile, string GameExe, string? Version)
{
    public static ModlistInfo Of(Settings s, Modlist m) =>
        new(m.Id, m.Title, m.Game, s.InstallDir(m), SafeSaveFolder(m), m.Mo2Profile, m.GameExePath, s.InstalledVersion(m));

    private static string? SafeSaveFolder(Modlist m) { try { return m.ResolveSaveFolder(); } catch { return null; } }
}
