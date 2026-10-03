using System.Security.Principal;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class CallerVisibilityTests
{
    private const string AliceSid = "S-1-5-21-1000000000-2000000000-3000000000-1001";
    private const string BobSid = "S-1-5-21-1000000000-2000000000-3000000000-1002";

    private static readonly Dictionary<string, string> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        [AliceSid] = @"C:\Users\alice",
        [BobSid] = @"C:\Users\bob",
        ["S-1-5-18"] = @"C:\Windows\system32\config\systemprofile",
    };

    private static CallerVisibility AsAlice(bool elevated = false) =>
        CallerVisibility.For(new SecurityIdentifier(AliceSid), elevated, Profiles);

    [TestMethod]
    [DataRow(@"C:\Users\alice\Documents\notes.txt")]
    [DataRow(@"C:\Users\alice")]
    [DataRow(@"C:\Users\Public\shared.txt")]
    [DataRow(@"D:\Projects\readme.md")]
    public void OwnProfileAndSharedPlaces_AreVisible(string path) =>
        Assert.IsTrue(AsAlice().IsVisible(path));

    [TestMethod]
    [DataRow(@"C:\Users\bob\Documents\secret.docx")]
    [DataRow(@"C:\Users\bob")]
    [DataRow(@"C:\Users\bob\")]
    [DataRow(@"c:\users\BOB\Desktop\x.txt")]
    [DataRow(@"C:\Windows\system32\config\systemprofile\AppData\x")]
    public void AnotherUsersProfile_IsHidden(string path) =>
        Assert.IsFalse(AsAlice().IsVisible(path));

    [TestMethod]
    public void APathThatOnlySharesAPrefix_IsVisible()
    {
        // Hiding C:\Users\bob must not hide C:\Users\bobby.
        Assert.IsTrue(AsAlice().IsVisible(@"C:\Users\bobby\file.txt"));
        Assert.IsTrue(AsAlice().IsVisible(@"C:\Users\bob.old"));
    }

    [TestMethod]
    public void AnElevatedAdministrator_SeesEverything()
    {
        var visibility = AsAlice(elevated: true);

        Assert.IsTrue(visibility.IsVisible(@"C:\Users\bob\Documents\secret.docx"));
        Assert.AreSame(CallerVisibility.Everything, visibility);
    }

    [TestMethod]
    public void AnUnidentifiedCaller_SeesNoProfileAtAll()
    {
        var visibility = CallerVisibility.For(null, isElevatedAdmin: false, Profiles);

        Assert.IsFalse(visibility.IsVisible(@"C:\Users\alice\notes.txt"));
        Assert.IsFalse(visibility.IsVisible(@"C:\Users\bob\notes.txt"));
        Assert.IsTrue(visibility.IsVisible(@"D:\Projects\readme.md"));
    }

    [TestMethod]
    public void ACallersOwnSid_IsMatchedCaseInsensitively()
    {
        var visibility = CallerVisibility.For(new SecurityIdentifier(AliceSid), false,
            new Dictionary<string, string> { [AliceSid.ToLowerInvariant()] = @"C:\Users\alice" });

        Assert.IsTrue(visibility.IsVisible(@"C:\Users\alice\notes.txt"));
    }

    [TestMethod]
    public void EmptyPaths_AreNotTreatedAsHidden()
    {
        Assert.IsTrue(AsAlice().IsVisible(null));
        Assert.IsTrue(AsAlice().IsVisible(string.Empty));
    }
}
