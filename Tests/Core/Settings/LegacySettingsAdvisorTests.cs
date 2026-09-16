namespace Lertaro.Core.Tests.Settings;

// Which saved values a previous release wrote but the current syntax can no longer honor.
//
// The value that matters is the shipped one, not a hypothetical: v5.6.7 wrote ':' as the plugin query
// token prefix, and ':' is now the exclusion operator, so a settings file carried over keeps a prefix
// whose tokens are consumed by the syntax before any token provider sees them. The notice exists to say
// so; these pin WHICH values it fires for, since firing on a value the user chose themselves would be
// wrong (that is the settings page's own warning to give).
[TestClass]
public sealed class LegacySettingsAdvisorTests
{
    [TestMethod]
    public void HasLegacyTokenPrefix_TheShippedColonValue_IsReported()
    {
        var settings = new UserSettings { GlobalTokenPrefix = ":" };

        Assert.IsTrue(LegacySettingsAdvisor.HasLegacyTokenPrefix(settings));
    }

    [TestMethod]
    public void HasLegacyTokenPrefix_TheCurrentDefault_IsNotReported()
    {
        // The default is what a fresh install writes, and what the notice tells the user to switch to --
        // flagging it would make the notice fire for everyone, including people who already fixed it.
        var settings = new UserSettings();

        Assert.AreEqual("\\", settings.GlobalTokenPrefix);
        Assert.IsFalse(LegacySettingsAdvisor.HasLegacyTokenPrefix(settings));
    }

    [TestMethod]
    // Any character the user picked themselves is the settings page's to report, not a migration's: it
    // means they have already been to that field and made a choice, even if the choice is still unusable.
    [DataRow("\\")]
    [DataRow("*")]
    [DataRow("@")]
    [DataRow("")]
    public void HasLegacyTokenPrefix_AnythingElse_IsNotReported(string prefix)
    {
        var settings = new UserSettings { GlobalTokenPrefix = prefix };

        Assert.IsFalse(LegacySettingsAdvisor.HasLegacyTokenPrefix(settings));
    }

    [TestMethod]
    public void ShouldShowNotice_LegacyPrefixOnAFreshSettingsFile_IsShown()
        => Assert.IsTrue(LegacySettingsAdvisor.ShouldShowNotice(new UserSettings { GlobalTokenPrefix = ":" }));

    [TestMethod]
    public void ShouldShowNotice_AlreadyShown_IsNotShownAgain()
    {
        // Once per user, not once per launch: the settings page keeps reporting the value on its own field,
        // so a balloon that reappeared at every start would only nag about something already seen.
        var settings = new UserSettings { GlobalTokenPrefix = ":", LegacyTokenPrefixNoticeShown = true };

        Assert.IsFalse(LegacySettingsAdvisor.ShouldShowNotice(settings));
    }

    [TestMethod]
    public void ShouldShowNotice_CurrentPrefix_IsNotShownEvenWhenUnmarked()
    {
        var settings = new UserSettings { GlobalTokenPrefix = "\\" };

        Assert.IsFalse(LegacySettingsAdvisor.HasLegacyTokenPrefix(settings));
        Assert.IsFalse(LegacySettingsAdvisor.ShouldShowNotice(settings));
    }
}
