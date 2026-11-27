// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FunkyLauncher.Core;
using FunkyLauncher.Services.Build;

namespace FunkyLauncher.Views;

/// <summary>27.11 (Funky, Nexus rules): the "Modlist" tools — profiles, own mods, space, moving… — free, as tabs.</summary>
public partial class ToolsView
{
    private static string T(string ru, string en) => Loc.Lang == "en" ? en : ru;
    private static string BuildCardTitle => T("Сборка", "Modlist");
    private static string BuildCardText => T("Профили, свои моды, место на диске, перенос и что изменилось после обновления.", "Profiles, your own mods, disk space, moving and what changed after an update.");

    public void ShowBuild(string? id = null) { _tool = id == null ? "build" : "build:" + id; Refresh(); }

    private void RefreshBuild()
    {
        BuildTitle.Text = BuildCardTitle;
        var m = BuildTools.SelectedModlist;
        BuildInfo.Text = BuildTools.Selected != null
            ? T($"Для сборки «{m!.Title}» — её выбирают в Библиотеке.", $"For \"{m!.Title}\" — pick the modlist in Library.")
            : T("Выбери установленную сборку в Библиотеке.", "Pick an installed modlist in Library.");
        var tools = BuildTools.List;
        if (tools.Count == 0) { BuildHost.Content = null; return; }
        var panel = tools.FirstOrDefault(p => "build:" + p.Id == _tool) ?? tools[0];
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var p in tools)
        {
            var b = new Button { Content = p.Icon + " " + p.Title, Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 8), FontSize = 13,
                Style = (Style)FindResource(p == panel ? "PrimaryButton" : "SecondaryButton") };
            var id = p.Id;
            b.Click += (_, _) => { _tool = "build:" + id; Refresh(); };
            tabs.Children.Add(b);
        }
        FrameworkElement el;
        try { el = panel.Create(); }
        catch (Exception ex) { el = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap }; }
        var box = new StackPanel();
        box.Children.Add(tabs);
        box.Children.Add(new TextBlock { Text = panel.Text, FontSize = 13, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        box.Children.Add(el);
        BuildHost.Content = box;
    }
}
