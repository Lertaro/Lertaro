using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Lertaro.Plugins.Tessoa;

internal static class TessoaQuery
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string? Read(TessoaWindow window, string operation) =>
        QueryAsync($"tessoa.q.{window.Pid}", window.Handle, operation).GetAwaiter().GetResult();

    internal static async Task<string?> QueryAsync(string pipeName, nint hwnd, string operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(200, timeout.Token).ConfigureAwait(false);
            var body = Utf8.GetBytes(FormattableString.Invariant($"v 1\nop {operation}\nhwnd {hwnd.ToInt64()}\n"));
            var frame = new byte[4 + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
            body.CopyTo(frame, 4);
            await pipe.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
            var header = new byte[4];
            await pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is < 0 or > 1024 * 1024) return null;
            var response = new byte[length];
            await pipe.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
            return Utf8.GetString(response);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException
            or UnauthorizedAccessException or DecoderFallbackException) { return null; }
    }

    internal static bool IsPath(string? path) => !string.IsNullOrEmpty(path) && path.IndexOfAny(['\0', '\r', '\n']) < 0
        && Path.IsPathFullyQualified(path);

    internal static bool SupportsTabs(string? response)
    {
        if (response == null) return false;
        var header = response.AsSpan();
        var newline = header.IndexOf('\n');
        if (newline >= 0) header = header[..newline];
        return header.StartsWith("api ", StringComparison.Ordinal)
            && int.TryParse(header[4..].TrimEnd('\r'), NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 2;
    }

    internal static IEnumerable<string> TabPaths(string response)
    {
        if (!SupportsTabs(response)) yield break;
        foreach (var line in response.Split('\n').Skip(1))
        {
            var fields = line.TrimEnd('\r').Split(' ', 4);
            if (fields is ["tab", var id, "c" or "v" or "b", var path]
                && ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _) && IsPath(path))
                yield return path;
        }
    }
}
