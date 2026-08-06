using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class GlobMatcherTests
{
    [TestMethod]
    public void Compile_EmptyPattern_IsEmpty()
    {
        var pattern = GlobMatcher.Compile("");

        Assert.IsTrue(pattern.IsEmpty);
    }

    [TestMethod]
    public void IsMatch_EmptyPattern_OnlyMatchesEmptyText()
    {
        var pattern = GlobMatcher.Compile("   ");

        Assert.IsTrue(pattern.IsMatch(""));
        Assert.IsFalse(pattern.IsMatch("anything"));
    }

    [TestMethod]
    public void IsMatch_ValidPattern_DelegatesToCompiledRegex()
    {
        var pattern = GlobMatcher.Compile("*.log");

        Assert.IsTrue(pattern.IsMatch("service.log"));
        Assert.IsFalse(pattern.IsMatch("service.txt"));
    }

    [TestMethod]
    public void IsMatch_InvalidPattern_FailsClosedInsteadOfThrowing()
    {
        // GlobToRegex.Compile throws ArgumentException on a mismatched bracket -- NetworkGlobPattern's
        // constructor catches it and leaves _regex null, so IsMatch degrades to "never matches" rather
        // than propagating the exception to every caller walking a directory tree.
        var pattern = GlobMatcher.Compile("file[");

        Assert.IsFalse(pattern.IsEmpty);
        Assert.IsFalse(pattern.IsMatch("file["));
        Assert.IsFalse(pattern.IsMatch("anything"));
    }
}
