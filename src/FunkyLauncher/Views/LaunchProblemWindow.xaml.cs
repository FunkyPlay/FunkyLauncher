// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Windows;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

public enum ProblemChoice { None, Repair, Rollback, Report }

/// <summary>"The game did not start / crashed" — offers repair, rollback to the previous version, or a report.</summary>
public partial class LaunchProblemWindow : Window
{
    public ProblemChoice Choice { get; private set; } = ProblemChoice.None;

    public LaunchProblemWindow(LaunchReport report, PreviousInstall? previous)
    {
        InitializeComponent();
        (TitleText.Text, BodyText.Text) = report.Result switch
        {
            LaunchResult.NotStarted => (Loc.T("problem.notstarted.title"), Loc.T("problem.notstarted.text")),
            LaunchResult.ClosedEarly => (Loc.T("problem.early.title"), Loc.F("problem.early.text", (int)report.RunTime.TotalSeconds)),
            _ => (Loc.T("problem.crash.title"), Loc.F("problem.crash.text", Math.Max(1, (int)report.RunTime.TotalMinutes))),
        };
        HelpText.Text = Loc.T("problem.help");
        CrashText.Text = report.CrashFile != null ? Loc.F("problem.crashfile", report.CrashFile) : "";
        RepairButton.Content = Loc.T("problem.repair");
        RollbackButton.Content = previous != null ? Loc.F("problem.rollback", previous.Version) : "";
        RollbackButton.Visibility = previous != null ? Visibility.Visible : Visibility.Collapsed;
        ReportButton.Content = Loc.T("problem.report");
        CloseButton.Content = Loc.T("problem.close");
    }

    private void Repair_Click(object sender, RoutedEventArgs e) { Choice = ProblemChoice.Repair; Close(); }
    private void Rollback_Click(object sender, RoutedEventArgs e) { Choice = ProblemChoice.Rollback; Close(); }
    private void Report_Click(object sender, RoutedEventArgs e) { Choice = ProblemChoice.Report; Close(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
