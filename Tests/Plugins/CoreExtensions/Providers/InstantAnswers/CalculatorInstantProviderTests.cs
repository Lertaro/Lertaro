using System.Globalization;
using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

[TestClass]
public sealed class CalculatorInstantProviderTests
{
    private static readonly CalculatorInstantProvider Provider = new();

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_SimpleExpression_ReturnsCopyResultWithComputedValue()
    {
        var result = Provider.GetInstantResults("2+2").Single();

        Assert.AreEqual("2+2 = 4", result.Title);
        Assert.AreEqual("Copy", result.ActionType);
        Assert.AreEqual("4", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_ThousandsSeparators_ReturnsComputedValue()
    {
        var result = Provider.GetInstantResults("1,000 + 2,000").Single();

        Assert.AreEqual("1,000 + 2,000 = 3000", result.Title);
        Assert.AreEqual("3000", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_PureAlphabeticNonConstantText_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("excel"));

    [TestMethod]
    public void GetInstantResults_PiAlone_IsTreatedAsExpression()
    {
        var result = Provider.GetInstantResults("pi").Single();

        Assert.StartsWith("pi = 3.14159", result.Title);
    }

    [TestMethod]
    public void GetInstantResults_NoDigitsOrConstants_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("+-*/"));

    [TestMethod]
    public void GetInstantResults_IncompleteExpression_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("2+"));

    [TestMethod]
    public void GetInstantResults_ResultEqualsInput_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("5"));

    [TestMethod]
    public void GetInstantResults_IntegerResult_HasNoDecimalPoint()
    {
        var result = Provider.GetInstantResults("4*3").Single();

        Assert.AreEqual("4*3 = 12", result.Title);
    }

    [TestMethod]
    public void GetInstantResults_HexToDecConversion_ReturnsConvertedValue()
    {
        var result = Provider.GetInstantResults("0xFF to dec").Single();

        Assert.AreEqual("255", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_DecToHexConversion_ReturnsConvertedValue()
    {
        var result = Provider.GetInstantResults("255 to hex").Single();

        Assert.AreEqual("0xFF", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_DecToBinConversion_ReturnsConvertedValue()
    {
        var result = Provider.GetInstantResults("5 to bin").Single();

        Assert.AreEqual("0b101", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_DecToOctConversion_ReturnsConvertedValue()
    {
        var result = Provider.GetInstantResults("8 to oct").Single();

        Assert.AreEqual("010", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_DecimalToDecConversion_UnderCommaDecimalCulture_UsesInvariantDecimalSeparator()
    {
        // de-DE uses ',' as decimal separator; both the parse and the "dec" output must
        // stay invariant, otherwise "1.5" would read as 15 and format back as "1,5".
        // CurrentCulture is per-thread (AsyncLocal), so setting and restoring it here
        // cannot leak into parallel test methods on other threads.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var result = Provider.GetInstantResults("1.5 to dec").Single();

            Assert.AreEqual("1.5", result.ActionArgument);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void GetHighlightMask_EmptyQuery_ReturnsNull() => Assert.IsNull(Provider.GetHighlightMask("2+2 = 4", ""));

    [TestMethod]
    public void GetHighlightMask_TextStartsWithQuery_HighlightsThatPrefix()
    {
        var mask = Provider.GetHighlightMask("2+2 = 4", "2+2");

        Assert.IsNotNull(mask);
        for (var i = 0; i < mask.Length; i++)
            Assert.AreEqual(i < 3, mask[i]);
    }
}
