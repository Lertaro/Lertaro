using Lertaro.Core.Indexer.Mft;

namespace Lertaro.Core.Tests.Indexer.Mft;

[TestClass]
public sealed class MftIndexScannerTests
{
    [TestMethod]
    public void ResolveRecordOwner_BaseRecord_UsesSequenceAndRecordIndex()
    {
        var owner = MftIndexScanner.ResolveRecordOwner(0, 7, 123);

        Assert.AreEqual((UInt128)0x0007_0000_0000_007B, owner);
    }

    [TestMethod]
    public void ResolveRecordOwner_ExtensionRecord_UsesBaseReference()
    {
        const ulong baseReference = 0x0003_0000_0000_002A;

        var owner = MftIndexScanner.ResolveRecordOwner(baseReference, 9, 88);

        Assert.AreEqual((UInt128)baseReference, owner);
    }
}
