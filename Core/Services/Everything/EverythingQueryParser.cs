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
                EverythingIpcConstants.CopyDataQueryA => TryParseQuery(cds.lpData, cds.cbData, isUnicode: false, isV2: false, out request),
                EverythingIpcConstants.CopyDataQueryW => TryParseQuery(cds.lpData, cds.cbData, isUnicode: true, isV2: false, out request),
                EverythingIpcConstants.CopyDataQuery2A => TryParseQuery(cds.lpData, cds.cbData, isUnicode: false, isV2: true, out request),
                EverythingIpcConstants.CopyDataQuery2W => TryParseQuery(cds.lpData, cds.cbData, isUnicode: true, isV2: true, out request),
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

    private static bool TryParseQuery(IntPtr ptr, int size, bool isUnicode, bool isV2, out EverythingQueryRequest? request)
    {
        request = null;
        var headerSize = isV2 ? QueryV2HeaderSize : QueryV1HeaderSize;
        if (size < headerSize) return false;

        var replyHwnd = (IntPtr)Marshal.ReadInt32(ptr, 0);
        var replyCopyDataMessage = (uint)Marshal.ReadInt32(ptr, 4);
        var searchFlags = (uint)Marshal.ReadInt32(ptr, 8);
        var offset = (uint)Marshal.ReadInt32(ptr, 12);
        var maxResults = (uint)Marshal.ReadInt32(ptr, 16);
        var requestFlags = isV2 ? (uint)Marshal.ReadInt32(ptr, 20) : EverythingIpcConstants.RequestFileName | EverythingIpcConstants.RequestPath;
        var sortType = isV2 ? (uint)Marshal.ReadInt32(ptr, 24) : 0u;

        var encoding = isUnicode ? Encoding.Unicode : Encoding.Default;
        var searchString = ReadNullTerminatedString(ptr, headerSize, size - headerSize, encoding);

        request = new EverythingQueryRequest(
            replyHwnd, replyCopyDataMessage, searchFlags, offset, maxResults, requestFlags, sortType, searchString, isUnicode, isV2);
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
        if (IsRootDriveWildcard(query))
        {
            return new EverythingSearchCriteria(searchString, null, null, false, false, true, false, string.Empty);
        }

        string? parentDir = null;
        string? ext = null;
        var folderOnly = false;
        var fileOnly = false;

        var parentMatch = Regex.Match(query, @"\b(?:parent|path):""([^""]+)""", RegexOptions.IgnoreCase);
        if (parentMatch.Success)
        {
            parentDir = parentMatch.Groups[1].Value.Trim();
            query = query.Remove(parentMatch.Index, parentMatch.Length).Trim();
        }
        else
        {
            var bareMatch = Regex.Match(query, @"\b(?:parent|path):([^\s]+)", RegexOptions.IgnoreCase);
            if (bareMatch.Success)
            {
                parentDir = bareMatch.Groups[1].Value.Trim();
                query = query.Remove(bareMatch.Index, bareMatch.Length).Trim();
            }
        }

        var extMatch = Regex.Match(query, @"\bext:([a-zA-Z0-9_,;]+)", RegexOptions.IgnoreCase);
        if (extMatch.Success)
        {
            ext = extMatch.Groups[1].Value.Trim();
            query = query.Remove(extMatch.Index, extMatch.Length).Trim();
        }

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

        if (query.StartsWith("?:*", StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith("?:/", StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith("?:\\", StringComparison.OrdinalIgnoreCase) ||
            query.StartsWith("?: ", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Length > 3 ? query.Substring(3).Trim() : string.Empty;
        }

        query = Regex.Replace(query, @"\b(?:nopath|name|exact):<([^>]+)>", "$1", RegexOptions.IgnoreCase);
        query = Regex.Replace(query, @"\b(?:nopath|name|exact):([^\s]+)", "$1", RegexOptions.IgnoreCase);
        query = Regex.Replace(query, @"<([^>]+)>", "$1");
        query = Regex.Replace(query, @"\b(?:nocase|nowholeword|noregex|case|wholeword|regex):\s*", string.Empty, RegexOptions.IgnoreCase).Trim();

        var isFolderSubtree = false;
        if (string.IsNullOrEmpty(parentDir) && !rootOnly && !string.IsNullOrEmpty(query))
        {
            (parentDir, query, isFolderSubtree) = ExtractLeadingPath(query);
        }

        return new EverythingSearchCriteria(
            searchString, parentDir, ext, folderOnly, fileOnly, rootOnly, isFolderSubtree, query);
    }

    private static bool IsRootDriveWildcard(string q) =>
        q.Equals("?:", StringComparison.OrdinalIgnoreCase) ||
        q.Equals("?:/", StringComparison.OrdinalIgnoreCase) ||
        q.Equals("?:\\", StringComparison.OrdinalIgnoreCase) ||
        q.Equals("?:*", StringComparison.OrdinalIgnoreCase);

    private static (string? parentDir, string remainingQuery, bool isFolderSubtree) ExtractLeadingPath(string query)
    {
        if (query.StartsWith('"'))
        {
            var closingQuote = query.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                var quotedPath = query.Substring(1, closingQuote - 1).Trim();
                if (quotedPath.Length >= 2 && quotedPath[1] == ':')
                {
                    var rest = query.Substring(closingQuote + 1).Trim();
                    return (quotedPath, rest, string.IsNullOrEmpty(rest) && (quotedPath.EndsWith('\\') || quotedPath.EndsWith('/')));
                }
            }
        }
        else
        {
            var firstSpace = query.IndexOf(' ');
            var pathToken = firstSpace > 0 ? query.Substring(0, firstSpace).Trim() : query;
            if (pathToken.Length >= 2 && char.IsLetter(pathToken[0]) && pathToken[1] == ':')
            {
                var cleanPath = pathToken.TrimEnd('*');
                if (cleanPath.Length == 2) cleanPath += "\\";
                var rest = firstSpace > 0 ? query.Substring(firstSpace + 1).Trim() : string.Empty;
                var isSubtree = string.IsNullOrEmpty(rest) && (query.EndsWith('\\') || query.EndsWith('/'));
                return (cleanPath, rest, isSubtree);
            }
        }
        return (null, query, false);
    }
}
