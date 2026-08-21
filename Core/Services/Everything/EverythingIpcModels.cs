namespace Lertaro.Core.Services.Everything;

/// <summary>Represents an incoming query request from an external Everything SDK or IPC client.</summary>
public sealed record EverythingQueryRequest(
    IntPtr ReplyHwnd,
    uint ReplyCopyDataMessage,
    uint SearchFlags,
    uint Offset,
    uint MaxResults,
    uint RequestFlags,
    uint SortType,
    string SearchString,
    bool IsUnicode,
    bool IsQuery2);

/// <summary>Represents a single file or directory search result item to be returned over Everything IPC.</summary>
public sealed record EverythingResultItem(
    string Path,
    string FileName,
    long Size,
    bool IsDirectory,
    bool IsDrive = false,
    DateTime? DateCreated = null,
    DateTime? DateModified = null,
    DateTime? DateAccessed = null,
    uint Attributes = 0,
    uint RunCount = 0,
    DateTime? DateRun = null,
    DateTime? DateRecentlyChanged = null);

/// <summary>Represents a run count / run history IPC request.</summary>
public sealed record EverythingRunHistoryRequest(
    uint CommandCode,
    string FileName,
    uint RunCount = 0);

/// <summary>Represents decomposed query criteria parsed from an Everything search string.</summary>
public sealed record EverythingSearchCriteria(
    string RawQuery,
    string? ParentDirectoryFilter,
    string? ExtensionFilter,
    bool MatchFoldersOnly,
    bool MatchFilesOnly,
    bool MatchRootsOnly,
    bool IsFolderSubtreeQuery,
    string KeywordQuery);
