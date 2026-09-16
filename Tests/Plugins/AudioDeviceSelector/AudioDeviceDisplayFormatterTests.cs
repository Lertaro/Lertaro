using Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

namespace Lertaro.Plugins.AudioDeviceSelector.Tests;

[TestClass]
public sealed class AudioDeviceDisplayFormatterTests
{
    [TestMethod]
    public void SplitFriendlyName_WithParenthesizedDescription_ReturnsBothParts()
    {
        var result = AudioDeviceDisplayFormatter.SplitFriendlyName("Speakers (USB Audio)");

        Assert.AreEqual("Speakers", result.Name);
        Assert.AreEqual("USB Audio", result.Description);
    }

    [TestMethod]
    public void SplitFriendlyName_WithoutDescription_KeepsFullName()
    {
        var result = AudioDeviceDisplayFormatter.SplitFriendlyName("Speakers");

        Assert.AreEqual("Speakers", result.Name);
        Assert.AreEqual(string.Empty, result.Description);
    }

    [TestMethod]
    public void Format_DeviceDescription_UsesDescriptionAsTitle()
    {
        var result = AudioDeviceDisplayFormatter.Format(
            "Speakers (USB Audio)", AudioDeviceDisplayMode.DeviceDescription);

        Assert.AreEqual("USB Audio", result.Title);
        Assert.AreEqual("Speakers", result.Description);
    }

    [TestMethod]
    public void TryParseQuery_OnlyAcceptsKeywordOrKeywordWithTerm()
    {
        Assert.IsTrue(AudioDeviceSelectorInstantProvider.TryParseQuery("ad", "ad", out var emptyTerm));
        Assert.AreEqual(string.Empty, emptyTerm);
        Assert.IsTrue(AudioDeviceSelectorInstantProvider.TryParseQuery("AD speakers", "ad", out var term));
        Assert.AreEqual("speakers", term);
        Assert.IsFalse(AudioDeviceSelectorInstantProvider.TryParseQuery("adapter", "ad", out _));
    }

    [TestMethod]
    public void DefaultDevice_IsTheOnlyOneWithADistinctOutputIcon()
    {
        // Which icon/colour a row gets is one ternary in the provider, so pinning the literal strings would
        // only restate it. What is worth pinning is the INVARIANT: "default" and "input" are the two axes
        // that must stay visually separable, and only "default" changes the output icon.
        Assert.AreNotEqual(
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Output, false),
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Output, true));
        Assert.AreEqual(
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Output, true),
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Input, true));
    }
}
