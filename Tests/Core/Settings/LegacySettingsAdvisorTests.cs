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

    // The other legacy: the CoreExtensions plugin used to store its own token prefix ("CustomFilterPrefix").
    // It now reads the host's (SearchSyntaxService), so a stored copy is not merely unused -- it is a
    // character its tokens no longer answer to, and any sidebar rule written with it stops expanding.
    private const string PluginId = "Lertaro.Plugins.CoreExtensions";
    private const string Key = "CustomFilterPrefix";

    [TestMethod]
    public void TakeLegacyFilterPrefix_DisagreeingValue_IsRemovedAndReported()
    {
        var settings = new UserSettings { GlobalTokenPrefix = "\\" };
        settings.SetPluginSetting(PluginId, Key, "#");

        Assert.AreEqual("#", LegacySettingsAdvisor.TakeLegacyFilterPrefix(settings));
        Assert.IsNull(settings.GetPluginSetting<string?>(PluginId, Key, null), "the stale key must be gone");
    }

    [TestMethod]
    public void TakeLegacyFilterPrefix_AgreeingValue_IsRemovedButNotReported()
    {
        // Nothing changed for the user, so there is nothing to tell them -- but the key is still dead
        // weight pointing at a setting that no longer exists, so it goes.
        var settings = new UserSettings { GlobalTokenPrefix = "\\" };
        settings.SetPluginSetting(PluginId, Key, "\\");

        Assert.IsNull(LegacySettingsAdvisor.TakeLegacyFilterPrefix(settings));
        Assert.IsNull(settings.GetPluginSetting<string?>(PluginId, Key, null));
    }

    [TestMethod]
    public void TakeLegacyFilterPrefix_NothingStored_ReportsNothing()
    {
        var settings = new UserSettings();

        Assert.IsNull(LegacySettingsAdvisor.TakeLegacyFilterPrefix(settings));
    }

    [TestMethod]
    public void TakeLegacyFilterPrefix_IsNotRepeatable()
    {
        // This is what makes the notice need no "already shown" flag of its own: removing the key is the
        // fix, so a second launch cannot find the condition again and nag about it.
        var settings = new UserSettings { GlobalTokenPrefix = "\\" };
        settings.SetPluginSetting(PluginId, Key, "#");

        Assert.IsNotNull(LegacySettingsAdvisor.TakeLegacyFilterPrefix(settings));
        Assert.IsNull(LegacySettingsAdvisor.TakeLegacyFilterPrefix(settings));
    }

    // The '?' collision: the precision-inversion trigger is read from a word's first character, which is
    // exactly where the app-wide token prefix and a per-result-type trigger are read. A value saved before '?'
    // became an operator therefore cannot work any more -- and in the prefix's case it does not merely lose
    // the inversion, it gets the whole word lifted out of the query as a token nobody claims.
    [TestMethod]
    public void TakePrecisionTriggerTokenPrefix_PrecisionValue_IsResetToTheDefault()
    {
        var settings = new UserSettings { GlobalTokenPrefix = "?" };

        Assert.IsTrue(LegacySettingsAdvisor.HasPrecisionTriggerCollisions(settings));
        Assert.IsTrue(LegacySettingsAdvisor.TakePrecisionTriggerTokenPrefix(settings));
        Assert.AreEqual("\\", settings.GlobalTokenPrefix, "the shipped default is the only prefix that cannot collide");
        Assert.IsFalse(LegacySettingsAdvisor.TakePrecisionTriggerTokenPrefix(settings), "and it must not fire twice");
    }

    [TestMethod]
    [DataRow("\\")]
    [DataRow(":")]
    [DataRow("#")]
    [DataRow("")]
    public void TakePrecisionTriggerTokenPrefix_AnythingElse_IsLeftAlone(string prefix)
    {
        // ':' is the OTHER legacy, and it is a separate notice with its own meaning: it still tokenizes, it
        // just collides with the exclusion operator. Nothing here may rewrite it.
        var settings = new UserSettings { GlobalTokenPrefix = prefix };

        Assert.IsFalse(LegacySettingsAdvisor.TakePrecisionTriggerTokenPrefix(settings));
        Assert.AreEqual(prefix, settings.GlobalTokenPrefix);
    }

    [TestMethod]
    public void TakePrecisionTriggerResultTypes_PrecisionTriggers_AreClearedAndReported()
    {
        var settings = new UserSettings
        {
            ResultTypeTriggers = new Dictionary<string, string>
            {
                ["Files"] = "?",
                ["SomeProvider"] = "?x",
                ["OtherProvider"] = ";",
            },
        };

        var taken = LegacySettingsAdvisor.TakePrecisionTriggerResultTypes(settings);

        Assert.HasCount(2, taken);
        Assert.Contains(item => item.TypeId == "Files" && item.Trigger == "?", taken);
        Assert.Contains(item => item.TypeId == "SomeProvider" && item.Trigger == "?x", taken);
        Assert.IsTrue(settings.ResultTypeTriggers.ContainsKey("OtherProvider"), "a usable trigger must survive");
        Assert.IsFalse(settings.ResultTypeTriggers.ContainsKey("Files"), "a cleared trigger must be gone, so the feature falls back to its working default");
    }

    [TestMethod]
    public void TakePrecisionTriggerResultTypes_IsNotRepeatable()
    {
        var settings = new UserSettings { ResultTypeTriggers = new Dictionary<string, string> { ["Files"] = "?" } };

        Assert.HasCount(1, LegacySettingsAdvisor.TakePrecisionTriggerResultTypes(settings));
        Assert.IsEmpty(LegacySettingsAdvisor.TakePrecisionTriggerResultTypes(settings));
        Assert.IsFalse(LegacySettingsAdvisor.HasPrecisionTriggerCollisions(settings));
    }

    [TestMethod]
    public void HasPrecisionTriggerCollisions_UsableSettings_SaysNothing()
    {
        var settings = new UserSettings
        {
            GlobalTokenPrefix = "\\",
            ResultTypeTriggers = new Dictionary<string, string> { ["Files"] = ";" },
        };

        Assert.IsFalse(LegacySettingsAdvisor.HasPrecisionTriggerCollisions(settings));
    }
}
