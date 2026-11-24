// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.RegularExpressions;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>
/// 0.1.22 (Funky 24.11): the player switches single mods of the base on or off in the launcher, without MO2.
/// Only the base pack's mods are listed (the add-on packs have their own switches). The choice is kept in the
/// settings and applied every time the profile is written, so an update keeps it.
/// </summary>
public static class ModChoices
{
    public sealed record Item(string Folder, string Name, bool DefaultOn, bool On, bool Core, bool HasPlugin)
    {
        /// <summary>Our one-line description (RU or EN by the launcher language), empty if none yet.</summary>
        public string About { get; init; } = "";
        /// <summary>The mod's page on Nexus, empty if unknown.</summary>
        public string Url { get; init; } = "";
    }

    /// <summary>Frameworks the other mods stand on, when the order file has no "core" list (game chats can override).</summary>
    private static readonly Regex KnownCore = new(
        @"^(Cyber Engine Tweaks|RED4ext|redscript |ArchiveXL|TweakXL|Codeware|Input Loader|Mod Settings|Native Settings UI \d|Redscript Config Framework|" +
        @"F4SE Scripts|Address Library|Mod Configuration Menu$|Starfield Script Extender|Plugins\.txt Enabler|Starfield Engine Fixes)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NexusTail = new(@"\s+\d{2,6}\s+v?[\w.\-]+\s+20\d\d$", RegexOptions.CultureInvariant);

    public static bool IsCore(ModOrder order, string folder) =>
        order.Core.Count > 0 ? order.Core.Contains(folder, StringComparer.OrdinalIgnoreCase) : KnownCore.IsMatch(folder);

    public static IReadOnlyDictionary<string, bool> Of(Settings s, Modlist m) =>
        s.ModChoices.TryGetValue(m.Id, out var c) ? c : new Dictionary<string, bool>();

    public static List<Item> Items(Settings s, Modlist m)
    {
        var list = new List<Item>();
        if (Addons.Base(m.Id) is not { } main || Addons.Order(m.Id) is not { } order) return list;
        var choices = Of(s, m);
        var off = new HashSet<string>(order.ModlistOff, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var about = Addons.About(m.Id);
        foreach (var x in main.Mods)
        {
            if (x.ModFolder.Length == 0 || !seen.Add(x.ModFolder)) continue;
            if (about.TryGetValue(x.ModFolder, out var ab) && ab == Addons.HiddenMark) continue;
            var def = !off.Contains(x.ModFolder);
            var core = IsCore(order, x.ModFolder);
            var on = core ? true : choices.TryGetValue(x.ModFolder, out var c) ? c : def;
            // "Alex Romanced 30777 2.0.1 2026" → "Alex Romanced": the Nexus id, version and year are noise for the player
            var name = NexusTail.Replace(x.DisplayName.ToString(), "").Trim();
            list.Add(new Item(x.ModFolder, name.Length > 0 ? name : x.ModFolder, def, on, core, x.Plugins.Count > 0)
            {
                About = about.TryGetValue(x.ModFolder, out var a) ? a : "",
                Url = x.Nexus is { ModId: > 0, Game.Length: > 0 } n ? $"https://www.nexusmods.com/{n.Game}/mods/{n.ModId}" : "",
            });
        }
        return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Keeps only what differs from the modlist's defaults, then rewrites the MO2 profile.</summary>
    public static void Save(Settings s, Modlist m, IEnumerable<(string Folder, bool On)> wanted)
    {
        var items = Items(s, m).ToDictionary(i => i.Folder, StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, on) in wanted)
            if (items.TryGetValue(folder, out var i) && !i.Core && on != i.DefaultOn) map[folder] = on;
        if (map.Count == 0) s.ModChoices.Remove(m.Id); else s.ModChoices[m.Id] = map;
        s.Save();
        if (s.IsInstalled(m) && Addons.Order(m.Id) is { } order)
        {
            if (AddonService.Mo2Running(s, m)) throw new InvalidOperationException(Loc.T("addon.err.mo2"));
            Mo2Profile.Write(s, m, order);
        }
    }
}
