namespace Lertaro.Core.Wire;

public enum SearchRequestId : byte
{
    Ping = 0,
    Status = 1,
    SubscribeStatus = 9,
    Rebuild = 2,
    GetMachineSettings = 3,
    SetMachineSettings = 4,
    Search = 5,
    SearchDir = 6,
    RebuildDrive = 7,
    DeleteDriveIndex = 8,
    Initialize = 10,
    GetFileMetadata = 11,
    ClearServiceLog = 12,
    GetRecentFiles = 13,
    ClearPathCaches = 14,
    LaunchHook = 15,
    CancelDriveIndex = 16,
    // Streams a directory's entries out of the index (DirectoryFilter = the directory, Query = the
    // filename filter, Recursive = descend). Streaming, like Search/SearchDir -- not a Process() case.
    EnumerateDir = 17,

    // Asks to be told when anything under one of Directories changes, and nothing else. Streaming, and
    // deliberately its own subscription rather than a field on SubscribeStatus: a connection parked in
    // a streaming loop cannot accept another request, so the two have to be separate connections.
    //
    // The matching happens on the service's side on purpose. Changes arrive there in the thousands per
    // second (measured: ~3000 batches/s on an ordinary working C:), and the alternative -- shipping the
    // changed directories out with every status and letting each client sift them -- costs a broadcast
    // per change and cannot even be made correct, since no history small enough to send covers the
    // window between two of them. A watch list is a handful of paths sent once; a hit is rare.
    SubscribeDirectoryChanges = 18
}

public struct SearchRequestMessage
{
    public SearchRequestId Id { get; set; }
    public int Limit { get; set; }
    public int AppLimit { get; set; }
    public string? Query { get; set; }
    public string? DirectoryFilter { get; set; }
    public string? Drive { get; set; }
    public MachineSettings? MachineSettings { get; set; }
    public List<string>? DisabledAliasComponents { get; set; }
    public List<string>? FilePaths { get; set; }
    // Target directories for GetRecentFiles -- distinct from FilePaths above (individual file paths
    // for GetFileMetadata) since the two requests take different kinds of list.
    public List<string>? Directories { get; set; }
    // GetRecentFiles' max-age cutoff, in minutes.
    public int MaxAgeMinutes { get; set; }
    // LaunchHook: whether the caller wants the hook elevated (only honored if that session's user is
    // genuinely an administrator -- see HookProcessBroker).
    public bool RequestElevation { get; set; }

    // Search/SearchDir: the user's fuzzy-matching preference, carried per request because the service
    // runs as a different (elevated) identity and cannot read this user's settings file. Deliberately
    // phrased as the negative ("exact") rather than "fuzzy": this is a struct, so it cannot carry a
    // field initializer, and a caller that forgets to set it must fall back to the historical fuzzy
    // behavior -- which only the negative phrasing gives, since default(bool) is false.
    public bool ExactMatch { get; set; }

    // EnumerateDir: whether to descend into subdirectories. Same struct-default reasoning as
    // ExactMatch above -- a caller that forgets it gets the cheap single-level listing, not a
    // full subtree walk.
    public bool Recursive { get; set; }
}
