using System.IO;
using Lertaro.Core;

namespace Lertaro.App.Services;

/// <summary>
/// Another Lertaro instance holds the settings-transfer lease, so this one may not share the data.
/// </summary>
/// <remarks>
/// The only failure that is honestly about other instances: <see cref="SettingsBackup.OpenSession"/> holds
/// the lease exclusively for the length of an import/export, and a shared session cannot be opened beside it.
/// </remarks>
internal sealed class SettingsLeaseConflictException(string dataDirectory, Exception inner)
    : IOException(inner.Message, inner)
{
    internal string DataDirectory { get; } = dataDirectory;
}

/// <summary>The settings session could not be opened for any other reason -- a path that is not openable,
/// refused for being a link, out of space, or otherwise unavailable.</summary>
internal sealed class SettingsSessionUnavailableException(string dataDirectory, Exception inner)
    : IOException(inner.Message, inner)
{
    internal string DataDirectory { get; } = dataDirectory;
}

/// <summary>
/// The data folder is reached through a link whose target this build does not support: a relocation onto a
/// network or removable volume. Refused before anything opens the folder, so the reason is the message rather
/// than a link refusal dressed up as a settings transfer.
/// </summary>
internal sealed class SettingsDataRootUnsupportedException(string message) : IOException(message);

/// <summary>
/// The one place that turns a settings start-up failure into what the user is told. Split out of
/// <c>App.OnStartup</c> so the wording -- and the single case that may name other instances -- is assertable
/// without a window. The old text blamed a settings transfer for every failure, which sent a user whose data
/// folder was merely relocated to another drive looking for Lertaro instances that were never running.
/// </summary>
internal static class SettingsStartupFailure
{
    internal static string Describe(Exception exception)
    {
        var directory = exception is SettingsLeaseConflictException lease ? lease.DataDirectory
            : exception is SettingsSessionUnavailableException unavailable ? unavailable.DataDirectory
            : Logger.UserDataDir;

        var headline = exception is SettingsLeaseConflictException
            ? "Another Lertaro instance is transferring settings right now, so this one cannot start.\n"
              + "Close the other Lertaro instances and try again.\n"
              + "配置传输正在进行中，另一个 Lertaro 实例正占用设置数据。请关闭其他 Lertaro 实例后重试。"
            : "Lertaro could not open its settings folder. No plugins were started.\n"
              + "Lertaro 无法打开设置数据目录，插件尚未启动。\n\n"
              + exception.Message;

        // The folder is worth a line in both cases: it is the first thing anyone needs to check, and for the
        // relocation this whole path exists for, it is the answer.
        return $"{headline}\n\n{Text.DataFolder}: {directory}"
            + (exception is SettingsLeaseConflictException ? string.Empty : "\n" + Text.RelocationHint);
    }

    /// <summary>
    /// What is shown when the data folder's own relocation cannot be supported at all -- the one case the
    /// refusal above has to name itself, because nothing else on this path may speak for it.
    /// </summary>
    internal static string DescribeUnsupportedDataRoot(string path, string reason) =>
        "Lertaro will not start with its data folder in this location.\n"
        + "Lertaro 不会在此位置启动：数据目录所在的卷不受支持。\n\n"
        + reason + "\n"
        + $"{Text.DataFolder}: {path}\n"
        + Text.SupportedHint;

    /// <summary>Kept apart from the sentence above only so the two languages stay readable side by side.</summary>
    private static class Text
    {
        internal const string DataFolder = "Data folder / 数据目录";
        internal const string RelocationHint =
            "If this folder was moved to another drive with a symlink or junction, make sure the link target "
            + "still exists and is writable.\n如果该目录是用符号链接/junction 迁移到其它盘的，请确认链接目标仍然存在且可写。";
        internal const string SupportedHint =
            "A relocated data folder is supported only when its target is a local fixed disk; move the folder "
            + "itself, or repoint the link at one.\n只支持把数据目录迁移到本地固定磁盘；请改为移动目录本身，或把链接指向本地磁盘。";
    }
}
