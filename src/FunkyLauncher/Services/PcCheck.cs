// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>What Windows tells about this PC. Read locally, never sent anywhere.</summary>
public sealed class HardwareInfo
{
    public string Gpu { get; set; } = "";
    public double VramGb { get; set; }
    public string DriverVersion { get; set; } = "";
    public DateTime? DriverDate { get; set; }
    public string Cpu { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public double RamGb { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public bool Windows64 { get; set; } = true;
    public int WindowsBuild { get; set; }
    /// <summary>Install drive, its free space and type (true = SSD, false = HDD, null = unknown).</summary>
    public string Drive { get; set; } = "";
    public long FreeBytes { get; set; }
    public bool? Ssd { get; set; }
}

public enum GpuTier { Unknown, Low, Mid, High }

public sealed record PcRow(CheckState State, string Title, string Detail, bool Performance, string? AdviceText = null, string? SoftId = null);

public sealed record PcResult(CheckState Verdict, List<PcRow> Rows, GpuTier Tier, bool TierGuessed);

/// <summary>"Can my PC run it?": reads the hardware and compares it with the modlist requirements.</summary>
public static class PcCheck
{
    // ---------- reading the PC ----------

    public static HardwareInfo Read(string installDir)
    {
        var h = new HardwareInfo { Windows64 = Environment.Is64BitOperatingSystem, WindowsBuild = Environment.OSVersion.Version.Build };
        ReadGpu(h);
        Try(() =>
        {
            foreach (ManagementObject p in new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").Get())
            {
                h.Cpu = Regex.Replace(p["Name"]?.ToString() ?? "", @"\s+", " ").Trim();
                h.Cores += Convert.ToInt32(p["NumberOfCores"] ?? 0);
                h.Threads += Convert.ToInt32(p["NumberOfLogicalProcessors"] ?? 0);
            }
        });
        Try(() =>
        {
            foreach (ManagementObject c in new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").Get())
                h.RamGb = Convert.ToDouble(c["TotalPhysicalMemory"]) / (1L << 30);
        });
        ReadDisk(h, installDir);
        return h;
    }

    private static void ReadGpu(HardwareInfo h)
    {
        // video memory: WMI caps it at 4 GB, the driver's registry value is exact
        var vram = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        Try(() =>
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return;
            foreach (var sub in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
            {
                using var k = cls.OpenSubKey(sub);
                var name = k?.GetValue("DriverDesc")?.ToString();
                if (name == null) continue;
                var v = k!.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0), int i => (uint)i,
                    byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0), _ => 0L,
                };
                if (v > 0) vram[name] = Math.Max(vram.GetValueOrDefault(name), v / (double)(1L << 30));
            }
        });
        Try(() =>
        {
            var best = (Score: -1.0, Name: "", Driver: "", Date: (DateTime?)null, W: 0, H: 0);
            foreach (ManagementObject v in new ManagementObjectSearcher(
                         "SELECT Name, AdapterRAM, DriverVersion, DriverDate, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController").Get())
            {
                var name = v["Name"]?.ToString() ?? "";
                var mem = vram.TryGetValue(name, out var g) ? g : Convert.ToDouble(v["AdapterRAM"] ?? 0) / (1L << 30);
                // the discrete card wins over built-in graphics
                var score = mem + (Regex.IsMatch(name, "NVIDIA|GeForce|Radeon RX|Arc", RegexOptions.IgnoreCase) ? 100 : 0);
                if (score <= best.Score) continue;
                DateTime? date = null;
                var raw = v["DriverDate"]?.ToString();
                if (raw is { Length: >= 8 }) date = ManagementDateTimeConverter.ToDateTime(raw);
                best = (score, name, v["DriverVersion"]?.ToString() ?? "", date,
                        Convert.ToInt32(v["CurrentHorizontalResolution"] ?? 0), Convert.ToInt32(v["CurrentVerticalResolution"] ?? 0));
                h.VramGb = mem;
            }
            h.Gpu = best.Name; h.DriverVersion = best.Driver; h.DriverDate = best.Date;
            h.ScreenWidth = best.W; h.ScreenHeight = best.H;
        });
        if (h.ScreenWidth == 0) Try(() =>
        {
            foreach (ManagementObject v in new ManagementObjectSearcher("SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController").Get())
            {
                var w = Convert.ToInt32(v["CurrentHorizontalResolution"] ?? 0);
                if (w > h.ScreenWidth) { h.ScreenWidth = w; h.ScreenHeight = Convert.ToInt32(v["CurrentVerticalResolution"] ?? 0); }
            }
        });
    }

    private static void ReadDisk(HardwareInfo h, string installDir)
    {
        Try(() =>
        {
            var root = Path.GetPathRoot(Path.GetFullPath(installDir)) ?? "C:\\";
            h.Drive = root.TrimEnd('\\');
            var d = new DriveInfo(root);
            if (d.IsReady) h.FreeBytes = d.AvailableFreeSpace;
            var letter = h.Drive.TrimEnd(':');
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            string? disk = null;
            foreach (ManagementObject p in new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'")).Get())
                disk = p["DiskNumber"]?.ToString();
            if (disk == null) return;
            foreach (ManagementObject p in new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{disk}'")).Get())
            {
                var t = Convert.ToInt32(p["MediaType"] ?? 0);
                h.Ssd = t == 4 ? true : t == 3 ? false : null;
            }
        });
    }

    private static void Try(Action a) { try { a(); } catch { /* one missing value never breaks the whole check */ } }

    // ---------- judging ----------

    public static (GpuTier Tier, bool Guessed) Tier(string gpu, double vramGb, PcRequirements r)
    {
        // "Intel(R) Arc(TM) A770" → "Intel Arc A770"
        var name = Regex.Replace(Regex.Replace(gpu, @"\((R|TM)\)|®|™", "", RegexOptions.IgnoreCase), @"\s+", " ").ToUpperInvariant();
        var tier = GpuTier.Unknown;
        var best = 0;
        void Match(IEnumerable<string> list, GpuTier t)
        {
            foreach (var p in list)
            {
                var q = Regex.Replace(p, @"\s+", " ").ToUpperInvariant();
                if (q.Length > best && name.Contains(q)) { best = q.Length; tier = t; }
            }
        }
        Match(r.GpuHigh, GpuTier.High);
        Match(r.GpuMid, GpuTier.Mid);
        Match(r.GpuLow, GpuTier.Low);
        if (tier != GpuTier.Unknown && name.Contains("LAPTOP") && tier > GpuTier.Low
            && !r.LaptopKeep.Any(k => name.Contains(Regex.Replace(k, @"\s+", " ").ToUpperInvariant())))
            tier--;
        if (tier != GpuTier.Unknown) return (tier, false);
        // not in the list: a rough guess by video memory
        var guess = vramGb >= 12 ? GpuTier.High : vramGb >= r.VramGb[1] ? GpuTier.Mid : vramGb >= r.VramGb[0] ? GpuTier.Low : GpuTier.Unknown;
        return (guess, true);
    }

    public static PcResult Evaluate(HardwareInfo h, PcRequirements r, Modlist m, bool installed, DateTime now)
    {
        var rows = new List<PcRow>();
        var (tier, guessed) = Tier(h.Gpu, h.VramGb, r);
        var gpuName = h.Gpu.Length > 0 ? h.Gpu : Loc.T("pc.unknown");
        var vram = h.VramGb > 0 ? $" · {h.VramGb:0} {Loc.T("pc.gb")}" : "";

        // GPU
        var (gs, gd) = tier switch
        {
            GpuTier.High => (CheckState.Ok, Loc.T("pc.gpu.high")),
            GpuTier.Mid => (CheckState.Warning, Loc.T("pc.gpu.mid")),
            GpuTier.Low => (CheckState.Error, Loc.T("pc.gpu.low")),
            _ => (CheckState.Warning, Loc.T("pc.gpu.unknown")),
        };
        if (guessed && tier != GpuTier.Unknown) gd += " " + Loc.T("pc.guess");
        rows.Add(new PcRow(gs, gpuName + vram, gd + (r.GpuText.Length > 0 ? " " + Loc.F("pc.gpu.rec", r.GpuText) : ""), true));

        // video memory
        if (h.VramGb > 0)
        {
            var vs = h.VramGb + 0.25 < r.VramGb[0] ? CheckState.Error : h.VramGb + 0.25 < r.VramGb[1] ? CheckState.Warning : CheckState.Ok;
            if (vs != CheckState.Ok) rows.Add(new PcRow(vs, Loc.F("pc.vram", h.VramGb.ToString("0")), Loc.F("pc.vram.need", r.VramGb[1]), true));
        }

        // CPU
        var cs = h.Threads == 0 ? CheckState.Warning : h.Threads < r.Threads[0] ? CheckState.Error : h.Threads < r.Threads[1] ? CheckState.Warning : CheckState.Ok;
        rows.Add(new PcRow(cs, h.Cpu.Length > 0 ? h.Cpu : Loc.T("pc.unknown"),
            h.Threads > 0 ? Loc.F("pc.cpu", h.Cores, h.Threads) + (cs == CheckState.Ok ? "" : " " + Loc.T("pc.cpu.weak")) : Loc.T("pc.unknown"), true));

        // RAM (a "16 GB" PC reports a bit less)
        var ram = Math.Round(h.RamGb);
        var rs = h.RamGb == 0 ? CheckState.Warning : ram < r.RamGb[0] ? CheckState.Error : ram < r.RamGb[1] ? CheckState.Warning : CheckState.Ok;
        rows.Add(new PcRow(rs, Loc.F("pc.ram", ram), rs == CheckState.Ok ? Loc.T("pc.ok") : Loc.F("pc.ram.need", r.RamGb[1]), true,
            rs == CheckState.Ok ? null : Loc.T("pc.advice.close")));

        // disk
        var need = installed ? 0 : m.RequiredFreeBytes;
        var free = h.FreeBytes / (double)(1L << 30);
        var ds = h.FreeBytes == 0 ? CheckState.Warning : h.FreeBytes < need ? CheckState.Error : h.Ssd == false ? CheckState.Warning : CheckState.Ok;
        var diskText = Loc.F("pc.disk", h.Drive, free.ToString("0"), h.Ssd == true ? "SSD" : h.Ssd == false ? "HDD" : "?");
        var diskDetail = h.FreeBytes < need ? Loc.F("pc.disk.need", need / (1L << 30)) : h.Ssd == false ? Loc.T("pc.disk.hdd") : installed ? Loc.T("pc.disk.installed") : Loc.T("pc.ok");
        rows.Add(new PcRow(ds, diskText, diskDetail, false, h.FreeBytes < need ? Loc.T("pc.advice.space") : null, h.FreeBytes < need ? "wiztree" : null));

        // driver
        if (h.DriverDate is DateTime dd)
        {
            var days = (now - dd).TotalDays;
            var drs = days > 180 ? CheckState.Warning : CheckState.Ok;
            var soft = Regex.IsMatch(h.Gpu, "NVIDIA|GeForce", RegexOptions.IgnoreCase) ? "nvidia-app" : Regex.IsMatch(h.Gpu, "AMD|Radeon", RegexOptions.IgnoreCase) ? "amd-adrenalin" : null;
            rows.Add(new PcRow(drs, Loc.F("pc.driver", h.DriverVersion, dd.ToString("dd.MM.yyyy")),
                drs == CheckState.Ok ? Loc.T("pc.driver.fresh") : Loc.F("pc.driver.old", (int)(days / 30)), false,
                drs == CheckState.Ok ? null : Loc.T("pc.advice.driver"), drs == CheckState.Ok ? null : soft));
        }

        // screen
        if (h.ScreenWidth > 0)
        {
            var big = h.ScreenWidth >= 3840 ? "4K" : h.ScreenWidth >= 2560 ? "2K" : null;
            var ss = big != null && (tier != GpuTier.High || big == "4K") ? CheckState.Warning : CheckState.Ok;
            rows.Add(new PcRow(ss, Loc.F("pc.screen", h.ScreenWidth, h.ScreenHeight),
                ss == CheckState.Ok ? Loc.T("pc.ok") : Loc.F("pc.screen.big", big!), big != null && tier != GpuTier.High,
                ss == CheckState.Ok ? null : Loc.T("pc.advice.upscale")));
        }

        // Windows
        var ws = !h.Windows64 || h.WindowsBuild < 10240 ? CheckState.Error : CheckState.Ok;
        rows.Add(new PcRow(ws, h.WindowsBuild >= 22000 ? "Windows 11" : "Windows 10", ws == CheckState.Ok ? Loc.T("pc.ok") : Loc.T("pc.windows.need"), false));

        var verdict = rows.Any(x => x.State == CheckState.Error) ? CheckState.Error
            : rows.Any(x => x.State == CheckState.Warning && x.Performance) ? CheckState.Warning : CheckState.Ok;
        return new PcResult(verdict, rows, tier, guessed);
    }
}
