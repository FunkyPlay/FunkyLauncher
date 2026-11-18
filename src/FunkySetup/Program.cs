// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
// A modlist's small installer: gets the official Funky Launcher from GitHub releases (checked by SHA-256), puts it into
// %LOCALAPPDATA%\FunkyLauncher\app and starts it on the install page of this modlist ("--open <id>").
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FunkySetup;

internal static unsafe class Program
{
    private const string ExeUrl = "https://github.com/FunkyPlay/FunkyLauncher/releases/latest/download/FunkyLauncher.exe";
    private const string HashUrl = ExeUrl + ".sha256";
    private const string ManualUrl = "https://funkylauncher.com/download";

    private static string _id = "", _title = "";
    private static nint _wnd, _text, _bar;
    private static string _error = "";

    [STAThread]
    private static int Main()
    {
        foreach (var a in typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (a.Key == "ModlistId") _id = a.Value ?? "";
            if (a.Key == "ModlistTitle") _title = a.Value ?? "";
        }
        CreateUi();
        var worker = new Thread(Work) { IsBackground = true };
        worker.Start();

        MSG msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        if (_error.Length > 0)
        {
            var answer = MessageBoxW(0,
                $"Не получилось подготовить Funky Launcher:\n{_error}\n\nСкачать лаунчер вручную с funkylauncher.com/download?",
                $"{_title} — установка", 0x4 | 0x30); // Yes/No, warning
            if (answer == 6) Process.Start(new ProcessStartInfo(ManualUrl) { UseShellExecute = true });
            return 1;
        }
        return 0;
    }

    private static void Work()
    {
        try
        {
            // FUNKYLAUNCHER_DATA (developer tests only): the same throwaway folder the launcher uses
            var data = Environment.GetEnvironmentVariable("FUNKYLAUNCHER_DATA") is { Length: > 0 } test
                ? test : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunkyLauncher");
            var dir = Path.Combine(data, "app");
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "FunkyLauncher.exe");

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FunkySetup/1.0");

            Status("Проверяю последнюю версию Funky Launcher…", 0);
            string? want = null;
            try { want = http.GetStringAsync(HashUrl).GetAwaiter().GetResult().Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant(); }
            catch { /* offline: use the copy we have, if any */ }

            var have = File.Exists(exe) ? Sha256(exe) : null;
            if (want == null && have == null) throw new Exception("нет связи с GitHub. Проверьте интернет.");

            if (want != null && have != want)
            {
                var part = exe + ".part";
                using (var resp = http.GetAsync(ExeUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    resp.EnsureSuccessStatusCode();
                    var total = resp.Content.Headers.ContentLength ?? 0;
                    using var src = resp.Content.ReadAsStream();
                    using var dst = File.Create(part);
                    var buf = new byte[1 << 16];
                    long done = 0;
                    int n, lastPct = -1;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        done += n;
                        var pct = total > 0 ? (int)(done * 100 / total) : 0;
                        if (pct != lastPct)
                        {
                            lastPct = pct;
                            Status($"Скачиваю Funky Launcher с GitHub… {done / 1048576} из {total / 1048576} МБ", pct);
                        }
                    }
                }
                Status("Проверяю файл…", 100);
                if (Sha256(part) != want) { File.Delete(part); throw new Exception("файл скачался с ошибкой (не совпала контрольная сумма). Попробуйте ещё раз."); }
                try { File.Move(part, exe, true); }
                catch when (have != null) { File.Delete(part); } // the launcher is open: it updates itself
            }

            Status($"Открываю установку {_title}…", 100);
            Process.Start(new ProcessStartInfo(exe, $"--open {_id} --from-setup") { WorkingDirectory = dir, UseShellExecute = false });
            Thread.Sleep(800);
        }
        catch (Exception e) { _error = e.Message; }
        PostMessageW(_wnd, 0x0010, 0, 0); // WM_CLOSE
    }

    private static string Sha256(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    private static void Status(string text, int pct)
    {
        fixed (char* t = text) SendMessageW(_text, 0x000C, 0, (nint)t); // WM_SETTEXT
        SendMessageW(_bar, 0x0402, pct, 0);                              // PBM_SETPOS
    }

    // ---- a tiny Win32 window: one line of text and a progress bar
    private static void CreateUi()
    {
        var inst = GetModuleHandleW(null);
        fixed (char* cls = "FunkySetupWnd")
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = inst,
                hCursor = LoadCursorW(0, 32512),
                hbrBackground = 16, // COLOR_BTNFACE + 1
                lpszClassName = cls,
                hIcon = LoadIconW(inst, (char*)32512),
            };
            RegisterClassExW(&wc);
            var dpi = GetDpiForSystem();
            int S(int v) => v * (int)dpi / 96;
            var title = $"{_title} — установка (Funky Launcher)";
            fixed (char* tt = title)
            {
                int w = S(480), h = S(150);
                int x = (GetSystemMetrics(0) - w) / 2, y = (GetSystemMetrics(1) - h) / 2;
                _wnd = CreateWindowExW(0, cls, tt, 0x00C80000 | 0x00020000 | 0x10000000, x, y, w, h, 0, 0, inst, null); // caption|sysmenu|minimize|visible
            }
            var font = CreateFontW(-S(15), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            fixed (char* st = "STATIC") fixed (char* empty = "Подготовка…")
                _text = CreateWindowExW(0, st, empty, 0x50000000, S(16), S(16), S(430), S(40), _wnd, 0, inst, null);
            fixed (char* pb = "msctls_progress32")
                _bar = CreateWindowExW(0, pb, null, 0x50000000, S(16), S(62), S(430), S(22), _wnd, 0, inst, null);
            SendMessageW(_text, 0x0030, font, 1); // WM_SETFONT
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint h, uint m, nint w, nint l)
    {
        if (m == 0x0002) { PostQuitMessage(0); return 0; } // WM_DESTROY
        return DefWindowProcW(h, m, w, l);
    }

    // ---- Win32
    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32")] private static extern ushort RegisterClassExW(WNDCLASSEXW* wc);
    [DllImport("user32")] private static extern nint CreateWindowExW(uint ex, char* cls, char* name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, void* param);
    [DllImport("user32")] private static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32")] private static extern int GetMessageW(MSG* msg, nint h, uint min, uint max);
    [DllImport("user32")] private static extern int TranslateMessage(MSG* msg);
    [DllImport("user32")] private static extern nint DispatchMessageW(MSG* msg);
    [DllImport("user32")] private static extern void PostQuitMessage(int code);
    [DllImport("user32")] private static extern int PostMessageW(nint h, uint m, nint w, nint l);
    [DllImport("user32")] private static extern nint SendMessageW(nint h, uint m, nint w, nint l);
    [DllImport("user32")] private static extern nint LoadCursorW(nint inst, nint id);
    [DllImport("user32")] private static extern nint LoadIconW(nint inst, char* id);
    [DllImport("user32")] private static extern int GetSystemMetrics(int i);
    [DllImport("user32")] private static extern uint GetDpiForSystem();
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(nint h, string text, string caption, uint type);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("gdi32", CharSet = CharSet.Unicode)] private static extern nint CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
}
