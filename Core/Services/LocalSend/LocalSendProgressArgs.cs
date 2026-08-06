using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Event arguments for LocalSend file transfer progress.
/// </summary>
public sealed class LocalSendProgressArgs : EventArgs
{
    public string SessionId { get; }
    public string SenderAlias { get; }
    public string FileId { get; }
    public string FileName { get; }
    public long BytesTransferred { get; }
    public long TotalBytes { get; }
    public int CurrentFileIndex { get; }
    public int TotalFiles { get; }
    public bool IsFinished { get; }
    public bool IsAllDone { get; }
    public string? SavedPath { get; }
    public string? RootSavedPath { get; }
    public long SessionBytesTransferred { get; }
    public long SessionTotalBytes { get; }

    public LocalSendProgressArgs(
        string sessionId,
        string senderAlias,
        string fileId,
        string fileName,
        long bytesTransferred,
        long totalBytes,
        int currentFileIndex,
        int totalFiles,
        bool isFinished = false,
        bool isAllDone = false,
        string? savedPath = null,
        string? rootSavedPath = null,
        long sessionBytesTransferred = 0,
        long sessionTotalBytes = 0)
    {
        SessionId = sessionId;
        SenderAlias = senderAlias;
        FileId = fileId;
        FileName = fileName;
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        CurrentFileIndex = currentFileIndex;
        TotalFiles = totalFiles;
        IsFinished = isFinished;
        IsAllDone = isAllDone;
        SavedPath = savedPath;
        RootSavedPath = rootSavedPath;
        SessionBytesTransferred = sessionBytesTransferred > 0 ? sessionBytesTransferred : bytesTransferred;
        SessionTotalBytes = sessionTotalBytes > 0 ? sessionTotalBytes : totalBytes;
    }
}

/// <summary>Reports the receiving device's HTTP result for one completed upload request.</summary>
public sealed class LocalSendFileConfirmationArgs : EventArgs
{
    public LocalSendFileConfirmationArgs(string fileId, string fileName, int fileIndex, int totalFiles,
        LocalSendSendResult result = LocalSendSendResult.Success, string? error = null)
    {
        FileId = fileId;
        FileName = fileName;
        FileIndex = fileIndex;
        TotalFiles = totalFiles;
        Result = result;
        Error = error;
    }

    public string FileId { get; }
    public string FileName { get; }
    public int FileIndex { get; }
    public int TotalFiles { get; }
    public LocalSendSendResult Result { get; }
    public string? Error { get; }
}
