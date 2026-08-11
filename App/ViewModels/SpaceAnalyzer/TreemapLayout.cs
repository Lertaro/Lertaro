namespace Lertaro.App.ViewModels.SpaceAnalyzer;

internal readonly record struct TreemapRect(int Index, double X, double Y, double Width, double Height);

internal static class TreemapLayout
{
    public static IReadOnlyList<TreemapRect> Calculate(IReadOnlyList<double> weights, double width, double height)
    {
        if (width <= 0 || height <= 0 || weights.Count == 0)
            return Array.Empty<TreemapRect>();

        var positive = weights.Select((weight, index) => (Weight: Math.Max(0, weight), Index: index))
            .Where(item => item.Weight > 0)
            .OrderByDescending(item => item.Weight)
            .ToList();
        var total = positive.Sum(item => item.Weight);
        if (total <= 0)
            return Array.Empty<TreemapRect>();

        var areas = positive.Select(item => (Area: item.Weight / total * width * height, item.Index)).ToList();
        var result = new List<TreemapRect>(areas.Count);
        var remaining = new LayoutArea(0, 0, width, height);
        var row = new List<(double Area, int Index)>();
        var position = 0;

        while (position < areas.Count)
        {
            var next = areas[position];
            var shortSide = Math.Min(remaining.Width, remaining.Height);
            if (row.Count == 0 || Worst(row.Append(next), shortSide) <= Worst(row, shortSide))
            {
                row.Add(next);
                position++;
                continue;
            }

            remaining = PlaceRow(row, remaining, result);
            row.Clear();
        }

        if (row.Count > 0)
            PlaceRow(row, remaining, result);
        return result;
    }

    private static double Worst(IEnumerable<(double Area, int Index)> row, double side)
    {
        var items = row.ToList();
        if (items.Count == 0 || side <= 0)
            return double.PositiveInfinity;
        var sum = items.Sum(item => item.Area);
        var max = items.Max(item => item.Area);
        var min = items.Min(item => item.Area);
        var sideSquared = side * side;
        var sumSquared = sum * sum;
        return Math.Max(sideSquared * max / sumSquared, sumSquared / (sideSquared * min));
    }

    private static LayoutArea PlaceRow(List<(double Area, int Index)> row, LayoutArea area, List<TreemapRect> result)
    {
        var sum = row.Sum(item => item.Area);
        if (area.Width >= area.Height)
        {
            var rowWidth = area.Height <= 0 ? 0 : sum / area.Height;
            var y = area.Y;
            foreach (var item in row)
            {
                var itemHeight = rowWidth <= 0 ? 0 : item.Area / rowWidth;
                result.Add(new TreemapRect(item.Index, area.X, y, rowWidth, itemHeight));
                y += itemHeight;
            }
            return new LayoutArea(area.X + rowWidth, area.Y, Math.Max(0, area.Width - rowWidth), area.Height);
        }

        var rowHeight = area.Width <= 0 ? 0 : sum / area.Width;
        var x = area.X;
        foreach (var item in row)
        {
            var itemWidth = rowHeight <= 0 ? 0 : item.Area / rowHeight;
            result.Add(new TreemapRect(item.Index, x, area.Y, itemWidth, rowHeight));
            x += itemWidth;
        }
        return new LayoutArea(area.X, area.Y + rowHeight, area.Width, Math.Max(0, area.Height - rowHeight));
    }

    private readonly record struct LayoutArea(double X, double Y, double Width, double Height);
}
