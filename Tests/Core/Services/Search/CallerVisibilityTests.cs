using System.Security.Principal;
using System.Security.AccessControl;
using System.IO.Pipes;
using Lertaro.Core.Wire;
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
    public void OwnProfileAndSharedPlaces_AreVisible(string path)
    {
        using var visibility = AsAlice();
        Assert.IsTrue(visibility.IsVisible(path));
        Assert.IsTrue(visibility.IsIndexedPathVisible(path));
    }

    [TestMethod]
    [DataRow(@"C:\Users\bob\Documents\secret.docx")]
    [DataRow(@"C:\Users\bob")]
    [DataRow(@"C:\Users\bob\")]
    [DataRow(@"c:\users\BOB\Desktop\x.txt")]
    [DataRow(@"C:\Windows\system32\config\systemprofile\AppData\x")]
    public void AnotherUsersProfile_IsHidden(string path)
    {
        using var visibility = AsAlice();
        Assert.IsFalse(visibility.IsVisible(path));
        Assert.IsFalse(visibility.IsIndexedPathVisible(path));
    }

    [TestMethod]
    public void APathThatOnlySharesAPrefix_IsVisible()
    {
        // Hiding C:\Users\bob must not hide C:\Users\bobby.
        Assert.IsTrue(AsAlice().IsVisible(@"C:\Users\bobby\file.txt"));
        Assert.IsTrue(AsAlice().IsVisible(@"C:\Users\bob.old"));
        Assert.IsTrue(AsAlice().IsIndexedPathVisible(@"C:\Users\bobby\file.txt"));
        Assert.IsTrue(AsAlice().IsIndexedPathVisible(@"C:\Users\bob.old"));
    }

    [TestMethod]
    public void AnElevatedAdministrator_SeesEverything()
    {
        var visibility = AsAlice(elevated: true);

        Assert.IsTrue(visibility.IsVisible(@"C:\Users\bob\Documents\secret.docx"));
        Assert.IsTrue(visibility.IsIndexedPathVisible(@"C:\Users\bob\Documents\secret.docx"));
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

    [TestMethod]
    [DataRow(@"C:\Users\bob\notes.txt")]
    [DataRow("c:/users/BOB/notes.txt")]
    [DataRow(@"C:\Users\alice\..\bob\notes.txt")]
    [DataRow(@"C:\Users\alice\..")]
    [DataRow(@"C:\Users\alice\.\notes.txt")]
    [DataRow(@"C:\Users\alice\.")]
    public void IsIndexedPathVisible_HiddenOrNonCanonicalPath_IsRejected(string path)
    {
        using var visibility = AsAlice();
        Assert.IsFalse(visibility.IsIndexedPathVisible(path));
    }

    [TestMethod]
    [DataRow("C:\\visible.txt\0hidden.txt")]
    [DataRow("relative.txt")]
    [DataRow(@"\\server\share\file.txt")]
    [DataRow("//server/share/file.txt")]
    public void IsIndexedPathVisible_InvalidOrNonLocalPath_IsRejected(string path)
    {
        Assert.IsFalse(CallerVisibility.Everything.IsIndexedPathVisible(path));
        Assert.IsFalse(CallerVisibility.Everything.IsVisible(path));
    }

    [TestMethod]
    public async Task IndexedSearch_DoesNotOpenFiles_ButDirectAccessStillChecksAclChanges()
    {
        using var visibility = await CaptureCallerAsync();
        using var identity = WindowsIdentity.GetCurrent();
        using var before = WindowsIdentity.GetCurrent(ifImpersonating: true);
        var folder = Directory.CreateTempSubdirectory("LertaroVisibility_").FullName;
        var file = new FileInfo(Path.Combine(folder, "readable~name.txt"));
        File.WriteAllText(file.FullName, "test");
        var originalAcl = file.GetAccessControl();
        originalAcl.SetSecurityDescriptorBinaryForm(originalAcl.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
        try
        {
            var readable = new SearchResult { Path = file.FullName };
            var directory = new SearchResult { Path = folder, IsDir = true };
            var missing = new SearchResult { Path = Path.Combine(folder, "missing.txt") };
            var equivalentPath = new SearchResult { Path = Path.Combine(folder, ".", file.Name) };
            Assert.IsTrue(visibility.IsIndexedPathVisible(readable.Path));
            Assert.IsTrue(visibility.IsIndexedPathVisible(directory.Path));
            Assert.IsTrue(visibility.IsIndexedPathVisible(missing.Path), "Indexed results do not require a live file open.");
            Assert.IsFalse(visibility.IsVisible(missing.Path));
            Assert.IsTrue(visibility.IsVisible(equivalentPath.Path));

            var deniedAcl = file.GetAccessControl();
            deniedAcl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
            file.SetAccessControl(deniedAcl);
            Assert.IsFalse(visibility.IsVisible(file.FullName));
            Assert.IsFalse(visibility.IsVisible(equivalentPath.Path));
            Assert.IsTrue(visibility.IsIndexedPathVisible(readable.Path), "Search visibility uses profile boundaries rather than per-file ACLs.");
            var channel = SearchStreamPump.CreateResultChannel();
            Assert.IsTrue(channel.Writer.TryWrite(readable));
            Assert.IsTrue(channel.Writer.TryWrite(directory));
            channel.Writer.Complete();
            using var response = new MemoryStream();
            await SearchStreamPump.WriteResultsAsync(channel.Reader, response, visibility, CancellationToken.None);
            await SearchResponseBinarySerializer.WriteEndAsync(response);
            response.Position = 0;
            var returnedPaths = new List<string>();
            await SearchResponseBinarySerializer.ReadAsync(response, row => returnedPaths.Add(row.Path));
            CollectionAssert.AreEqual(new[] { file.FullName, folder }, returnedPaths, "Search may list a file whose contents cannot be read.");

            file.SetAccessControl(originalAcl);
            Assert.IsTrue(visibility.IsVisible(file.FullName), "Direct access must observe the changed ACL.");
            using var after = WindowsIdentity.GetCurrent(ifImpersonating: true);
            Assert.AreEqual(before?.User?.Value, after?.User?.Value, "Impersonation must not escape the access check.");
        }
        finally
        {
            file.SetAccessControl(originalAcl);
            file.Delete();
            Directory.Delete(folder);
        }
    }

    [TestMethod]
    public void IsIndexedPathVisible_UnidentifiedPipeCaller_RejectsAllResults()
    {
        using var pipe = new NamedPipeServerStream("LertaroUnknownCaller_" + Guid.NewGuid().ToString("N"));
        using var visibility = CallerVisibility.ForClient(pipe);
        Assert.IsFalse(visibility.IsIndexedPathVisible(@"C:\Windows"));
        Assert.IsFalse(visibility.IsIndexedPathVisible(string.Empty));
    }

    private static async Task<CallerVisibility> CaptureCallerAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var name = "LertaroVisibility_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        await Task.WhenAll(client.WriteAsync(new byte[] { 1 }, timeout.Token).AsTask(),
            server.ReadExactlyAsync(new byte[1], timeout.Token).AsTask());
        return CallerVisibility.ForClient(server);
    }
}
