namespace Lertaro.Plugins.PinyinAlias.Tests;
// The real conversion is done by a Windows locale call (LCMapStringEx), so this is the one place that
// exercises the native path instead of the pure seam. The repo only ships Windows builds, so the
// mapping is expected to be available; IsConversionAvailable asserts that expectation explicitly rather
// than letting a silent pass-through make the assertions below meaningless.
[TestClass]
public sealed class HanConversionTests
{
    [TestMethod]
    public void ToSimplified_TraditionalName_ReturnsItsSimplifiedSpelling()
    {
        Assert.IsTrue(HanConversion.IsConversionAvailable(), "the Windows locale mapping must be usable");

        Assert.AreEqual("电脑", HanConversion.ToSimplified("電腦"));
    }

    [TestMethod]
    public void ToSimplified_TextWithoutCjk_ReturnsInputUnchanged()
    {
        // Also covers the cheap gate: text with no CJK character never reaches the OS call at all.
        Assert.AreEqual("hello.txt", HanConversion.ToSimplified("hello.txt"));
        Assert.AreEqual("café 01", HanConversion.ToSimplified("café 01"));
    }

    [TestMethod]
    public void ToSimplified_EmptyText_ReturnsInputUnchanged()
    {
        Assert.AreEqual(string.Empty, HanConversion.ToSimplified(""));
        Assert.AreEqual(string.Empty, HanConversion.ToSimplified(string.Empty));
    }

    [TestMethod]
    public void ToSimplified_AlreadySimplifiedText_ReturnsInputUnchanged()
    {
        // The alias side relies on this: a name that is already Simplified must not gain a duplicate
        // alias, which is what SimplifiedForms' "differs from the input" check leans on.
        Assert.AreEqual("电脑", HanConversion.ToSimplified("电脑"));
    }

    [TestMethod]
    public void ToSimplified_TraditionalNameWithExtension_KeepsTheNonCjkPartLiteral()
    {
        Assert.AreEqual("电脑.txt", HanConversion.ToSimplified("電腦.txt"));
    }

    [TestMethod]
    public void ToSimplified_CalledRepeatedly_IsStable()
    {
        // The bounded cache answers the second call; both must agree.
        Assert.AreEqual(HanConversion.ToSimplified("繁體中文"), HanConversion.ToSimplified("繁體中文"));
    }
}
