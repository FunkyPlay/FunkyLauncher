// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;

namespace FunkyLauncher.Core;

/// <summary>
/// The door for the separate Funky Pro module (FunkyLauncher.Pro.dll). The open launcher only knows this interface;
/// the module is loaded when a valid Pro key is saved. Without the module or the key, every free feature works as usual.
/// </summary>
public interface IProModule
{
    string Version { get; }

    /// <summary>Called once after loading.</summary>
    void Start(IProHost host);

    /// <summary>How many automatic save backups to keep (free: 10).</summary>
    int AutoBackupsToKeep(int free);

    /// <summary>A save backup was just made (backupDir holds "files" and backup.json).</summary>
    void OnBackupCreated(string modlistId, string modlistTitle, string backupDir);

    /// <summary>The Pro panel shown in Tools → Funky Pro (null = none).</summary>
    FrameworkElement? CreatePanel();
}

/// <summary>What the launcher gives the Pro module.</summary>
public interface IProHost
{
    Settings Settings { get; }
    string DataDir { get; }
    string Language { get; }
    void Log(string message);
}
