namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Runs an external tool somewhere that can actually talk to the tool's counterpart, for plugins whose
/// own process cannot.
/// </summary>
/// <remarks>
/// The Directory Opus collector needs this. The Hook is started ELEVATED, and an elevated
/// <c>dopusrt.exe</c> can never be answered by the unelevated Opus -- User Interface Privilege Isolation
/// blocks the reply, so the tool hangs forever and writes nothing. Starting a process at a lower
/// integrity level requires CreateProcessAsUser and a privilege the Hook does not hold, so no token
/// juggling inside the Hook can fix it. The App runs at the user's own level, so the Hook forwards the
/// request to it and the App runs the tool.
/// </remarks>
public static class ToolRunService
{
    /// <summary>
    /// Runs <paramref name="toolPath"/> as <c>/info "&lt;outputFile&gt;",paths</c> and returns what the tool
    /// wrote, or null when nothing usable was produced.
    /// </summary>
    /// <remarks>
    /// The output file is created by the caller before this is invoked, because the tool fills in a file
    /// that already exists; only its path crosses the process boundary.
    /// </remarks>
    public static Func<string, string, Task<string?>>? RunDopusPathsFunc { get; set; }
}
