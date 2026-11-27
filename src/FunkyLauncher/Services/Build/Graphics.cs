// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Services.Build;

/// <summary>
/// №11 (free since 27.11) graphics presets in one click (Quality / Balance / Performance) — the game's own settings file is changed,
/// the player's file is kept once as "*.funky-before" and can be put back. №12 the preset picked by the PC check.
/// Supported now: the one-folder-per-save game (UserSettings.json) and the .fos game (…Prefs.ini). Others: a message.
/// </summary>
public static class Graphics
{
    public static readonly string[] Names = { "quality", "balance", "performance" };

    public static void Start()
    {
        BuildTools.Add("graphics", "◐", () => Ui.T("Пресеты графики", "Graphics presets"),
            () => Ui.T("Качество, баланс или производительность — одним выбором.", "Quality, balance or performance — pick one."),
            () => { var r = new StackPanel(); Fill(r); return r; });
    }

    private static string Label(string p) => p switch
    {
        "quality" => Ui.T("Качество", "Quality"),
        "balance" => Ui.T("Баланс", "Balance"),
        _ => Ui.T("Производительность", "Performance"),
    };

    private enum Kind { None, Json, Fo4 }

    private static Kind KindOf(ModlistInfo m) =>
        m.Game.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase) ? Kind.Json
        : m.Game.Contains("Fallout 4", StringComparison.OrdinalIgnoreCase) ? Kind.Fo4 : Kind.None;

    private static bool LocalSettings(ModlistInfo m, string profile)
    {
        // MO2 (profile.cpp): no LocalSettings in the profile's settings.ini = the instance default
        // profile_local_inis from ModOrganizer.ini (a profile the launcher made and MO2 has not opened yet)
        var ini = Path.Combine(m.InstallDir, "profiles", profile, "settings.ini");
        if (Flag(ini, "LocalSettings") is { } own) return own;
        return Flag(Path.Combine(m.InstallDir, "ModOrganizer.ini"), "profile_local_inis") ?? false;
    }

    private static bool? Flag(string file, string key)
    {
        if (!File.Exists(file)) return null;
        foreach (var l in File.ReadLines(file))
        {
            var t = l.Trim();
            if (t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return t[(key.Length + 1)..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }

    /// <summary>The settings file the game really reads (profile-local when Mod Organizer keeps settings per profile).</summary>
    public static string? SettingsFile(ModlistInfo m)
    {
        var profile = BuildData.Current.ActiveProfile.TryGetValue(m.Id, out var a) && a.Length > 0 ? Profiles.Prefix + a : m.Mo2Profile;
        switch (KindOf(m))
        {
            case Kind.Json:
                return LocalSettings(m, profile) ? Path.Combine(m.InstallDir, "profiles", profile, "UserSettings.json")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json");
            case Kind.Fo4:
                return LocalSettings(m, profile) ? Path.Combine(m.InstallDir, "profiles", profile, "Fallout4Prefs.ini")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Fallout4", "Fallout4Prefs.ini");
            default: return null;
        }
    }

    // ---------- the json game ----------

    // option → value for quality / balance / performance (null = leave as is)
    private static readonly Dictionary<string, object?[]> JsonPreset = new()
    {
        ["TextureQuality"] = new object?[] { "High", "High", "Medium" },
        ["ShadowMeshQuality"] = new object?[] { "High", "Medium", "Low" },
        ["LocalShadowsQuality"] = new object?[] { "High", "Medium", "Low" },
        ["CascadedShadowsRange"] = new object?[] { "High", "Medium", "Low" },
        ["CascadedShadowsResolution"] = new object?[] { "High", "Medium", "Low" },
        ["DistantShadowsResolution"] = new object?[] { "High", "Medium", "Low" },
        ["VolumetricFogResolution"] = new object?[] { "Ultra", "Medium", "Low" },
        ["VolumetricCloudsQuality"] = new object?[] { "Ultra", "Medium", "Low" },
        ["MaxDynamicDecals"] = new object?[] { "Ultra", "Medium", "Low" },
        ["ScreenSpaceReflectionsQuality"] = new object?[] { "Ultra", "Medium", "Low" },
        ["SubsurfaceScatteringQuality"] = new object?[] { "High", "Medium", "Low" },
        ["AmbientOcclusion"] = new object?[] { "High", "Medium", "Low" },
        ["ColorPrecision"] = new object?[] { "High", "Medium", "Medium" },
        ["MirrorQuality"] = new object?[] { "High", "Medium", "Low" },
        ["LODPreset"] = new object?[] { "High", "Medium", "Low" },
        ["CrowdDensity"] = new object?[] { "High", "Medium", "Low" },
        ["Anisotropy"] = new object?[] { 16, 8, 4 },
        ["ContactShadows"] = new object?[] { true, true, false },
        ["FacialTangentUpdates"] = new object?[] { true, true, false },
        ["RayTracing"] = new object?[] { null, false, false },
        ["RayTracedPathTracing"] = new object?[] { null, false, false },
    };

    public static int ApplyJson(string file, int level)
    {
        var root = JsonNode.Parse(File.ReadAllText(file))!;
        var n = 0;
        foreach (var g in root["data"]!.AsArray())
        {
            if (g?["group_name"]?.GetValue<string>() is not { } gname || !gname.StartsWith("/graphics")) continue;
            foreach (var o in g["options"]!.AsArray())
            {
                var name = o?["name"]?.GetValue<string>() ?? "";
                if (name == "QuickPresets") { o!["value"] = "Custom"; continue; }
                if (!JsonPreset.TryGetValue(name, out var vals) || vals[level] is not { } v) continue;
                var values = o!["values"] as JsonArray;
                if (values != null)
                {
                    var list = values.Select(x => x?.ToJsonString().Trim('"')).ToList();
                    var want = v is bool b ? (b ? "true" : "false") : v.ToString()!;
                    var i = list.IndexOf(want);
                    if (i < 0) continue; // this game version has no such value — leave it
                    o["value"] = v is int iv ? JsonValue.Create(iv) : v is bool bv ? JsonValue.Create(bv) : JsonValue.Create(want);
                    o["index"] = i;
                }
                else if (v is bool bb) o["value"] = bb;
                else if (v is int ii) o["value"] = ii;
                else o["value"] = v.ToString();
                n++;
            }
        }
        File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return n;
    }

    // ---------- the .fos game ----------

    private static readonly (string Section, string Key, string[] Vals)[] Fo4Preset =
    {
        ("Display", "iShadowMapResolution", new[] { "4096", "2048", "1024" }),
        ("Display", "fShadowDistance", new[] { "20000.0000", "14000.0000", "3000.0000" }),
        ("Display", "fDirShadowDistance", new[] { "20000.0000", "14000.0000", "3000.0000" }),
        ("Display", "iMaxFocusShadows", new[] { "4", "2", "1" }),
        ("Display", "bVolumetricLightingEnable", new[] { "1", "1", "0" }),
        ("Display", "iVolumetricLightingQuality", new[] { "2", "1", "0" }),
        ("Display", "bSAOEnable", new[] { "1", "1", "0" }),
        ("Display", "iTexMipMapSkip", new[] { "0", "0", "1" }),
        ("Display", "sAntiAliasing", new[] { "TAA", "TAA", "FXAA" }),
        ("LOD", "fLODFadeOutMultObjects", new[] { "15.0000", "10.0000", "6.0000" }),
        ("LOD", "fLODFadeOutMultItems", new[] { "6.0000", "4.0000", "2.5000" }),
        ("LOD", "fLODFadeOutMultActors", new[] { "15.0000", "10.0000", "6.0000" }),
    };

    private static int ApplyFo4(string file, int level)
    {
        foreach (var (sec, key, vals) in Fo4Preset) Mo2Profile.SetIni(file, sec, key, vals[level]);
        return Fo4Preset.Length;
    }

    // ---------- apply / restore ----------

    public static string Apply(ModlistInfo m, string preset)
    {
        var file = SettingsFile(m) ?? throw new InvalidOperationException(Ui.T("Для этой игры пресетов пока нет.", "No presets for this game yet."));
        if (!File.Exists(file)) throw new InvalidOperationException(Ui.T("Файла настроек игры ещё нет — запусти игру один раз: ", "The game's settings file isn't there yet — start the game once: ") + file);
        if (Ui.GameRunning(m)) throw new InvalidOperationException(Ui.T("Сначала закрой игру.", "Close the game first."));
        var before = file + ".funky-before";
        if (!File.Exists(before)) File.Copy(file, before); // the player's own settings, kept once
        var level = Array.IndexOf(Names, preset);
        var n = KindOf(m) == Kind.Json ? ApplyJson(file, level) : ApplyFo4(file, level);
        BuildData.Current.Preset[m.Id] = preset; BuildData.Save();
        BuildTools.Log($"preset {m.Id}: {preset}, {n} settings");
        return Ui.T($"«{Label(preset)}» включён: изменено настроек {n}. Масштабирование (DLSS/FSR) и разрешение не трогаю — их удобнее выбрать в игре.",
                    $"\"{Label(preset)}\" applied: {n} settings changed. Upscaling (DLSS/FSR) and resolution are left to the game menu.");
    }

    public static string Restore(ModlistInfo m)
    {
        var file = SettingsFile(m);
        if (file == null || !File.Exists(file + ".funky-before")) return Ui.T("Возвращать нечего — пресет ещё не включали.", "Nothing to put back — no preset was applied.");
        if (Ui.GameRunning(m)) throw new InvalidOperationException(Ui.T("Сначала закрой игру.", "Close the game first."));
        File.Copy(file + ".funky-before", file, overwrite: true);
        BuildData.Current.Preset.Remove(m.Id); BuildData.Save();
        return Ui.T("Твои прежние настройки возвращены.", "Your previous settings are back.");
    }

    /// <summary>№12: the launcher's own PC check → the preset.</summary>
    public static (string Preset, string Why) Advise(ModlistInfo m)
    {
        var ml = Catalog.Find(m.Id);
        var hw = PcCheck.Read(m.InstallDir);
        var tier = ml?.PcCheck is { } req ? PcCheck.Tier(hw.Gpu, hw.VramGb, req).Tier : GpuTier.Unknown;
        var big = hw.ScreenWidth >= 3000; // 4K
        var preset = tier switch
        {
            GpuTier.High => big ? "balance" : "quality",
            GpuTier.Mid => big ? "performance" : "balance",
            _ => "performance",
        };
        if (hw.RamGb > 0 && hw.RamGb < 16 && preset == "quality") preset = "balance";
        var why = $"{hw.Gpu} · {hw.VramGb:0} " + Ui.T("ГБ видеопамяти", "GB VRAM") + $" · {hw.RamGb:0} " + Ui.T("ГБ ОЗУ", "GB RAM") +
                  (hw.ScreenWidth > 0 ? $" · {hw.ScreenWidth}×{hw.ScreenHeight}" : "");
        return (preset, why);
    }

    // ---------- panel ----------

    private static void Fill(StackPanel root)
    {
        root.Children.Clear();
        var m = BuildTools.Selected;
        if (m == null) { root.Children.Add(Ui.Text(Ui.T("Выбери установленную сборку в Библиотеке.", "Pick an installed modlist in Library."))); return; }
        root.Children.Add(Ui.Title(Ui.T("Графика — ", "Graphics — ") + m.Title));
        if (KindOf(m) == Kind.None)
        {
            root.Children.Add(Ui.Text(Ui.T("Для этой игры пресетов пока нет — настрой графику в самой игре.", "No presets for this game yet — set the graphics in the game itself.")));
            return;
        }
        var status = Ui.Text("", 12.5, Ui.Cyan, top: 6);
        var cur = BuildData.Current.Preset.TryGetValue(m.Id, out var c) ? c : "";
        root.Children.Add(Ui.Text(Ui.T("Меняются настройки самой игры (тени, туман, отражения, дальность…). Твои прежние настройки сохраняются один раз — их можно вернуть.",
            "The game's own settings change (shadows, fog, reflections, distance…). Your previous settings are kept once and can be put back."), bottom: 6));
        var row = Ui.Row();
        foreach (var p in Names)
            row.Children.Add(Ui.Button((cur == p ? "✓ " : "") + Label(p), () =>
            {
                try { status.Text = Apply(m, p); Fill(root); }
                catch (Exception ex) { status.Text = Ui.T("Не получилось: ", "Failed: ") + ex.Message; }
            }, primary: cur == p, height: 34));
        row.Children.Add(Ui.Button(Ui.T("Вернуть мои настройки", "Put my settings back"), () =>
        {
            try { status.Text = Restore(m); Fill(root); } catch (Exception ex) { status.Text = ex.Message; }
        }, height: 34));
        root.Children.Add(row);

        var advice = Ui.Text(Ui.T("Смотрю твой ПК…", "Checking your PC…"), 13, Ui.TextBrush, top: 12);
        root.Children.Add(advice);
        var adviceRow = Ui.Row();
        root.Children.Add(adviceRow);
        root.Children.Add(status);
        Task.Run(() => Advise(m)).ContinueWith(t =>
        {
            if (t.Exception != null) { advice.Text = ""; return; }
            var (p, why) = t.Result;
            advice.Text = Ui.T("Для твоего ПК советую: ", "For your PC: ") + Label(p) + "\n" + why;
            adviceRow.Children.Add(Ui.Button(Ui.T("Включить совет", "Apply the advice"), () =>
            {
                try { status.Text = Apply(m, p); } catch (Exception ex) { status.Text = Ui.T("Не получилось: ", "Failed: ") + ex.Message; }
            }, primary: true, height: 30));
        }, TaskScheduler.FromCurrentSynchronizationContext());
        var file = SettingsFile(m);
        if (file != null) root.Children.Add(Ui.Text(Ui.T("Файл: ", "File: ") + file, 11.5, top: 8));
    }
}
