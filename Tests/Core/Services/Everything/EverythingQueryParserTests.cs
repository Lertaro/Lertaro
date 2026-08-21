using System.Runtime.InteropServices;
using System.Text;
using Lertaro.Core.Services.Everything;

namespace Lertaro.Core.Tests.Services.Everything;

[TestClass]
public class EverythingQueryParserTests
{
    [TestMethod]
    public void ParseSearchCriteria_DirectoryOpusParentFolderQuery_ExtractsDirectory()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(@"parent:""C:\Users\testuser\Documents""");

        Assert.AreEqual(@"C:\Users\testuser\Documents", criteria.ParentDirectoryFilter);
        Assert.AreEqual(string.Empty, criteria.KeywordQuery);
        Assert.IsFalse(criteria.MatchFoldersOnly);
        Assert.IsFalse(criteria.MatchFilesOnly);
    }

    [TestMethod]
    public void ParseSearchCriteria_FolderParentQuery_ExtractsDirectoryAndFlags()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(@"folder: parent:""D:\Projects"" report");

        Assert.AreEqual(@"D:\Projects", criteria.ParentDirectoryFilter);
        Assert.AreEqual("report", criteria.KeywordQuery);
        Assert.IsTrue(criteria.MatchFoldersOnly);
        Assert.IsFalse(criteria.MatchFilesOnly);
    }

    [TestMethod]
    public void ParseSearchCriteria_ExtensionAndFileFilter_ExtractsModifiers()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(@"file: ext:pdf budget 2026");

        Assert.IsTrue(criteria.MatchFilesOnly);
        Assert.IsFalse(criteria.MatchFoldersOnly);
        Assert.AreEqual("pdf", criteria.ExtensionFilter);
        Assert.AreEqual("budget 2026", criteria.KeywordQuery);
    }

    [TestMethod]
    public void ParseSearchCriteria_RootQuery_ExtractsMatchRootsOnly()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria("root:");

        Assert.IsTrue(criteria.MatchRootsOnly);
        Assert.AreEqual(string.Empty, criteria.KeywordQuery);
    }

    [TestMethod]
    public void ParseSearchCriteria_FolderSubtreeQuery_ExtractsIsFolderSubtreeQuery()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(@"""C:\Windows\""");

        Assert.AreEqual(@"C:\Windows\", criteria.ParentDirectoryFilter);
        Assert.IsTrue(criteria.IsFolderSubtreeQuery);
        Assert.AreEqual(string.Empty, criteria.KeywordQuery);
    }

    [TestMethod]
    public void ParseSearchCriteria_TotalCommanderDriveProbe_ExtractsMatchRootsOnly()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria("?:");

        Assert.IsTrue(criteria.MatchRootsOnly);
        Assert.AreEqual(string.Empty, criteria.KeywordQuery);
    }

    [TestMethod]
    public void ParseSearchCriteria_TotalCommanderAllDrivesQuery_ExtractsKeywordOnly()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria("?: *.png");

        Assert.IsFalse(criteria.MatchRootsOnly);
        Assert.IsNull(criteria.ParentDirectoryFilter);
        Assert.AreEqual("*.png", criteria.KeywordQuery);
    }

    [TestMethod]
    public void ParseSearchCriteria_TotalCommanderDriveRootQuery_ExtractsDriveAndKeyword()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(@"C:\ *.txt");

        Assert.AreEqual(@"C:\", criteria.ParentDirectoryFilter);
        Assert.AreEqual("*.txt", criteria.KeywordQuery);
        Assert.IsFalse(criteria.IsFolderSubtreeQuery);
    }

    [TestMethod]
    public void ParseSearchCriteria_TotalCommanderFolderQuery_ExtractsFolderAndKeyword()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(@"D:\Projects\ test");

        Assert.AreEqual(@"D:\Projects\", criteria.ParentDirectoryFilter);
        Assert.AreEqual("test", criteria.KeywordQuery);
        Assert.IsFalse(criteria.IsFolderSubtreeQuery);
    }

    [TestMethod]
    public void ParseSearchCriteria_QuotedFolderWithKeyword_ExtractsFolderAndKeyword()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(@"""C:\Program Files\"" *.exe");

        Assert.AreEqual(@"C:\Program Files\", criteria.ParentDirectoryFilter);
        Assert.AreEqual("*.exe", criteria.KeywordQuery);
        Assert.IsFalse(criteria.IsFolderSubtreeQuery);
    }

    [TestMethod]
    public void ParseSearchCriteria_TotalCommanderNopathQuery_ExtractsDirectoryAndCleanKeyword()
    {
        var criteria = EverythingQueryParser.ParseSearchCriteria(@"path:c:\ nopath:<samplefile>");

        Assert.AreEqual(@"c:\", criteria.ParentDirectoryFilter);
        Assert.AreEqual("samplefile", criteria.KeywordQuery);
        Assert.IsFalse(criteria.IsFolderSubtreeQuery);
    }

    [TestMethod]
    public void TryParseCopyDataQuery_V2UnicodeBuffer_ParsesSuccessfully()
    {
        // Layout: reply_hwnd(4), reply_id(4), search_flags(4), offset(4), max_results(4), request_flags(4), sort_type(4), string
        var searchString = @"parent:""C:\Work""";
        var strBytes = Encoding.Unicode.GetBytes(searchString + "\0");
        var totalSize = 28 + strBytes.Length;

        var buffer = Marshal.AllocHGlobal(totalSize);
        try
        {
            Marshal.WriteInt32(buffer, 0, 0x1234); // reply_hwnd
            Marshal.WriteInt32(buffer, 4, 100);    // reply_copydata_message
            Marshal.WriteInt32(buffer, 8, (int)EverythingIpcConstants.MatchPath);
            Marshal.WriteInt32(buffer, 12, 5);     // offset
            Marshal.WriteInt32(buffer, 16, 50);    // max_results
            Marshal.WriteInt32(buffer, 20, (int)(EverythingIpcConstants.RequestSize | EverythingIpcConstants.RequestFileName));
            Marshal.WriteInt32(buffer, 24, (int)EverythingIpcConstants.SortSizeDescending);
            Marshal.Copy(strBytes, 0, IntPtr.Add(buffer, 28), strBytes.Length);

            var cds = new CopyDataStructWrapper
            {
                dwData = (IntPtr)EverythingIpcConstants.CopyDataQuery2W,
                cbData = totalSize,
                lpData = buffer
            };

            var cdsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStructWrapper>());
            try
            {
                Marshal.StructureToPtr(cds, cdsPtr, false);
                var success = EverythingQueryParser.TryParseCopyDataQuery(cdsPtr, out var request);

                Assert.IsTrue(success);
                Assert.IsNotNull(request);
                Assert.AreEqual((IntPtr)0x1234, request.ReplyHwnd);
                Assert.AreEqual(100u, request.ReplyCopyDataMessage);
                Assert.AreEqual(5u, request.Offset);
                Assert.AreEqual(50u, request.MaxResults);
                Assert.AreEqual(EverythingIpcConstants.SortSizeDescending, request.SortType);
                Assert.AreEqual(searchString, request.SearchString);
                Assert.IsTrue(request.IsUnicode);
                Assert.IsTrue(request.IsQuery2);
            }
            finally
            {
                Marshal.FreeHGlobal(cdsPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStructWrapper
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }
}
