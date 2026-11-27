// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Text.Json;
using System.Windows;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services.Build;

/// <summary>One tool on the free "Modlist" page in Tools (a tab).</summary>
public sealed record BuildTool(string Id, string Icon, Func<string> TitleOf, Func<string> TextOf, Func<FrameworkElement> Create)
{
    public string Title => TitleOf();
    public string Text => TextOf();
}

/// <summary>
/// 27.11 (Funky, Nexus rules): everything that touches mods and modlists is free — these tools are free for everyone.
/// The launcher gives them the settings, the selected modlist and a few doors (PLAY, install); they keep their own
/// data in build-tools.json next to settings.json.
/// </summary>
public static class BuildTools
{
    private static Func<Modlist>? _selected;
    private static Action? _refresh;
    public static Settings Settings { get; private set; } = null!;
    public static readonly List<BuildTool> List = new();

    /// <summary>The selected modlist when it is installed (MO2 modlists only; GTA V has its own page).</summary>
    public static ModlistInfo? Selected =>
        _selected?.Invoke() is { } m && m.Installer != "gta" && Settings.IsInstalled(m) ? ModlistInfo.Of(Settings, m) : null;

    public static Modlist? SelectedModlist => _selected?.Invoke();

    public static IReadOnlyList<ModlistInfo> Modlists() =>
        Catalog.Items.Where(m => m.Installer != "gta" && Settings.IsInstalled(m)).Select(m => ModlistInfo.Of(Settings, m)).ToList();

    /// <summary>Right before the game starts (after the launcher's checks and backup).</summary>
    public static event Action<ModlistInfo>? GameStarting;

    public static void Init(Settings s, Func<Modlist> selected, Action refresh)
    {
        if (Settings != null) return;
        Settings = s; _selected = selected; _refresh = refresh;
        BuildData.Load();
        Space.Start();
        BeforeUpdate.Start();
        GameWatch.Start();
        Profiles.Start();
        OwnMods.Start(); // after Profiles: own mods go into the rebuilt profiles
        Mover.Start();
        Changes.Start();
        Graphics.Start();
        MoveFile.Start();
        GameSettings.Start();
    }

    public static void Add(string id, string icon, Func<string> title, Func<string> text, Func<FrameworkElement> create)
    {
        List.RemoveAll(t => t.Id == id);
        List.Add(new BuildTool(id, icon, title, text, create));
    }

    public static void RaiseGameStarting(Settings s, Modlist m)
    {
        if (Settings == null || m.Installer == "gta") return;
        foreach (var h in GameStarting?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { ((Action<ModlistInfo>)h)(ModlistInfo.Of(s, m)); } catch (Exception ex) { Log("game starting: " + ex.Message); }
    }

    public static void Log(string message) => AppLog.Write("build: " + message);
    public static void Notify(string title, string text) => Views.ScreenToast.Message(title, text);
    public static void RefreshUi() { try { _refresh?.Invoke(); } catch { } }
    public static string DataDir => AppConfig.DataDir;
}

/// <summary>The free modlist tools' own settings (build-tools.json).</summary>
public sealed class BuildData
{
    public static BuildData Current { get; private set; } = new();
    private static string FilePath => Path.Combine(AppConfig.DataDir, "build-tools.json");

    // space before play
    public bool DiskCheck { get; set; } = true;
    public int DiskMinGb { get; set; } = 10;

    // backup before update: modlist id → the version it was made for and its folder
    public Dictionary<string, BeforeUpdateMark> BeforeUpdate { get; set; } = new();

    // game version watch: on, backup on a change, modlist id → the game version already told about
    public bool GameWatch { get; set; } = true;
    public bool GameWatchBackup { get; set; } = true;
    public Dictionary<string, string> GameSeen { get; set; } = new();

    // own modlist profiles: modlist id → profiles, and the one PLAY starts ("" = the modlist's own)
    public Dictionary<string, List<OwnProfile>> Profiles { get; set; } = new();
    public Dictionary<string, string> ActiveProfile { get; set; } = new();

    // own mods on top of the modlist: modlist id → mods (files in DataDir\ownmods\<id>)
    public Dictionary<string, List<global::FunkyLauncher.Services.Build.OwnMods.Entry>> OwnMods { get; set; } = new();

    // what changed in the modlist: modlist id → one entry per update
    public Dictionary<string, List<ChangeEntry>> Changes { get; set; } = new();

    // graphics presets: modlist id → "quality" / "balance" / "performance"
    public Dictionary<string, string> Preset { get; set; } = new();

    // copy of the game's own settings: at PLAY when changed, extra files the player added, the last copied state
    public bool SettingsAuto { get; set; } = true;
    public Dictionary<string, List<string>> SettingsExtra { get; set; } = new();
    public Dictionary<string, string> SettingsStamp { get; set; } = new();

    public static void Load()
    {
        try { if (File.Exists(FilePath)) Current = JsonSerializer.Deserialize<BuildData>(File.ReadAllText(FilePath)) ?? new(); } catch { Current = new(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppConfig.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { BuildTools.Log("save: " + ex.Message); }
    }
}
