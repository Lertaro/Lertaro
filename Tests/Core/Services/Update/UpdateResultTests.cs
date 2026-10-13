using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

// The updater script's own verdict file (portable-updater.ps1 writes it, nothing read it). Everything here is
// about refusing to act on something that is not a verdict: absent, truncated, foreign, or without a time.
[TestClass]
public sealed class UpdateResultTests
{
    private readonly string _directory = Directory.CreateTempSubdirectory("LertaroUpdateResult-").FullName;
    private string ResultPath => Path.Combine(_directory, UpdateResult.FileName);

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    // The shape ConvertTo-Json produces from the script's own hashtable.
    [TestMethod]
    public void ReadIfPresent_TheScriptsOwnShape_IsReadBack()
    {
        File.WriteAllText(ResultPath, """
            {
              "Status": "failed",
              "Time": "2026-10-11T03:30:09.1234567+08:00",
              "Message": "Update failed at 'D:\\Program Files\\Lertaro\\Lertaro.App.exe': Access denied",
              "RecoveryDirectory": "D:\\Program Files\\Lertaro\\update-payload\\rollback-abc"
            }
            """);

        var result = UpdateResult.ReadIfPresent(_directory);

        Assert.IsNotNull(result);
        Assert.AreEqual("failed", result.Status);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "Access denied");
        StringAssert.Contains(result.RecoveryDirectory, "rollback-abc");
        Assert.AreEqual(2026, result.Time.Year);
    }

    [TestMethod]
    public void ReadIfPresent_SuccessfulVerdict_ReadsAsSucceeded()
    {
        File.WriteAllText(ResultPath, """{"Status":"success","Time":"2026-10-11T03:30:09.0000000+08:00","Message":""}""");

        var result = UpdateResult.ReadIfPresent(_directory);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Succeeded);
    }

    // An older PowerShell host writes a BOM; a raw byte parse would refuse the file over it.
    [TestMethod]
    public void ReadIfPresent_Utf8Bom_WasWrittenByAnOlderHostAndIsStillRead()
    {
        File.WriteAllText(ResultPath, """{"Status":"failed","Time":"2026-10-11T03:30:09.0000000+08:00","Message":"denied"}""",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = UpdateResult.ReadIfPresent(_directory);

        Assert.IsNotNull(result);
        Assert.AreEqual("denied", result.Message);
    }

    [TestMethod]
    public void ReadIfPresent_NothingToActOn_IsNull()
    {
        Assert.IsNull(UpdateResult.ReadIfPresent(_directory));

        File.WriteAllText(ResultPath, "not json at all");
        Assert.IsNull(UpdateResult.ReadIfPresent(_directory));

        File.WriteAllText(ResultPath, """{"Status":"failed","Message":"no timestamp"}""");
        Assert.IsNull(UpdateResult.ReadIfPresent(_directory));

        File.WriteAllText(ResultPath, """{"Status":"failed","Time":"2026-10-11T03:30:09.0000000+08:00"}""");
        Assert.IsNotNull(UpdateResult.ReadIfPresent(_directory));
    }

    // A portable installation's directory is the user's own, so neither the file nor the text in it is trusted
    // to be small -- and both go in front of the user.
    [TestMethod]
    public void ReadIfPresent_OversizedFile_IsRefusedInsteadOfRead()
    {
        File.WriteAllText(ResultPath, "{\"Status\":\"failed\",\"Time\":\"2026-10-11T03:30:09.0000000+08:00\",\"Message\":\""
            + new string('x', 128 * 1024) + "\"}");

        Assert.IsNull(UpdateResult.ReadIfPresent(_directory));
    }

    [TestMethod]
    public void ReadIfPresent_LongMessage_IsClampedForTheDialog()
    {
        File.WriteAllText(ResultPath, "{\"Status\":\"failed\",\"Time\":\"2026-10-11T03:30:09.0000000+08:00\",\"Message\":\""
            + new string('x', 5000) + "\"}");

        var result = UpdateResult.ReadIfPresent(_directory);

        Assert.IsNotNull(result);
        Assert.AreEqual(1001, result.Message!.Length);
    }
}
