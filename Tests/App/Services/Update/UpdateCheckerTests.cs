using Lertaro.App.Services.Update;
using Lertaro.Core;

namespace Lertaro.App.Tests.Services.Update;

// Two release lines are polled now, and they answer in two different JSON shapes with two different ideas
// of what an asset is. Everything downstream (UpdateAssetSelector, the installer, the ".sig" it appends)
// sees one shape, so the mapping is where a silent break would land: a release that parses but carries no
// downloadable asset looks exactly like "you are up to date".
//
// The network loop itself is not exercised here -- it owns a private HttpClient and the decisions it makes
// are PickNewest and UsableSources, which are. Both endpoints are public and were checked live instead.
[TestClass]
public sealed class UpdateCheckerTests
{
    private const string GitHubJson = """
        {
          "tag_name": "v5.12.8",
          "html_url": "https://github.com/Lertaro/Lertaro/releases/tag/v5.12.8",
          "body": "release notes GitHub calls body",
          "assets": [
            {
              "name": "Lertaro-Portable.zip",
              "browser_download_url": "https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip"
            },
            {
              "name": "Lertaro-Portable.zip.sig",
              "browser_download_url": "https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip.sig"
            }
          ]
        }
        """;

    private const string GitLabJson = """
        {
          "tag_name": "v5.12.8",
          "description": "release notes GitLab calls description",
          "assets": {
            "count": 2,
            "links": [
              {
                "name": "Lertaro-Portable.zip",
                "url": "https://gitlab.com/api/v4/projects/1/packages/generic/lertaro/5.12.8/Lertaro-Portable.zip",
                "direct_asset_url": "https://gitlab.com/Mikachu2333/lertaro/-/releases/v5.12.8/downloads/Lertaro-Portable.zip"
              },
              {
                "name": "Lertaro-Portable.zip.sig",
                "url": "https://gitlab.com/api/v4/projects/1/packages/generic/lertaro/5.12.8/Lertaro-Portable.zip.sig"
              }
            ]
          }
        }
        """;

    private static ReleaseInfo Tagged(string tag) => new() { TagName = tag };

    private static ReleaseAssetLink AssetNamed(ReleaseInfo release, string name)
        => release.Assets.Links.Single(a => a.Name == name);

    [TestMethod]
    public void UsableSources_NoToken_AsksGitHubOnly()
    {
        // GitLab's line is asked with a personal access token. Without one it is skipped outright, which
        // is also what guarantees no update is downloaded from GitLab: nothing there is ever discovered.
        var sources = UpdateChecker.UsableSources(string.Empty).ToList();

        Assert.HasCount(1, sources);
        Assert.IsTrue(sources[0].IsGitHub);
    }

    [TestMethod]
    public void UsableSources_AConfiguredToken_AsksBothLinesGitHubFirst()
    {
        var sources = UpdateChecker.UsableSources("glpat-abc123").ToList();

        Assert.HasCount(2, sources);
        Assert.IsTrue(sources[0].IsGitHub);
        Assert.IsFalse(sources[1].IsGitHub);

        // The token belongs on the GitLab request only; the record carries the URL the header is added to.
        StringAssert.Contains(sources[1].Url, "gitlab.com");
    }

    [TestMethod]
    public void PickNewest_TakesTheHigherVersionEvenWhenItIsNotTheFirstAnswered()
    {
        // GitLab's pipeline can be ahead: GitHub throttles at 36h and GitLab additionally needs a
        // self-hosted runner, so either line may be the one holding the newer tag.
        var picked = UpdateChecker.PickNewest([Tagged("v5.12.8"), Tagged("v5.13.0")]);

        Assert.IsNotNull(picked);
        Assert.AreEqual("v5.13.0", picked.TagName);
    }

    [TestMethod]
    public void PickNewest_TakesGitHubWhenItIsTheHigherOne()
    {
        var picked = UpdateChecker.PickNewest([Tagged("v5.13.0"), Tagged("v5.12.8")]);

        Assert.IsNotNull(picked);
        Assert.AreEqual("v5.13.0", picked.TagName);
    }

    [TestMethod]
    public void PickNewest_ATieKeepsGitHub()
    {
        var github = Tagged("v5.12.8");
        var gitlab = Tagged("v5.12.8");

        // "GitHub first" has to survive a check that asks both lines, and the usual state of the two is
        // exactly this: the same tag published twice.
        Assert.AreSame(github, UpdateChecker.PickNewest([github, gitlab]));
    }

    [TestMethod]
    public void PickNewest_ComparesVersionsNumericallyNotAsText()
    {
        var picked = UpdateChecker.PickNewest([Tagged("v5.12.9"), Tagged("v5.12.10")]);

        Assert.IsNotNull(picked);
        Assert.AreEqual("v5.12.10", picked.TagName);
    }

    [TestMethod]
    public void PickNewest_StripsEitherCaseOfTheVPrefix()
    {
        var picked = UpdateChecker.PickNewest([Tagged("V5.12.8"), Tagged("5.13.0")]);

        Assert.IsNotNull(picked);
        Assert.AreEqual("5.13.0", picked.TagName);
    }

    [TestMethod]
    public void PickNewest_SkipsATagThatIsNotAVersion()
    {
        var picked = UpdateChecker.PickNewest([Tagged("nightly"), Tagged("v5.12.8")]);

        Assert.IsNotNull(picked);
        Assert.AreEqual("v5.12.8", picked.TagName);
    }

    [TestMethod]
    public void PickNewest_NoUsableTag_ReturnsNull()
    {
        Assert.IsNull(UpdateChecker.PickNewest([]));
        Assert.IsNull(UpdateChecker.PickNewest([Tagged("nightly"), Tagged("latest")]));
    }

    [TestMethod]
    public void ParseGitHub_MapsTheFlatAssetArrayOntoTheSharedShape()
    {
        var release = UpdateChecker.ParseGitHub(GitHubJson, []);

        Assert.IsNotNull(release);
        Assert.AreEqual("v5.12.8", release.TagName);
        Assert.AreEqual("https://github.com/Lertaro/Lertaro/releases/tag/v5.12.8", release.HtmlUrl);
        Assert.HasCount(2, release.Assets.Links);

        // browser_download_url lands on Url, so DownloadUrl falls through to it: GitHub sends no
        // direct_asset_url, and preferring an empty one would hand the installer an empty address.
        var zip = AssetNamed(release, "Lertaro-Portable.zip");
        Assert.AreEqual(string.Empty, zip.DirectAssetUrl);
        Assert.AreEqual("https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip", zip.DownloadUrl);
    }

    [TestMethod]
    public void ParseGitHub_CarriesTheMirrorsItWasGiven()
    {
        var mirrors = new[] { "https://one.example.com/", "https://two.example.com/" };

        var release = UpdateChecker.ParseGitHub(GitHubJson, mirrors);

        Assert.IsNotNull(release);
        CollectionAssert.AreEqual(mirrors, release.Mirrors.ToList());
    }

    [TestMethod]
    public void ParseGitHub_NullResponse_ReturnsNull() => Assert.IsNull(UpdateChecker.ParseGitHub("null", []));

    [TestMethod]
    public void ParseGitLab_PrefersTheDirectAssetPathAndFallsBackToTheLink()
    {
        var release = UpdateChecker.ParseGitLab(GitLabJson);

        Assert.IsNotNull(release);
        Assert.AreEqual("v5.12.8", release.TagName);
        Assert.AreEqual(
            "https://gitlab.com/Mikachu2333/lertaro/-/releases/v5.12.8/downloads/Lertaro-Portable.zip",
            AssetNamed(release, "Lertaro-Portable.zip").DownloadUrl);
        Assert.AreEqual(
            "https://gitlab.com/api/v4/projects/1/packages/generic/lertaro/5.12.8/Lertaro-Portable.zip.sig",
            AssetNamed(release, "Lertaro-Portable.zip.sig").DownloadUrl);
    }

    [TestMethod]
    public void ParseGitLab_BuildsTheReleasePageGitLabDoesNotSend()
    {
        var release = UpdateChecker.ParseGitLab(GitLabJson);

        Assert.IsNotNull(release);
        Assert.AreEqual("https://gitlab.com/Mikachu2333/lertaro/-/releases/v5.12.8", release.HtmlUrl);
    }

    [TestMethod]
    public void ParseGitLab_NeverCarriesMirrors()
    {
        var release = UpdateChecker.ParseGitLab(GitLabJson);

        Assert.IsNotNull(release);
        // GitLab is the line a user reaches precisely when GitHub is the one they cannot talk to, so
        // routing it through a GitHub proxy would defeat the point of having it.
        Assert.IsEmpty(release.Mirrors);
        CollectionAssert.AreEqual(
            new[] { AssetNamed(release, "Lertaro-Portable.zip").DownloadUrl },
            release.DownloadUrls(AssetNamed(release, "Lertaro-Portable.zip")).ToList());
    }

    [TestMethod]
    public void ParseGitLab_AWireMirrorsKeyCannotNameTheHostItsOwnPackageComesFrom()
    {
        // Mirrors is [JsonIgnore] for this reason: the prefixes decide where an executable is fetched
        // from, and they must come from the user's settings, never from the response being verified.
        const string hostile = """
            {
              "tag_name": "v9.9.9",
              "mirrors": [ "https://attacker.example.com/" ],
              "assets": { "links": [ { "name": "Lertaro-Portable.zip", "url": "https://gitlab.com/x.zip" } ] }
            }
            """;

        var release = UpdateChecker.ParseGitLab(hostile);

        Assert.IsNotNull(release);
        Assert.IsEmpty(release.Mirrors);
        CollectionAssert.AreEqual(new[] { "https://gitlab.com/x.zip" }, release.DownloadUrls(release.Assets.Links[0]).ToList());
    }

    [TestMethod]
    public void ParseGitHub_AWireMirrorsKeyCannotOverrideTheSettingsOnesEither()
    {
        const string hostile = """
            {
              "tag_name": "v9.9.9",
              "mirrors": [ "https://attacker.example.com/" ],
              "assets": [ { "name": "Lertaro-Portable.zip", "browser_download_url": "https://github.com/x.zip" } ]
            }
            """;

        var release = UpdateChecker.ParseGitHub(hostile, ["https://user.example.com/"]);

        Assert.IsNotNull(release);
        CollectionAssert.AreEqual(new[] { "https://user.example.com/" }, release.Mirrors.ToList());
    }

    [TestMethod]
    public void DownloadUrls_TriesEachMirrorInOrderThenTheOriginalAddress()
    {
        var release = new ReleaseInfo
        {
            Mirrors = ["https://one.example.com/", "https://two.example.com/", "https://three.example.com/"],
        };
        var asset = new ReleaseAssetLink { Name = "Lertaro-Portable.zip", Url = "https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip" };

        // The last entry is why mirrors whose owners all disappeared cost nothing but time.
        CollectionAssert.AreEqual(
            new[]
            {
                "https://one.example.com/https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip",
                "https://two.example.com/https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip",
                "https://three.example.com/https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip",
                "https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip",
            },
            release.DownloadUrls(asset).ToList());
    }

    [TestMethod]
    public void DownloadUrls_NoMirrors_IsJustTheOriginalAddress()
    {
        var release = new ReleaseInfo();
        var asset = new ReleaseAssetLink { Url = "https://github.com/x.zip" };

        CollectionAssert.AreEqual(new[] { "https://github.com/x.zip" }, release.DownloadUrls(asset).ToList());
    }

    [TestMethod]
    [DataRow("https://mirror.example.com")]
    [DataRow("https://mirror.example.com/")]
    [DataRow("https://mirror.example.com///")]
    public void DownloadUrls_ProducesTheSameAddressHoweverTheSlashWasTyped(string typed)
    {
        // End to end through the two pieces that own the rule: Normalize gives the prefix its one slash,
        // DownloadUrls concatenates the whole original URL onto it without asking any further questions.
        var release = UpdateChecker.ParseGitHub(GitHubJson, UpdateMirrorSettings.Normalize([typed]));

        Assert.IsNotNull(release);
        CollectionAssert.AreEqual(
            new[]
            {
                "https://mirror.example.com/https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip",
                "https://github.com/Lertaro/Lertaro/releases/download/v5.12.8/Lertaro-Portable.zip",
            },
            release.DownloadUrls(AssetNamed(release, "Lertaro-Portable.zip")).ToList());
    }

    [TestMethod]
    public void DownloadUrls_AMirroredZipStillFindsItsSignatureBesideIt()
    {
        var release = UpdateChecker.ParseGitHub(GitHubJson, ["https://mirror.example.com/"]);
        Assert.IsNotNull(release);

        var zip = AssetNamed(release, "Lertaro-Portable.zip");
        var sig = AssetNamed(release, "Lertaro-Portable.zip.sig");

        // The installer fetches the signature as zipUrl + ".sig" rather than looking the asset up, so a
        // mirror that prefixed only the zip would leave the signature coming from unreachable GitHub and
        // every mirrored update would fail verification. Both release lines publish it under that name.
        Assert.AreEqual(
            release.DownloadUrls(sig).ToList()[0],
            release.DownloadUrls(zip).ToList()[0] + ".sig");
    }
}