using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Lertaro.Core.Services.Everything;

/// <summary>Parses raw Win32 WM_COPYDATA packets and Everything search strings into typed structures.</summary>
public static class EverythingQueryParser
{
    private const int QueryV1HeaderSize = 20;
    private const int QueryV2HeaderSize = 28;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    public static bool TryParseCopyDataQuery(IntPtr lParam, out EverythingQueryRequest? request)
    {
        request = null;
        if (lParam == IntPtr.Zero) return false;

        try
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            var actionCode = (uint)cds.dwData.ToInt64();
            if (cds.lpData == IntPtr.Zero || cds.cbData <= 0) return false;

            return actionCode switch
            {
                EverythingIpcConstants.CopyDataQueryA => TryParseV1(cds.lpData, cds.cbData, isUnicode: false, out request),
                EverythingIpcConstants.CopyDataQueryW => TryParseV1(cds.lpData, cds.cbData, isUnicode: true, out request),
                EverythingIpcConstants.CopyDataQuery2A => TryParseV2(cds.lpData, cds.cbData, isUnicode: false, out request),
                EverythingIpcConstants.CopyDataQuery2W => TryParseV2(cds.lpData, cds.cbData, isUnicode: true, out request),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParseRunHistory(IntPtr lParam, out EverythingRunHistoryRequest? request)
    {
        request = null;
        if (lParam == IntPtr.Zero) return false;

        try
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            var actionCode = (uint)cds.dwData.ToInt64();
            if (cds.lpData == IntPtr.Zero || cds.cbData <= 0) return false;

            switch (actionCode)
            {
                case EverythingIpcConstants.CopyDataGetRunCountA:
                case EverythingIpcConstants.CopyDataIncRunCountA:
                    var fileNameA = ReadNullTerminatedString(cds.lpData, 0, cds.cbData, Encoding.Default);
                    request = new EverythingRunHistoryRequest(actionCode, fileNameA);
                    return true;

                case EverythingIpcConstants.CopyDataGetRunCountW:
                case EverythingIpcConstants.CopyDataIncRunCountW:
                    var fileNameW = ReadNullTerminatedString(cds.lpData, 0, cds.cbData, Encoding.Unicode);
                    request = new EverythingRunHistoryRequest(actionCode, fileNameW);
                    return true;

                case EverythingIpcConstants.CopyDataSetRunCountA:
                    if (cds.cbData < sizeof(uint)) return false;
                    var runCountA = (uint)Marshal.ReadInt32(cds.lpData, 0);
                    var nameA = ReadNullTerminatedString(cds.lpData, sizeof(uint), cds.cbData - sizeof(uint), Encoding.Default);
                    request = new EverythingRunHistoryRequest(actionCode, nameA, runCountA);
                    return true;

                case EverythingIpcConstants.CopyDataSetRunCountW:
                    if (cds.cbData < sizeof(uint)) return false;
                    var runCountW = (uint)Marshal.ReadInt32(cds.lpData, 0);
                    var nameW = ReadNullTerminatedString(cds.lpData, sizeof(uint), cds.cbData - sizeof(uint), Encoding.Unicode);
                    request = new EverythingRunHistoryRequest(actionCode, nameW, runCountW);
                    return true;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParseCommandLine(IntPtr lParam, out uint showCommand, out string commandLine)
    {
        showCommand = 0;
        commandLine = string.Empty;
        if (lParam == IntPtr.Zero) return false;

        try
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if ((uint)cds.dwData.ToInt64() != EverythingIpcConstants.CopyDataCommandLineUtf8) return false;
            if (cds.lpData == IntPtr.Zero || cds.cbData < sizeof(uint)) return false;

            showCommand = (uint)Marshal.ReadInt32(cds.lpData, 0);
            commandLine = ReadNullTerminatedString(cds.lpData, sizeof(uint), cds.cbData - sizeof(uint), Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseV1(IntPtr ptr, int size, bool isUnicode, out EverythingQueryRequest? request)
    {
        request = null;
        if (size < QueryV1HeaderSize) return false;

        var replyHwnd = (IntPtr)Marshal.ReadInt32(ptr, 0);
        var replyCopyDataMessage = (uint)Marshal.ReadInt32(ptr, 4);
        var searchFlags = (uint)Marshal.ReadInt32(ptr, 8);
        var offset = (uint)Marshal.ReadInt32(ptr, 12);
        var maxResults = (uint)Marshal.ReadInt32(ptr, 16);

        var encoding = isUnicode ? Encoding.Unicode : Encoding.Default;
        var searchString = ReadNullTerminatedString(ptr, QueryV1HeaderSize, size - QueryV1HeaderSize, encoding);

        request = new EverythingQueryRequest(
            ReplyHwnd: replyHwnd,
            ReplyCopyDataMessage: replyCopyDataMessage,
            SearchFlags: searchFlags,
            Offset: offset,
            MaxResults: maxResults,
            RequestFlags: EverythingIpcConstants.RequestFileName | EverythingIpcConstants.RequestPath,
            SortType: EverythingIpcConstants.SortNameAscending,
            SearchString: searchString,
            IsUnicode: isUnicode,
            IsQuery2: false);
        return true;
    }

    private static bool TryParseV2(IntPtr ptr, int size, bool isUnicode, out EverythingQueryRequest? request)
    {
        request = null;
        if (size < QueryV2HeaderSize) return false;

        var replyHwnd = (IntPtr)Marshal.ReadInt32(ptr, 0);
        var replyCopyDataMessage = (uint)Marshal.ReadInt32(ptr, 4);
        var searchFlags = (uint)Marshal.ReadInt32(ptr, 8);
        var offset = (uint)Marshal.ReadInt32(ptr, 12);
        var maxResults = (uint)Marshal.ReadInt32(ptr, 16);
        var requestFlags = (uint)Marshal.ReadInt32(ptr, 20);
        var sortType = (uint)Marshal.ReadInt32(ptr, 24);

        var encoding = isUnicode ? Encoding.Unicode : Encoding.Default;
        var searchString = ReadNullTerminatedString(ptr, QueryV2HeaderSize, size - QueryV2HeaderSize, encoding);

        request = new EverythingQueryRequest(
            ReplyHwnd: replyHwnd,
            ReplyCopyDataMessage: replyCopyDataMessage,
            SearchFlags: searchFlags,
            Offset: offset,
            MaxResults: maxResults,
            RequestFlags: requestFlags,
            SortType: sortType,
            SearchString: searchString,
            IsUnicode: isUnicode,
            IsQuery2: true);
        return true;
    }

    private static string ReadNullTerminatedString(IntPtr basePtr, int offset, int maxBytes, Encoding encoding)
    {
        if (maxBytes <= 0) return string.Empty;
        var buffer = new byte[maxBytes];
        Marshal.Copy(IntPtr.Add(basePtr, offset), buffer, 0, maxBytes);

        var len = 0;
        if (encoding == Encoding.Unicode)
        {
            for (var i = 0; i + 1 < buffer.Length; i += 2)
            {
                if (buffer[i] == 0 && buffer[i + 1] == 0) break;
                len += 2;
            }
        }
        else
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == 0) break;
                len++;
            }
        }

        return len > 0 ? encoding.GetString(buffer, 0, len) : string.Empty;
    }

    public static EverythingSearchCriteria ParseSearchCriteria(string searchString)
    {
        if (string.IsNullOrWhiteSpace(searchString))
            return new EverythingSearchCriteria(string.Empty, null, null, false, false, false, false, string.Empty);

        var query = searchString.Trim();
        string? parentDir = null;
        string? ext = null;
        var folderOnly = false;
        var fileOnly = false;

        // Extract parent:"<path>" or path:"<path>"
        var parentMatch = Regex.Match(query, @"\b(?:parent|path):""([^""]+)""", RegexOptions.IgnoreCase);
        if (parentMatch.Success)
        {
            parentDir = parentMatch.Groups[1].Value.Trim();
            query = query.Remove(parentMatch.Index, parentMatch.Length).Trim();
        }
        else
        {
            var bareParentMatch = Regex.Match(query, @"\b(?:parent|path):([^\s]+)", RegexOptions.IgnoreCase);
            if (bareParentMatch.Success)
            {
                parentDir = bareParentMatch.Groups[1].Value.Trim();
                query = query.Remove(bareParentMatch.Index, bareParentMatch.Length).Trim();
            }
        }

        // Extract ext:<ext>
        var extMatch = Regex.Match(query, @"\bext:([a-zA-Z0-9_,;]+)", RegexOptions.IgnoreCase);
        if (extMatch.Success)
        {
            ext = extMatch.Groups[1].Value.Trim();
            query = query.Remove(extMatch.Index, extMatch.Length).Trim();
        }

        // Check folder: / file:
        if (Regex.IsMatch(query, @"\bfolder:\s*", RegexOptions.IgnoreCase))
        {
            folderOnly = true;
            query = Regex.Replace(query, @"\bfolder:\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
        }
        else if (Regex.IsMatch(query, @"\bfile:\s*", RegexOptions.IgnoreCase))
        {
            fileOnly = true;
            query = Regex.Replace(query, @"\bfile:\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        var rootOnly = false;
        if (Regex.IsMatch(query, @"\b(?:root|drive):\s*", RegexOptions.IgnoreCase))
        {
            rootOnly = true;
            query = Regex.Replace(query, @"\b(?:root|drive):\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        var isFolderSubtree = false;
        // If parent wasn't specified via parent: but query is an exact existing directory path
        if (string.IsNullOrEmpty(parentDir) && !rootOnly && !string.IsNullOrEmpty(query))
        {
            var candidate = query.Trim('"', '<', '>');
            if (candidate.Length >= 2 && candidate[1] == ':' && (candidate.Length == 2 || candidate[2] == '\\' || candidate[2] == '/'))
            {
                var normalized = candidate.Length == 2 ? candidate + "\\" : candidate;
                if (Directory.Exists(normalized))
                {
                    parentDir = normalized;
                    isFolderSubtree = query.Trim().EndsWith('\\') || query.Trim().EndsWith("\\\"") || query.Trim().EndsWith("/>");
                    query = string.Empty;
                }
            }
        }

        return new EverythingSearchCriteria(
            RawQuery: searchString,
            ParentDirectoryFilter: parentDir,
            ExtensionFilter: ext,
            MatchFoldersOnly: folderOnly,
            MatchFilesOnly: fileOnly,
            MatchRootsOnly: rootOnly,
            IsFolderSubtreeQuery: isFolderSubtree,
            KeywordQuery: query);
    }
}
