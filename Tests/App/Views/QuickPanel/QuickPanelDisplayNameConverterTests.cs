using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lertaro.App.Views.QuickPanel;

namespace Lertaro.App.Tests.Views.QuickPanel;

[TestClass]
public class QuickPanelDisplayNameConverterTests
{
    private readonly QuickPanelDisplayNameConverter _nameConverter = new();
    private readonly QuickPanelRelativeDirectoryConverter _dirConverter = new();

    [TestMethod]
    public void GetRelativeDirectory_StripsGroupFolderPrefix()
    {
        var relative = QuickPanelPathHelper.GetRelativeDirectory(@"D:\Dev\cs\Lertaro\Src\App", @"D:\Dev\cs\Lertaro");
        Assert.AreEqual(@"Src\App", relative);
    }

    [TestMethod]
    public void GetRelativeDirectory_WhenDirectChildOfGroupFolder_ReturnsEmptyString()
    {
        var relative = QuickPanelPathHelper.GetRelativeDirectory(@"D:\Dev\cs\Lertaro", @"D:\Dev\cs\Lertaro");
        Assert.AreEqual(string.Empty, relative);
    }

    [TestMethod]
    public void Convert_MultiBinding_WhenSubfolderItem_AppendsRelativeDir()
    {
        var item = new AppSearchResult
        {
            Name = "App.csproj",
            ParentDir = @"D:\Dev\cs\Lertaro\Src\App"
        };
        var values = new object[] { item, @"D:\Dev\cs\Lertaro" };

        var result = _nameConverter.Convert(values, typeof(string), parameter: null, culture: CultureInfo.InvariantCulture);

        Assert.AreEqual(@"App.csproj (Src\App)", result);
    }

    [TestMethod]
    public void Convert_MultiBinding_WhenDirectItem_ReturnsNameOnly()
    {
        var item = new AppSearchResult
        {
            Name = "README.md",
            ParentDir = @"D:\Dev\cs\Lertaro"
        };
        var values = new object[] { item, @"D:\Dev\cs\Lertaro" };

        var result = _nameConverter.Convert(values, typeof(string), parameter: null, culture: CultureInfo.InvariantCulture);

        Assert.AreEqual("README.md", result);
    }

    [TestMethod]
    public void RelativeDirectoryConverter_WhenDirectItem_ReturnsNull()
    {
        var item = new AppSearchResult
        {
            Name = "README.md",
            ParentDir = @"D:\Dev\cs\Lertaro"
        };
        var values = new object[] { item, @"D:\Dev\cs\Lertaro" };

        var result = _dirConverter.Convert(values, typeof(string), parameter: null, culture: CultureInfo.InvariantCulture);

        Assert.IsNull(result);
    }
}


