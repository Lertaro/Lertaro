using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Lertaro.Core.Services.Installation;

/// <summary>Stable, non-disclosing identifiers for the current Windows user and session.</summary>
public static class CurrentUserIdentity
{
    private static string Sid => WindowsIdentity.GetCurrent().User!.Value;

    public static string SidHash => Hash(Sid);

    public static string SessionHash => Hash($"{Sid}\0{Process.GetCurrentProcess().SessionId}");

    internal static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
