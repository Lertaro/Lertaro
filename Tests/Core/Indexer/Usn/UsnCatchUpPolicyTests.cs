using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class UsnCatchUpPolicyTests
{
    [TestMethod]
    public void CanAccept_AllowsRecordsBelowLimit() => Assert.IsTrue(UsnCatchUpPolicy.CanAccept(UsnCatchUpPolicy.MaxRecords - 1));

    [TestMethod]
    public void CanAccept_RejectsTheRecordAfterLimit() => Assert.IsFalse(UsnCatchUpPolicy.CanAccept(UsnCatchUpPolicy.MaxRecords));
}
