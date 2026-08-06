using Lertaro.App.Views.SearchWindow;

namespace Lertaro.App.Tests.Views.SearchWindow;

[TestClass]
public sealed class SearchWindowInputHandlerResolveColumnIdAtXTests
{
    private static readonly (string ColumnId, double Width)[] Columns =
    {
        ("Name", 320),
        ("Path", 260),
        ("DateModified", 120),
    };

    [TestMethod]
    public void ResolveColumnIdAtX_XWithinFirstColumn_ReturnsFirstColumnId() =>
        Assert.AreEqual("Name", SearchWindowInputHandler.ResolveColumnIdAtX(150, Columns));

    [TestMethod]
    public void ResolveColumnIdAtX_XJustPastFirstColumnBoundary_ReturnsSecondColumnId() =>
        Assert.AreEqual("Path", SearchWindowInputHandler.ResolveColumnIdAtX(320.5, Columns));

    [TestMethod]
    public void ResolveColumnIdAtX_XExactlyAtBoundary_ReturnsSecondColumnId() =>
        // x < cumulativeWidth, not <=, so landing exactly on the boundary belongs to the NEXT column.
        Assert.AreEqual("Path", SearchWindowInputHandler.ResolveColumnIdAtX(320, Columns));

    [TestMethod]
    public void ResolveColumnIdAtX_XWithinLastColumn_ReturnsLastColumnId() =>
        Assert.AreEqual("DateModified", SearchWindowInputHandler.ResolveColumnIdAtX(650, Columns));

    [TestMethod]
    public void ResolveColumnIdAtX_XPastAllColumns_ReturnsNull() =>
        Assert.IsNull(SearchWindowInputHandler.ResolveColumnIdAtX(1000, Columns));

    [TestMethod]
    public void ResolveColumnIdAtX_NoColumns_ReturnsNull() =>
        Assert.IsNull(SearchWindowInputHandler.ResolveColumnIdAtX(50, Array.Empty<(string, double)>()));

    [TestMethod]
    public void ResolveColumnIdAtX_NegativeX_ReturnsFirstColumnId() =>
        Assert.AreEqual("Name", SearchWindowInputHandler.ResolveColumnIdAtX(-10, Columns));
}
