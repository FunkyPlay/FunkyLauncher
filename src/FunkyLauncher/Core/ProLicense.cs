// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FunkyLauncher.Core;

/// <summary>What a Funky Pro key says (signed by FunkyPlay, checked offline).</summary>
public sealed record ProKeyInfo(string Id, string To, string Plan, DateTime From, DateTime Until);

public enum ProKeyStatus { None, Valid, Expired, Invalid }

/// <summary>
/// Funky Pro keys: "FPRO-&lt;payload&gt;.&lt;signature&gt;". The payload (who, plan, valid until) is signed with
/// FunkyPlay's secret ECDSA P-256 key, which never leaves Funky's PC. The launcher only holds the PUBLIC key,
/// so it can check a key offline but can't create one. Keys are short-lived (a month); renewing = a new key.
/// </summary>
public static class ProLicense
{
    /// <summary>FunkyPlay's public key (SubjectPublicKeyInfo, base64). Safe to publish.</summary>
    private const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEbuheLqh4e4duo8ZI6fSBy/+7TAK0RxzS/C8XQW1lu6h+emeuqJKzQZiRgrFBcTGSmanMWzmtAPnMcLU7hcmNoQ==";

    public static (ProKeyStatus Status, ProKeyInfo? Info) Check(string? key, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(key)) return (ProKeyStatus.None, null);
        try
        {
            key = key.Trim();
            if (!key.StartsWith("FPRO-", StringComparison.Ordinal)) return (ProKeyStatus.Invalid, null);
            var parts = key[5..].Split('.');
            if (parts.Length != 2) return (ProKeyStatus.Invalid, null);
            var payload = FromB64(parts[0]);
            var sig = FromB64(parts[1]);
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
            if (!ec.VerifyData(payload, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return (ProKeyStatus.Invalid, null);

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(payload));
            var r = doc.RootElement;
            var info = new ProKeyInfo(
                r.GetProperty("id").GetString() ?? "",
                r.GetProperty("to").GetString() ?? "",
                r.GetProperty("plan").GetString() ?? "",
                DateTime.Parse(r.GetProperty("from").GetString()!),
                DateTime.Parse(r.GetProperty("until").GetString()!));
            if (info.Plan != "pro") return (ProKeyStatus.Invalid, null);
            return (nowUtc.Date <= info.Until.Date ? ProKeyStatus.Valid : ProKeyStatus.Expired, info);
        }
        catch { return (ProKeyStatus.Invalid, null); }
    }

    /// <summary>Is Pro active right now for the saved key?</summary>
    public static bool Active(Settings s) => Check(s.ProKey, DateTime.UtcNow).Status == ProKeyStatus.Valid;

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
