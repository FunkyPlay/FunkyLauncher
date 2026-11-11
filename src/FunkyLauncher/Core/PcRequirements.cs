// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Text.Json.Serialization;

namespace FunkyLauncher.Core;

/// <summary>
/// "Can my PC run it?" requirements of a modlist, from catalog.json. Pairs are [minimum, recommended].
/// GPU tiers are name fragments (case-insensitive); the LONGEST matching fragment wins, so
/// "RTX 4060 Ti" (high) beats "RTX 4060" (mid) and "RTX 2060 SUPER" (mid) beats "RTX 2060" (low).
/// </summary>
public sealed class PcRequirements
{
    [JsonPropertyName("ramGb")] public double[] RamGb { get; set; } = { 12, 16 };
    [JsonPropertyName("vramGb")] public double[] VramGb { get; set; } = { 6, 8 };
    [JsonPropertyName("threads")] public int[] Threads { get; set; } = { 4, 8 };
    [JsonPropertyName("gpuHigh")] public List<string> GpuHigh { get; set; } = new();
    [JsonPropertyName("gpuMid")] public List<string> GpuMid { get; set; } = new();
    [JsonPropertyName("gpuLow")] public List<string> GpuLow { get; set; } = new();
    /// <summary>Laptop GPUs drop one tier, except these (strong enough as laptop chips).</summary>
    [JsonPropertyName("laptopKeep")] public List<string> LaptopKeep { get; set; } = new();
    /// <summary>The recommended GPUs as text for the player, e.g. "RTX 2060 Super / RX 5700 XT / Arc A770".</summary>
    [JsonPropertyName("gpuText")] public string GpuText { get; set; } = "";
}
