using System.Globalization;
using System.Windows;
using Lertaro.App.Converters;

namespace Lertaro.App.Tests.Converters;

[TestClass]
public sealed class BoolToVisibilityConverterTests
{
    [TestMethod]
    public void Convert_True_ReturnsVisible() =>
        Assert.AreEqual(Visibility.Visible, new BoolToVisibilityConverter().Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void Convert_False_ReturnsCollapsed() =>
        Assert.AreEqual(Visibility.Collapsed, new BoolToVisibilityConverter().Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void Convert_NonBoolValue_TreatedAsFalse() =>
        Assert.AreEqual(Visibility.Collapsed, new BoolToVisibilityConverter().Convert("x", typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void Convert_InvertProperty_FlipsResult() =>
        Assert.AreEqual(Visibility.Collapsed, new BoolToVisibilityConverter { Invert = true }.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void Convert_InvertParameter_FlipsResult() =>
        Assert.AreEqual(Visibility.Collapsed, new BoolToVisibilityConverter().Convert(true, typeof(Visibility), "Invert", CultureInfo.InvariantCulture));

    [TestMethod]
    public void ConvertBack_Visible_ReturnsTrue() =>
        Assert.IsTrue((bool)new BoolToVisibilityConverter().ConvertBack(Visibility.Visible, typeof(bool), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void ConvertBack_Collapsed_ReturnsFalse() =>
        Assert.IsFalse((bool)new BoolToVisibilityConverter().ConvertBack(Visibility.Collapsed, typeof(bool), null!, CultureInfo.InvariantCulture));
}

[TestClass]
public sealed class StringToVisibilityConverterTests
{
    private static readonly StringToVisibilityConverter Converter = new();

    [TestMethod]
    public void Convert_NonEmptyString_ReturnsVisible() =>
        Assert.AreEqual(Visibility.Visible, Converter.Convert("text", typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void Convert_EmptyOrWhitespaceString_ReturnsCollapsed()
    {
        Assert.AreEqual(Visibility.Collapsed, Converter.Convert("", typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.AreEqual(Visibility.Collapsed, Converter.Convert("   ", typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Convert_Null_ReturnsCollapsed() =>
        Assert.AreEqual(Visibility.Collapsed, Converter.Convert(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void Convert_NonStringValue_ReturnsCollapsed() =>
        Assert.AreEqual(Visibility.Collapsed, Converter.Convert(42, typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void ConvertBack_Throws() =>
        Assert.ThrowsExactly<NotImplementedException>(() => Converter.ConvertBack(Visibility.Visible, typeof(string), null!, CultureInfo.InvariantCulture));
}

[TestClass]
public sealed class NullToVisibilityConverterTests
{
    private static readonly NullToVisibilityConverter Converter = new();

    [TestMethod]
    public void Convert_NonNull_ReturnsVisible() =>
        Assert.AreEqual(Visibility.Visible, Converter.Convert(new object(), typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void Convert_Null_ReturnsCollapsed() =>
        Assert.AreEqual(Visibility.Collapsed, Converter.Convert(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void ConvertBack_Throws() =>
        Assert.ThrowsExactly<NotImplementedException>(() => Converter.ConvertBack(Visibility.Visible, typeof(object), null!, CultureInfo.InvariantCulture));
}

[TestClass]
public sealed class ReferenceEqualsConverterTests
{
    private static readonly ReferenceEqualsConverter Converter = new();

    [TestMethod]
    public void Convert_SameReference_ReturnsTrue()
    {
        var obj = new object();

        Assert.IsTrue((bool)Converter.Convert(new[] { obj, obj }, typeof(bool), null!, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Convert_DifferentReferences_ReturnsFalse() =>
        Assert.IsFalse((bool)Converter.Convert(new[] { new object(), new object() }, typeof(bool), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void Convert_WrongArrayLength_ReturnsFalse() =>
        Assert.IsFalse((bool)Converter.Convert(new[] { new object() }, typeof(bool), null!, CultureInfo.InvariantCulture));

    [TestMethod]
    public void ConvertBack_Throws() =>
        Assert.ThrowsExactly<NotImplementedException>(() => Converter.ConvertBack(true, new[] { typeof(object), typeof(object) }, null!, CultureInfo.InvariantCulture));
}
