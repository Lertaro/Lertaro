namespace Lertaro.Plugins.BrowserData.Tests;

[TestClass]
public sealed class BrowserDataDefaultsTests
{
    [TestMethod]
    public void Profiles_CoverEdgeChromeAndFirefox()
    {
        var names = BrowserDataDefaults.Profiles().Select(p => p.Name).ToList();

        CollectionAssert.AreEqual(new[] { "Edge", "Chrome", "Firefox" }, names);
    }

    [TestMethod]
    public void Profiles_UseEnvVarTokensRatherThanAnAbsolutePath()
    {
        // Guards the rule the whole default set is built on: a shipped default can name a per-user
        // location only through %LOCALAPPDATA%/%APPDATA%, never a concrete path that would carry the
        // username of whoever's machine it was written on.
        foreach (var profile in BrowserDataDefaults.Profiles())
        {
            Assert.StartsWith("%", profile.Path);
            Assert.DoesNotContain(":", profile.Path);
        }
    }

    [TestMethod]
    public void ProfileSchemaDefaults_CarryTheSameRowsAsProfiles()
    {
        // The Settings page renders the schema default while the plugin runs on Profiles(); these two
        // disagreeing is what made a fresh install show pre-filled rows that indexed nothing.
        var rows = BrowserDataDefaults.ProfileSchemaDefaults().Cast<Dictionary<string, object>>().ToList();

        Assert.HasCount(3, rows);
        for (var i = 0; i < rows.Count; i++)
        {
            var expected = BrowserDataDefaults.Profiles()[i];
            Assert.AreEqual(expected.Name, rows[i][nameof(BrowserProfileConfig.Name)]);
            Assert.AreEqual(expected.Icon, rows[i][nameof(BrowserProfileConfig.Icon)]);
            Assert.AreEqual(expected.Path, rows[i][nameof(BrowserProfileConfig.Path)]);
        }
    }

    [TestMethod]
    public void ConfigSchemaProfilesField_DefaultsToProfileSchemaDefaults()
    {
        var field = new BrowserDataPlugin().GetConfigSchema().Fields.Single(f => f.Key == "Profiles");

        var paths = ((List<object>)field.DefaultValue)
            .Cast<Dictionary<string, object>>()
            .Select(row => (string)row[nameof(BrowserProfileConfig.Path)])
            .ToList();

        CollectionAssert.AreEqual(BrowserDataDefaults.Profiles().Select(p => p.Path).ToList(), paths);
    }
}
