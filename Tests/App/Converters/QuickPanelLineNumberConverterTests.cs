using System.Globalization;
using Lertaro.App.Converters;

namespace Lertaro.App.Tests.Converters;

[TestClass]
public sealed class QuickPanelLineNumberConverterTests
{
    [TestMethod]
    public void Format_PadsToTheNumberOfDigitsInTheGroup()
    {
        Assert.AreEqual("01", QuickPanelLineNumberConverter.Format(1, 27, CultureInfo.InvariantCulture));
        Assert.AreEqual("27", QuickPanelLineNumberConverter.Format(27, 27, CultureInfo.InvariantCulture));
        Assert.AreEqual("001", QuickPanelLineNumberConverter.Format(1, 126, CultureInfo.InvariantCulture));
    }
}
