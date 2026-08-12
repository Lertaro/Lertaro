using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.SpaceAnalyzer;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Application = System.Windows.Application;
using Cursors = System.Windows.Input.Cursors;

namespace Lertaro.App.Views.SpaceAnalyzer;

internal static class SpaceTreemapPresenter
{
    // ponytail: group everything beyond 240 blocks into one proportional "Other" tile. More visual
    // elements stop being readable anyway; a zoomable virtual canvas is the upgrade path if needed.
    private const int MaximumTiles = 240;

    public static void Render(Canvas canvas, IReadOnlyList<SpaceDisplayItem> items,
        SpaceDisplayItem? selected, Action<SpaceDisplayItem> select, Action<SpaceDisplayItem> open,
        Action<SpaceDisplayItem> showActions)
    {
        canvas.Children.Clear();
        if (canvas.ActualWidth <= 1 || canvas.ActualHeight <= 1)
            return;

        var positive = items.Where(item => item.Size > 0).ToList();
        var tiles = positive.Take(MaximumTiles).Select(item => new Tile(item, item.Name, item.Size)).ToList();
        if (positive.Count > MaximumTiles)
        {
            var otherSize = positive.Skip(MaximumTiles).Aggregate(0L, static (sum, item) => SaturatingAdd(sum, item.Size));
            tiles.Add(new Tile(null, TranslationManager.Instance["Space_Other"], otherSize));
        }

        var largestSize = tiles.Count == 0 ? 0 : tiles.Max(tile => tile.Size);
        var boxes = TreemapLayout.Calculate(tiles.Select(tile => (double)tile.Size).ToList(), canvas.ActualWidth, canvas.ActualHeight);
        for (var index = 0; index < boxes.Count; index++)
        {
            var box = boxes[index];
            var tile = tiles[box.Index];
            canvas.Children.Add(CreateTile(tile, box, largestSize, selected, select, open, showActions));
        }
    }

    private static Border CreateTile(Tile tile, TreemapRect box, long largestSize,
        SpaceDisplayItem? selected, Action<SpaceDisplayItem> select, Action<SpaceDisplayItem> open,
        Action<SpaceDisplayItem> showActions)
    {
        const double gap = 2;
        var color = TileColor(tile.Size, largestSize);
        var isDirectory = tile.Item?.IsDirectory == true;
        var outline = (Application.Current.TryFindResource("TextPrimary") as SolidColorBrush)?.Color ?? Colors.White;
        var label = CreateLabel(tile, box.Width, box.Height, color);
        var border = new Border
        {
            Width = Math.Max(0, box.Width - gap),
            Height = Math.Max(0, box.Height - gap),
            CornerRadius = new CornerRadius(Math.Min(10, Math.Max(2, Math.Min(box.Width, box.Height) / 8))),
            Background = new SolidColorBrush(color),
            BorderBrush = new SolidColorBrush(Color.FromArgb(isDirectory ? (byte)150 : (byte)64, outline.R, outline.G, outline.B)),
            BorderThickness = tile.Item != null && ReferenceEquals(tile.Item, selected)
                ? new Thickness(3)
                : new Thickness(isDirectory ? 2 : 1),
            Cursor = tile.Item == null ? Cursors.Arrow : Cursors.Hand,
            Child = label
        };
        TrimmedTextToolTip.SetText(border, $"{tile.Name}{Environment.NewLine}{SpaceSizeFormatter.Format(tile.Size)}");
        Canvas.SetLeft(border, box.X + gap / 2);
        Canvas.SetTop(border, box.Y + gap / 2);

        if (tile.Item != null)
        {
            var opening = false;
            border.MouseLeftButtonUp += (_, e) =>
            {
                if (!opening)
                    select(tile.Item);
                opening = false;
                e.Handled = true;
            };
            border.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2 && tile.Item.IsDirectory)
                {
                    opening = true;
                    open(tile.Item);
                    e.Handled = true;
                }
            };
            border.MouseRightButtonUp += (_, e) =>
            {
                select(tile.Item);
                showActions(tile.Item);
                e.Handled = true;
            };
        }
        return border;
    }

    private static UIElement? CreateLabel(Tile tile, double width, double height, Color color)
    {
        if (width < 58 || height < 32)
            return null;
        var panel = new StackPanel { Margin = new Thickness(9, 7, 7, 5) };
        var name = new TextBlock
        {
            Text = tile.Name,
            FontSize = width > 150 && height > 70 ? 13 : 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = ContrastBrush(color),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock
        {
            Text = SpaceSizeFormatter.Format(tile.Size),
            FontSize = 10,
            Opacity = 0.82,
            Foreground = name.Foreground,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = width >= 92 && height >= 54 ? Visibility.Visible : Visibility.Collapsed
        });
        return panel;
    }

    private static Color TileColor(long size, long largestSize)
    {
        var accent = (Application.Current.TryFindResource("AccentBlue") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var background = (Application.Current.TryFindResource("ContentBg") as SolidColorBrush)?.Color ?? Colors.White;
        return Blend(background, accent, CalculateAccentAmount(size, largestSize));
    }

    // Normalize each visible item's share against the largest visible share. The square root keeps
    // small items distinguishable instead of compressing almost every tile into the darkest shade.
    internal static double CalculateAccentAmount(long size, long largestSize)
    {
        const double minimum = 0.52;
        const double range = 0.38;
        var ratio = largestSize > 0 ? Math.Clamp((double)size / largestSize, 0, 1) : 0;
        return minimum + Math.Sqrt(ratio) * range;
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * amount),
        (byte)(from.G + (to.G - from.G) * amount),
        (byte)(from.B + (to.B - from.B) * amount));

    private static Brush ContrastBrush(Color color)
        => new SolidColorBrush((color.R * 299 + color.G * 587 + color.B * 114) / 1000 >= 150 ? Colors.Black : Colors.White);

    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;

    private sealed record Tile(SpaceDisplayItem? Item, string Name, long Size);
}
