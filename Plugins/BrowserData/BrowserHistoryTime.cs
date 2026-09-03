using System.Globalization;

namespace Lertaro.Plugins.BrowserData;

// Converts the browser-specific timestamp units once at the reader boundary so the rest of the
// plugin can display one consistent local date and time.
internal static class BrowserHistoryTime
{
    private static readonly DateTimeOffset ChromiumEpoch = new(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset? FromChromium(long timestamp)
    {
        try
        {
            return ChromiumEpoch.AddTicks(checked(timestamp * 10));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static DateTimeOffset? FromFirefox(long timestamp)
    {
        try
        {
            return DateTimeOffset.UnixEpoch.AddTicks(checked(timestamp * 10));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static string Format(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
}
