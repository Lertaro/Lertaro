using Lertaro.Core.IndexV2.Space;

namespace Lertaro.App.ViewModels.SpaceAnalyzer;

internal sealed class SpaceDisplayItem
{
    public required IndexedSpaceSource Source { get; init; }
    public required IndexedSpaceEntry Entry { get; init; }
    public string Name => Entry.Name;
    public long Size => Entry.Size;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsHardLinkDuplicate => Entry.IsHardLinkDuplicate;
    public string DisplaySize => SpaceSizeFormatter.Format(Size);
    public string ToolTipText => $"{Name}{Environment.NewLine}{DisplaySize}";
}

internal static class SpaceSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        var value = Math.Max(0, bytes);
        var scaled = (double)value;
        var unit = 0;
        while (scaled >= 1024 && unit < Units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value} {Units[unit]}" : $"{scaled:0.##} {Units[unit]}";
    }
}
