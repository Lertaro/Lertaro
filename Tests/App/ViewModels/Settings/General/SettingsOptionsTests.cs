using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class LabeledOptionTests
{
    [TestMethod]
    public void Constructor_SetsValueAndLabel()
    {
        var option = new LabeledOption("v1", "Label 1");

        Assert.AreEqual("v1", option.Value);
        Assert.AreEqual("Label 1", option.Label);
    }

    [TestMethod]
    public void Label_Set_RaisesPropertyChanged()
    {
        var option = new LabeledOption("v1", "Old");
        var raised = false;
        option.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(option.Label)) raised = true; };

        option.Label = "New";

        Assert.IsTrue(raised);
        Assert.AreEqual("New", option.Label);
    }

    [TestMethod]
    public void ToString_ReturnsLabel() =>
        Assert.AreEqual("My Label", new LabeledOption("v", "My Label").ToString());
}

[TestClass]
public sealed class LanguageOptionTests
{
    [TestMethod]
    public void ToString_ReturnsLabel() =>
        Assert.AreEqual("English", new LanguageOption("en-US", "English").ToString());

    [TestMethod]
    public void GetLanguageDisplayName_ValidCulture_ReturnsCapitalizedNativeName()
    {
        var name = LanguageOption.GetLanguageDisplayName("en-US");

        Assert.AreEqual(char.ToUpper(name[0]), name[0]);
        Assert.IsNotEmpty(name);
    }

    [TestMethod]
    public void GetLanguageDisplayName_InvalidCultureCode_ReturnsCodeUnchanged() =>
        // .NET's culture parser is lenient -- it synthesizes a custom culture from most BCP-47-shaped
        // strings rather than throwing, so this needs a string with characters that are actually invalid
        // in a culture name (not just an unrecognized one) to hit the catch-and-fall-back branch.
        Assert.AreEqual("!!!not-valid!!!", LanguageOption.GetLanguageDisplayName("!!!not-valid!!!"));
}
