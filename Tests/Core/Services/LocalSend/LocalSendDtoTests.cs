using System.Text.Json;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public class LocalSendDtoTests
{
    [TestMethod]
    public void LocalSendDeviceInfo_Serialization_MatchesOfficialJsonSchema()
    {
        var device = new LocalSendDeviceInfo
        {
            Alias = "Test-Device",
            Version = "2.1",
            DeviceModel = "Windows",
            DeviceType = "desktop",
            Fingerprint = "test-hash",
            Port = 53317,
            Protocol = "http",
            Download = false
        };

        var json = JsonSerializer.Serialize(device);

        StringAssert.Contains(json, "\"alias\":\"Test-Device\"");
        StringAssert.Contains(json, "\"version\":\"2.1\"");
        StringAssert.Contains(json, "\"deviceModel\":\"Windows\"");
        StringAssert.Contains(json, "\"deviceType\":\"desktop\"");
        StringAssert.Contains(json, "\"fingerprint\":\"test-hash\"");
        StringAssert.Contains(json, "\"port\":53317");
        StringAssert.Contains(json, "\"protocol\":\"http\"");
        StringAssert.Contains(json, "\"download\":false");
    }

    [TestMethod]
    public void PrepareUploadRequestDto_Deserialization_ParsesOfficialFormat()
    {
        var sampleJson = """
        {
          "info": {
            "alias": "Phone-App",
            "version": "2.1",
            "deviceModel": "Pixel",
            "deviceType": "mobile",
            "fingerprint": "abc",
            "port": 53317,
            "protocol": "http",
            "download": false
          },
          "files": {
            "id-1": {
              "id": "id-1",
              "fileName": "test.png",
              "size": 2048,
              "fileType": "image",
              "sha256": "hash123"
            }
          }
        }
        """;

        var dto = JsonSerializer.Deserialize<PrepareUploadRequestDto>(sampleJson);

        Assert.IsNotNull(dto);
        Assert.AreEqual("Phone-App", dto.Info.Alias);
        Assert.AreEqual("Pixel", dto.Info.DeviceModel);
        Assert.AreEqual("mobile", dto.Info.DeviceType);
        Assert.HasCount(1, dto.Files);
        Assert.IsTrue(dto.Files.ContainsKey("id-1"));

        var file = dto.Files["id-1"];
        Assert.AreEqual("test.png", file.FileName);
        Assert.AreEqual(2048L, file.Size);
        Assert.AreEqual("image", file.FileType);
    }
}
