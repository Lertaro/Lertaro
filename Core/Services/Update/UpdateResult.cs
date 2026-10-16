using System.Text.Json;

namespace Lertaro.Core.Services.Update;

/// <summary>
/// The updater script's own verdict on the last copy it attempted, as it left it beside the installation.
/// </summary>
/// <remarks>
/// <c>portable-updater.ps1</c> writes this on every run, success or failure, and nothing read it. An update
/// that died between "the App exited" and "the files were replaced" therefore looked exactly like nothing
/// having happened -- the report in issue #354: the App quit, the version was unchanged, and there was a lock
/// file and a payload left in the install directory -- while the reason sat in this file the whole time.
///
/// The App reads it at startup and shows it once. It cannot delete it: an installation directory is closed to
/// Users for writing, which is also what makes the file trustworthy enough to show. What "shown once" means
/// is remembered in user settings instead.
/// </remarks>
public sealed class UpdateResult
{
    public const string FileName = "update-result.json";

    /// <summary>How old a verdict may be and still be shown: an ancient leftover is history, not news.</summary>
    internal static readonly TimeSpan FreshFor = TimeSpan.FromDays(7);

    // An installation directory is closed to Users only for an INSTALLED copy: a portable one is the user's own
    // directory, where any process of that user can write anything. So neither the file nor the text taken from
    // it is trusted to be small.
    private const long MaximumBytes = 64 * 1024;
    private const int MaximumMessageLength = 1000;

    /// <summary>
    /// One instance: a freshly constructed <see cref="JsonSerializerOptions"/> carries no cached contract
    /// metadata, so every read would re-derive the serializer.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public string? Status { get; set; }
    public DateTimeOffset Time { get; set; }
    public string? Message { get; set; }
    public string? RecoveryDirectory { get; set; }

    public bool Succeeded => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The verdict beside <paramref name="applicationDirectory"/>, or null when there is none to act on:
    /// absent, unreadable, not JSON, or lacking the timestamp that makes it a verdict at all. Never throws --
    /// this is read while the App is still starting.
    /// </summary>
    public static UpdateResult? ReadIfPresent(string applicationDirectory)
    {
        try
        {
            var path = Path.Combine(applicationDirectory, FileName);
            if (!File.Exists(path)) return null;

            // A verdict is a few hundred bytes of JSON. Anything much larger than that is not one, and reading
            // it in full to find that out is exactly what a portable install's own user could arrange.
            var length = new FileInfo(path).Length;
            if (length > MaximumBytes)
            {
                Logger.Log($"[UpdateResult] {FileName} is {length} bytes; refusing to read it as a verdict.", LogLevel.Warn);
                return null;
            }

            // ReadAllText drops the BOM an older PowerShell host writes, which a raw byte parse would choke on.
            var result = JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(path), ReadOptions);
            if (result?.Time is not { } time || time <= DateTimeOffset.MinValue) return null;

            // Clamped, because this text goes straight into a dialog.
            if (result.Message is { Length: > MaximumMessageLength } message)
                result.Message = message[..MaximumMessageLength] + "…";
            return result;
        }
        catch (Exception ex)
        {
            Logger.Log($"[UpdateResult] Could not read {FileName}: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }
}
