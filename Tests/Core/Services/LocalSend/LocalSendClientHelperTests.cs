using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendClientHelperTests
{
    [TestMethod]
    public void GetFileType_UsesMimeForV2AndLegacyCategoryForV1()
    {
        Assert.AreEqual("image/png", LocalSendClientHelper.GetFileType(".png", legacy: false));
        Assert.AreEqual("image", LocalSendClientHelper.GetFileType(".png", legacy: true));
    }

    [TestMethod]
    public void GetFileType_UsesTheBuiltInMapInsteadOfWindowsRegistration()
    {
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", LocalSendClientHelper.GetFileType(".xlsx", legacy: false));
        Assert.AreEqual("application/vnd.lotus-1-2-3", LocalSendClientHelper.GetFileType(".123", legacy: false));
        Assert.AreEqual("application/octet-stream", LocalSendClientHelper.GetFileType(".unknown", legacy: false));
    }

    [TestMethod]
    public void GetMimeTypeForFileName_UsesTheFullOfficialMimeMap()
    {
        Assert.AreEqual("video/3gpp2", LocalSendClientHelper.GetMimeTypeForFileName("video.3g2"));
    }
}
