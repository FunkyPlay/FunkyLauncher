// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using FunkyLauncher.Core;
using FunkyLauncher.Services;

namespace FunkyLauncher.Views;

/// <summary>Funky Player: plays any video, steps frame by frame, saves frames, cuts clips. The source video is never changed.</summary>
public partial class PlayerWindow : Window
{
    private Mpv? _mpv;
    private VideoHost? _host;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _dragging;
    private double? _in, _out;
    private string? _file;
    private string? _lastFolder;
    private readonly string? _startFile;

    public PlayerWindow(string? file = null)
    {
        InitializeComponent();
        _startFile = file;
        ApplyTexts();
        LoadList();
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { if (PlayerEngine.Ready) StartEngine(); else ShowSetup(); };
    }

    private void ApplyTexts()
    {
        ListTitle.Text = Loc.T("player.list");
        ListHint.Text = Loc.T("player.list.hint");
        OpenButton.Content = Loc.T("player.open");
        InButton.Content = Loc.T("player.in");
        OutButton.Content = Loc.T("player.out");
        FrameButton.Content = Loc.T("player.frame");
        CopyButton.Content = Loc.T("player.copy");
        AudioButton.Content = Loc.T("player.audio");
        FastButton.Content = Loc.T("player.fast");
        ExactButton.Content = Loc.T("player.exact");
        MuteCheck.Content = Loc.T("player.mute");
        FolderButton.Content = Loc.T("player.folder");
        SetupTitle.Text = Loc.T("player.setup.title");
        SetupText.Text = Loc.T("player.setup.text");
        SetupButton.Content = Loc.T("player.setup.button");
        ShowMarks();
    }

    // ---------- engine ----------

    private void ShowSetup()
    {
        SetupPanel.Visibility = Visibility.Visible;
        SetControls(false);
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        SetupButton.IsEnabled = false;
        SetupProgress.Visibility = Visibility.Visible;
        try
        {
            await PlayerEngine.EnsureAsync(new Progress<(string Step, int Percent)>(p =>
            {
                SetupStatus.Text = p.Step + (p.Percent is > 0 and < 100 ? $" — {p.Percent}%" : "");
                SetupProgress.Value = p.Percent;
            }), CancellationToken.None);
            SetupPanel.Visibility = Visibility.Collapsed;
            StartEngine();
        }
        catch (Exception ex)
        {
            SetupStatus.Text = Loc.F("player.setup.error", ex.Message);
            SetupButton.IsEnabled = true;
        }
    }

    private void StartEngine()
    {
        try
        {
            _host = new VideoHost();
            VideoBorder.Child = _host;
            _host.Ready += () =>
            {
                try
                {
                    _mpv = new Mpv(_host.Handle);
                    _mpv.Volume = 80;
                    SetControls(true);
                    _timer.Start();
                    var first = _startFile ?? (Files.Items.Count > 0 ? ((FileInfo)((ListBoxItem)Files.Items[0]).Tag).FullName : null);
                    if (_startFile != null) OpenFile(_startFile);
                    else if (first != null) Message.Text = Loc.T("player.pick");
                }
                catch (Exception ex) { Message.Text = Loc.F("player.setup.error", ex.Message); }
            };
        }
        catch (Exception ex) { Message.Text = Loc.F("player.setup.error", ex.Message); }
    }

    private void SetControls(bool on)
    {
        foreach (var c in new Control[] { BackButton, PlayButton, NextButton, InButton, OutButton, FrameButton, CopyButton, AudioButton, FastButton, ExactButton, Timeline, OpenButton, Files })
            c.IsEnabled = on;
    }

    // ---------- list ----------

    private void LoadList()
    {
        Files.Items.Clear();
        foreach (var f in Clipper.Recordings())
        {
            var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            panel.Children.Add(new TextBlock { Text = f.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(new TextBlock { Text = $"{ToolsView.When(f.LastWriteTime)} · {ToolsView.Size(f.Length)} · {f.Directory?.Name}", FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
            Files.Items.Add(new ListBoxItem { Content = panel, Tag = f, ToolTip = f.FullName, Foreground = (System.Windows.Media.Brush)FindResource("Text") });
        }
        if (Files.Items.Count == 0) Files.Items.Add(new ListBoxItem { Content = Loc.T("player.list.empty"), IsEnabled = false, Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
    }

    private void Files_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Files.SelectedItem is ListBoxItem { Tag: FileInfo f }) OpenFile(f.FullName);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("player.filter") + "|" + string.Join(";", Clipper.VideoExt.Select(x => "*" + x)) + "|*.*|*.*" };
        if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) OpenFile(files[0]);
    }

    private void OpenFile(string file)
    {
        if (_mpv == null) return;
        _file = file;
        _in = _out = null;
        _mpv.Load(file);
        _mpv.SetPause(false);
        Title = "Funky Player — " + Path.GetFileName(file);
        Message.Text = "";
        FolderButton.Visibility = Visibility.Collapsed;
        ShowMarks();
    }

    // ---------- playback ----------

    private void Tick()
    {
        if (_mpv == null) return;
        var dur = _mpv.Duration;
        var pos = _mpv.Position;
        if (dur > 0 && !_dragging)
        {
            Timeline.Maximum = dur;
            Timeline.Value = Math.Min(pos, dur);
        }
        TimeText.Text = $"{Clipper.Stamp(pos)} / {Clipper.Stamp(dur)}";
        PlayButton.Content = _mpv.Paused ? "▶" : "❚❚";
    }

    private void Timeline_Down(object sender, MouseButtonEventArgs e) => _dragging = true;

    private void Timeline_Up(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        // the value moves to the click point after this event
        Dispatcher.BeginInvoke(() => _mpv?.SeekAbsolute(Timeline.Value), DispatcherPriority.Input);
    }

    private void Play_Click(object sender, RoutedEventArgs e) => _mpv?.TogglePause();
    private void Back_Click(object sender, RoutedEventArgs e) => _mpv?.FrameBack();
    private void Next_Click(object sender, RoutedEventArgs e) => _mpv?.FrameStep();
    private void Audio_Click(object sender, RoutedEventArgs e) => _mpv?.CycleAudio();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_mpv == null) return;
        var handled = true;
        switch (e.Key)
        {
            case Key.Space: _mpv.TogglePause(); break;
            case Key.Left: if (_mpv.Paused) _mpv.FrameBack(); else _mpv.SeekRelative(-5); break;
            case Key.Right: if (_mpv.Paused) _mpv.FrameStep(); else _mpv.SeekRelative(5); break;
            case Key.I: In_Click(this, e); break;
            case Key.O: Out_Click(this, e); break;
            case Key.S: Frame_Click(this, e); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    // ---------- marks and clips ----------

    private void In_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv == null || _file == null) return;
        _in = _mpv.Position;
        if (_out <= _in) _out = null;
        _mpv.ShowText(Loc.F("player.in.set", Clipper.Stamp(_in.Value)));
        ShowMarks();
    }

    private void Out_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv == null || _file == null) return;
        _out = _mpv.Position;
        if (_in >= _out) _in = null;
        _mpv.ShowText(Loc.F("player.out.set", Clipper.Stamp(_out.Value)));
        ShowMarks();
    }

    private void ShowMarks()
    {
        var a = _in.HasValue ? Clipper.Stamp(_in.Value) : "—";
        var b = _out.HasValue ? Clipper.Stamp(_out.Value) : "—";
        var len = _in.HasValue && _out.HasValue ? Loc.F("player.len", (_out.Value - _in.Value).ToString("0.0")) : "";
        MarksText.Text = Loc.F("player.marks", a, b) + len;
        FastButton.IsEnabled = ExactButton.IsEnabled = _mpv != null && _in.HasValue && _out.HasValue;
    }

    private void Fast_Click(object sender, RoutedEventArgs e) => _ = Cut(fast: true);
    private void Exact_Click(object sender, RoutedEventArgs e) => _ = Cut(fast: false);

    private async Task Cut(bool fast)
    {
        if (_file == null || _in is not double a || _out is not double b) return;
        FastButton.IsEnabled = ExactButton.IsEnabled = false;
        Message.Text = Loc.T(fast ? "player.cutting.fast" : "player.cutting.exact");
        FolderButton.Visibility = Visibility.Collapsed;
        try
        {
            var outFile = await Clipper.CutAsync(_file, a, b, fast, MuteCheck.IsChecked == true, CancellationToken.None);
            Message.Text = Loc.F("player.cut.done", Path.GetFileName(outFile));
            _lastFolder = Clipper.ClipsDir;
            FolderButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { Message.Text = Loc.F("player.cut.error", ex.Message); }
        ShowMarks();
    }

    private void Frame_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv == null || _file == null) return;
        Directory.CreateDirectory(Clipper.FramesDir);
        var name = $"{Path.GetFileNameWithoutExtension(_file)} {Clipper.Stamp(_mpv.Position).Replace(':', '.')}.png";
        var file = Path.Combine(Clipper.FramesDir, name);
        _mpv.Screenshot(file);
        Message.Text = Loc.F("player.frame.done", name);
        _lastFolder = Clipper.FramesDir;
        FolderButton.Visibility = Visibility.Visible;
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_mpv == null || _file == null) return;
        var tmp = Path.Combine(Path.GetTempPath(), $"funky-frame-{Guid.NewGuid():N}.png");
        _mpv.Screenshot(tmp);
        for (var i = 0; i < 30 && !File.Exists(tmp); i++) await Task.Delay(100); // mpv writes the file asynchronously
        await Task.Delay(150);
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(tmp); bmp.EndInit(); bmp.Freeze();
            Clipboard.SetImage(bmp);
            Message.Text = Loc.T("player.copy.done");
        }
        catch (Exception ex) { Message.Text = Loc.F("player.cut.error", ex.Message); }
        try { File.Delete(tmp); } catch { }
    }

    private void Folder_Click(object sender, RoutedEventArgs e) { if (_lastFolder != null) PlayService.OpenFolder(_lastFolder); }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _mpv?.Dispose();
        _mpv = null;
    }
}

/// <summary>A plain child window mpv draws into. Disabled, so mouse and keys stay with the WPF window.</summary>
public sealed class VideoHost : HwndHost
{
    public event Action? Ready;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_DISABLED = 0x08000000, WS_CLIPSIBLINGS = 0x04000000;
        var h = CreateWindowEx(0, "static", "", WS_CHILD | WS_VISIBLE | WS_DISABLED | WS_CLIPSIBLINGS, 0, 0, 100, 100, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Dispatcher.BeginInvoke(() => Ready?.Invoke(), DispatcherPriority.Loaded);
        return new HandleRef(this, h);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) => DestroyWindow(hwnd.Handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
}
