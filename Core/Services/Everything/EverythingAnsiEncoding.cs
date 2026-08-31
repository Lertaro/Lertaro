using System.Globalization;
using System.Text;

namespace Lertaro.Core.Services.Everything;

/// <summary>
/// The system ANSI codepage used by Everything's ANSI ('A') WM_COPYDATA IPC variants.
/// Encoding.Default is UTF-8 on modern .NET and is NOT the system ANSI codepage, so the
/// 'A' variants must resolve their codepage explicitly (everything_ipc.h documents the
/// 'A' variants as CHAR/ANSI and UTF-8 only for EVERYTHING_IPC_COPYDATA_COMMAND_LINE_UTF8).
/// </summary>
internal static class EverythingAnsiEncoding
{
    public static Encoding Instance { get; } = Create();

    private static Encoding Create()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
    }
}
