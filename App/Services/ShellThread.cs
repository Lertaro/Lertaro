using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Runs shell work on its own single-threaded-apartment thread, off whatever thread asked for it.
/// </summary>
/// <remarks>
/// Every shell entry point this app uses can block without a deadline. Directory.Exists and File.Exists
/// wait out the SMB timeout on a mapped drive whose server is gone; SHParseDisplayName contacts the
/// server behind a UNC path; SHOpenFolderAndSelectItems routes through Explorer, which does not return
/// while Explorer is busy; and ShellExecuteEx (what Process.Start with UseShellExecute does) hands off to
/// whatever shell extension is registered for the target. Called inline from a key press or a menu click,
/// any of those takes the window down with no way back but Task Manager.
///
/// STA specifically, rather than the thread pool: ShellExecuteEx delegates to shell extensions that use
/// COM, and some of them require a single-threaded apartment. The pool is MTA, so work scheduled there is
/// running against that documented requirement even when it happens to work.
/// </remarks>
internal static class ShellThread
{
    public static void Run(string name, Action action)
    {
        var thread = new Thread(() =>
        {
            // An exception escaping a non-pooled thread's entry point takes the process with it, so this
            // is guarded even where the body already handles its own failures.
            try { action(); }
            catch (Exception ex) { Logger.Log($"[ShellThread] '{name}' threw: {ex.Message}", LogLevel.Error); }
        })
        {
            IsBackground = true,
            Name = name
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
