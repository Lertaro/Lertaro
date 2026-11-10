using System.Reflection;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.PluginSdk.Tests.Abstractions;

// Binary compatibility for third-party plugins.
//
// Plugins do not build against this source tree: they add a <Reference> to Lertaro.PluginSdk.dll taken from
// an installed copy, with <Private>false</Private> (see Site/dev-guide/getting-started.md). So the only
// thing a plugin's compiled IL is checked against is the assembly that ships. Removing a public member
// while the assembly version stays the same therefore passes every check inside this repository -- every
// in-tree plugin gets recompiled -- and still breaks every third-party one: the binding succeeds because
// the version matches, and the missing member only surfaces as MissingMethodException when the host calls
// the plugin's GetConfigSchema. The host swallows exceptions from that call, so the plugin's whole settings
// page disappears with no diagnostic at all.
//
// IsTriggerWord was removed exactly that way. These keep the replacement honest and the forwarding alive.
[TestClass]
public sealed class PluginConfigFieldCompatTests
{
    [TestMethod]
    public void IsTriggerWord_TrueClaimsTheTriggerKeywordValidation()
    {
#pragma warning disable CS0618 // deliberate: this member exists for code compiled against the older assembly
        var field = new PluginConfigField { IsTriggerWord = true };
#pragma warning restore CS0618

        Assert.AreEqual(ConfigFieldValidation.TriggerKeyword, field.Validation);
#pragma warning disable CS0618
        Assert.IsTrue(field.IsTriggerWord);
#pragma warning restore CS0618
    }

    [TestMethod]
    public void Validation_IsTheSameStateTheObsoleteFlagReports()
    {
        var field = new PluginConfigField { Validation = ConfigFieldValidation.TriggerKeyword };

#pragma warning disable CS0618
        Assert.IsTrue(field.IsTriggerWord, "a plugin reading the flag it wrote must still see it set");
#pragma warning restore CS0618
    }

    [TestMethod]
    public void IsTriggerWord_FalseDoesNotClobberAnotherValidation()
    {
        // None is what an unset field already holds, so "false" has nothing to do -- and clearing here would
        // let an initializer that sets Validation first and then writes IsTriggerWord = false silently drop
        // the first.
        var field = new PluginConfigField { Validation = ConfigFieldValidation.TokenKeyword };

#pragma warning disable CS0618
        field.IsTriggerWord = false;
#pragma warning restore CS0618

        Assert.AreEqual(ConfigFieldValidation.TokenKeyword, field.Validation);
    }

    [TestMethod]
    public void PluginConfigField_StillDeclaresTheMemberPluginsWereCompiledAgainst()
    {
        // The guard against a repeat: this fails if the forwarding property is deleted again while
        // PluginSdk.csproj still says 2.0.0. Removing a public member is fine -- it just has to come with an
        // assembly version bump and a migration note, or the failure lands on third-party users silently.
        var property = typeof(PluginConfigField).GetProperty("IsTriggerWord", BindingFlags.Public | BindingFlags.Instance);

        Assert.IsNotNull(property, "IsTriggerWord is part of the surface third-party plugins were compiled against");
        Assert.IsNotNull(property!.GetSetMethod(true), "the setter is the call the old IL makes");
        Assert.AreEqual(typeof(bool), property.PropertyType);
    }
}
