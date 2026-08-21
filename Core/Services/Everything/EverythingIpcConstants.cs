namespace Lertaro.Core.Services.Everything;

/// <summary>Defines constants, window message IDs, and flags for the Everything IPC protocol.</summary>
public static class EverythingIpcConstants
{
    // Window class names and broadcast messages
    public const string TaskbarNotificationWndClass = "EVERYTHING_TASKBAR_NOTIFICATION";
    public const string SearchClientWndClass = "EVERYTHING";
    public const string CreatedBroadcastMessageName = "EVERYTHING_IPC_CREATED";

    // Base Window Messages
    public const uint WM_USER = 0x0400;
    public const uint WM_COPYDATA = 0x004A;
    public const uint EverythingWmIpc = WM_USER;

    // Everything WM_USER IPC command codes
    public const int IpcGetMajorVersion = 0;
    public const int IpcGetMinorVersion = 1;
    public const int IpcGetRevision = 2;
    public const int IpcGetBuildNumber = 3;
    public const int IpcExit = 4;
    public const int IpcGetTargetMachine = 5;

    public const int IpcIsStartMenuShortcuts = 300;
    public const int IpcIsQuickLaunchShortcut = 301;
    public const int IpcIsDesktopShortcut = 302;
    public const int IpcIsFolderContextMenu = 303;
    public const int IpcIsRunOnSystemStartup = 304;
    public const int IpcIsUrlProtocol = 305;
    public const int IpcIsService = 306;

    public const int IpcIsNtfsDriveIndexed = 400;
    public const int IpcIsDbLoaded = 401;
    public const int IpcIsDbBusy = 402;
    public const int IpcIsAdmin = 403;
    public const int IpcIsAppData = 404;
    public const int IpcRebuildDb = 405;
    public const int IpcUpdateAllFolderIndexes = 406;
    public const int IpcSaveDb = 407;
    public const int IpcSaveRunHistory = 408;
    public const int IpcDeleteRunHistory = 409;
    public const int IpcIsFastSort = 410;
    public const int IpcIsFileInfoIndexed = 411;
    public const int IpcQueueRebuildDb = 412;

    // Search window state commands (500-518)
    public const int IpcIsMatchCase = 500;
    public const int IpcIsMatchWholeWord = 501;
    public const int IpcIsMatchPath = 502;
    public const int IpcIsMatchDiacritics = 503;
    public const int IpcIsRegex = 504;
    public const int IpcIsFilters = 505;
    public const int IpcIsPreview = 506;
    public const int IpcIsStatusBar = 507;
    public const int IpcIsDetails = 508;
    public const int IpcGetThumbnailSize = 509;
    public const int IpcGetSort = 510;
    public const int IpcGetOnTop = 511;
    public const int IpcGetFilter = 512;
    public const int IpcGetFilterIndex = 513;

    // File info indexed types (for IpcIsFileInfoIndexed)
    public const int FileInfoFileSize = 1;
    public const int FileInfoFolderSize = 2;
    public const int FileInfoDateCreated = 3;
    public const int FileInfoDateModified = 4;
    public const int FileInfoDateAccessed = 5;
    public const int FileInfoAttributes = 6;

    // WM_COPYDATA action codes
    public const uint CopyDataCommandLineUtf8 = 0;
    public const uint CopyDataQueryA = 1;
    public const uint CopyDataQueryW = 2;
    public const uint CopyDataQuery2A = 17;
    public const uint CopyDataQuery2W = 18;
    public const uint CopyDataGetRunCountA = 19;
    public const uint CopyDataGetRunCountW = 20;
    public const uint CopyDataSetRunCountA = 21;
    public const uint CopyDataSetRunCountW = 22;
    public const uint CopyDataIncRunCountA = 23;
    public const uint CopyDataIncRunCountW = 24;

    // Target machines
    public const int TargetMachineX86 = 1;
    public const int TargetMachineX64 = 2;
    public const int TargetMachineArm = 3;
    public const int TargetMachineArm64 = 4;

    // Item flags
    public const uint ItemFlagFolder = 0x00000001;
    public const uint ItemFlagDrive = 0x00000002;
    public const uint ItemFlagRoot = 0x00000002;

    // Search query flags
    public const uint MatchCase = 0x00000001;
    public const uint MatchWholeWord = 0x00000002;
    public const uint MatchPath = 0x00000004;
    public const uint Regex = 0x00000008;
    public const uint MatchDiacritics = 0x00000010;

    // Request flags for Query2
    public const uint RequestFileName = 0x00000001;
    public const uint RequestPath = 0x00000002;
    public const uint RequestFullPathAndFileName = 0x00000004;
    public const uint RequestExtension = 0x00000008;
    public const uint RequestSize = 0x00000010;
    public const uint RequestDateCreated = 0x00000020;
    public const uint RequestDateModified = 0x00000040;
    public const uint RequestDateAccessed = 0x00000080;
    public const uint RequestAttributes = 0x00000100;
    public const uint RequestFileListFileName = 0x00000200;
    public const uint RequestRunCount = 0x00000400;
    public const uint RequestDateRun = 0x00000800;
    public const uint RequestDateRecentlyChanged = 0x00001000;
    public const uint RequestHighlightedFileName = 0x00002000;
    public const uint RequestHighlightedPath = 0x00004000;
    public const uint RequestHighlightedFullPathAndFileName = 0x00008000;

    // Sort types
    public const uint SortNameAscending = 1;
    public const uint SortNameDescending = 2;
    public const uint SortPathAscending = 3;
    public const uint SortPathDescending = 4;
    public const uint SortSizeAscending = 5;
    public const uint SortSizeDescending = 6;
    public const uint SortExtensionAscending = 7;
    public const uint SortExtensionDescending = 8;
    public const uint SortTypeNameAscending = 9;
    public const uint SortTypeNameDescending = 10;
    public const uint SortDateCreatedAscending = 11;
    public const uint SortDateCreatedDescending = 12;
    public const uint SortDateModifiedAscending = 13;
    public const uint SortDateModifiedDescending = 14;
    public const uint SortAttributesAscending = 15;
    public const uint SortAttributesDescending = 16;
    public const uint SortFileListFileNameAscending = 17;
    public const uint SortFileListFileNameDescending = 18;
    public const uint SortRunCountAscending = 19;
    public const uint SortRunCountDescending = 20;

    public const uint AllResults = 0xFFFFFFFF;
}
