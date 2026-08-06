using System.Runtime.InteropServices;

namespace Lertaro.App.Services.Update;

/// <summary>
/// Picks the release asset built for the architecture this process is actually running as.
/// </summary>
/// <remarks>
/// Both update paths used to take the first asset whose name ended in ".zip", which was correct only
/// for as long as a release carried exactly one. It carries an arm64 build as well now, so "the first
/// zip" would otherwise hand an x64 machine an arm64 build and silently install it.
///
/// The x64 asset deliberately keeps the unsuffixed name. ARM64 uses the conventional hyphenated
/// architecture suffix.
///
/// ProcessArchitecture, not OSArchitecture: an x64 build running under emulation on an arm64 machine
/// reports X64, and should keep being offered x64 updates. That is what is installed and working, and
/// swapping someone onto a native build is a decision for them to make by downloading it, not something
/// to do underneath them during a silent update.
/// </remarks>
internal static class UpdateAssetSelector
{
    /// <summary>
    /// Suffix used on assets built for something other than x64, or null for an architecture that has
    /// no release build at all.
    /// </summary>
    /// <remarks>
    /// The new repository is not visible to legacy installs, so it can use the conventional hyphenated
    /// architecture suffix without affecting their asset selection.
    /// </remarks>
    internal static string? SuffixFor(Architecture architecture) => architecture switch
    {
        Architecture.X64 => string.Empty,
        Architecture.Arm64 => "-arm64",
        _ => null,
    };

    public static TAsset? SelectPortableZip<TAsset>(IEnumerable<TAsset>? assets, Func<TAsset, string> nameOf)
        where TAsset : class
        => SelectPortableZip(assets, nameOf, RuntimeInformation.ProcessArchitecture);

    /// <summary>
    /// The portable zip matching <paramref name="architecture"/>, or null when the release has none.
    /// </summary>
    /// <remarks>
    /// Null rather than a best guess on purpose. Not updating is a minor annoyance; installing a build
    /// for the wrong architecture over a working one leaves the app unable to start, and the updater
    /// runs unattended.
    /// </remarks>
    public static TAsset? SelectPortableZip<TAsset>(IEnumerable<TAsset>? assets, Func<TAsset, string> nameOf, Architecture architecture)
        where TAsset : class
    {
        if (assets == null)
            return null;

        var suffix = SuffixFor(architecture);
        if (suffix == null)
            return null;

        TAsset? match = null;
        foreach (var asset in assets)
        {
            var name = nameOf(asset) ?? string.Empty;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            var stem = name[..^".zip".Length];

            // An unsuffixed name is the x64 asset, so x64 has to reject every suffix rather than simply
            // not requiring its own -- otherwise "ends with nothing" matches the arm64 asset too.
            var isMatch = suffix.Length == 0
                ? !HasArchitectureSuffix(stem)
                : stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

            if (!isMatch)
                continue;

            // More than one candidate means the naming assumption no longer holds, and guessing between
            // them is the failure this exists to prevent.
            if (match != null)
                return null;
            match = asset;
        }

        return match;
    }

    private static bool HasArchitectureSuffix(string stem)
    {
        foreach (var architecture in Enum.GetValues<Architecture>())
        {
            var suffix = SuffixFor(architecture);
            if (!string.IsNullOrEmpty(suffix) && stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
