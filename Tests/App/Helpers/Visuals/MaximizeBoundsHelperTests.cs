using System.Windows;
using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass]
public sealed class MaximizeBoundsHelperTests
{
    [StaTestMethod]
    public void Attach_NullWindow_ThrowsArgumentNullException() => Assert.ThrowsExactly<ArgumentNullException>(() => MaximizeBoundsHelper.Attach(null!));

}
