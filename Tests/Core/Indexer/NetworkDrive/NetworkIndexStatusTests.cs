using Lertaro.Core.Indexer.NetworkDrive;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive;

[TestClass]
public sealed class NetworkIndexStatusTests
{
    [TestMethod]
    public void Clone_CopiesEveryField_IntoAnIndependentInstance()
    {
        var original = new NetworkIndexStatus
        {
            Drive = "Z",
            State = "Indexing",
            Items = 10,
            Skipped = 1,
            Errors = 2,
            EnumerateErrors = 3,
            AttributeErrors = 4,
            ReparseSkipped = 5,
            SlowDirectories = 6,
            CachePath = @"C:\cache\z",
            LastUpdated = new DateTime(2024, 1, 1),
            Error = "boom"
        };

        var clone = original.Clone();

        Assert.AreNotSame(original, clone);
        Assert.AreEqual(original.Drive, clone.Drive);
        Assert.AreEqual(original.State, clone.State);
        Assert.AreEqual(original.Items, clone.Items);
        Assert.AreEqual(original.Skipped, clone.Skipped);
        Assert.AreEqual(original.Errors, clone.Errors);
        Assert.AreEqual(original.EnumerateErrors, clone.EnumerateErrors);
        Assert.AreEqual(original.AttributeErrors, clone.AttributeErrors);
        Assert.AreEqual(original.ReparseSkipped, clone.ReparseSkipped);
        Assert.AreEqual(original.SlowDirectories, clone.SlowDirectories);
        Assert.AreEqual(original.CachePath, clone.CachePath);
        Assert.AreEqual(original.LastUpdated, clone.LastUpdated);
        Assert.AreEqual(original.Error, clone.Error);
    }

    [TestMethod]
    public void Clone_MutatingClone_DoesNotAffectOriginal()
    {
        var original = new NetworkIndexStatus { Items = 1 };

        var clone = original.Clone();
        clone.Items = 99;

        Assert.AreEqual(1, original.Items);
    }
}
