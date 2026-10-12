using System.IO;
using Lertaro.App.Services;

namespace Lertaro.App.Views.Settings;

/// <summary>
/// Resolves optional component versions for the About page. Kept separate so the view remains under the
/// repository's per-file line limit without changing how missing or unreadable components are displayed.
/// </summary>
internal static class AboutComponentVersionSupport
{
    /// <summary>
    /// Prints an assembly version with every part that is actually used.
    /// </summary>
    /// <remarks>
    /// Fork releases carry a fourth part (5.9.1.5) while upstream's are three (5.9.3). <c>ToString(3)</c> silently
    /// dropped the fork's fourth part -- the one that changes between fork releases -- so the About page showed
    /// "5.9.1" for a 5.9.1.5 build. Keeping three parts when the fourth is zero leaves upstream's own display
    /// exactly as it was.
    /// </remarks>
    internal static string FormatVersion(Version? version)
        => version == null ? string.Empty
            : version.Revision > 0 ? version.ToString(4)
            : version.ToString(3);

    internal static string GetServiceVersion() => GetVersion("Lertaro.Service.dll", "About_ServiceVersion");

    internal static string GetCliVersion() => GetVersion("lff.dll", "About_CliVersion");

    private static string GetVersion(string fileName, string translationKey)
    {
        var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        if (File.Exists(dllPath))
        {
            try
            {
                var version = System.Reflection.AssemblyName.GetAssemblyName(dllPath).Version;
                if (version != null)
                    return string.Format(TranslationManager.Instance[translationKey], FormatVersion(version));
            }
            catch
            {
                // The component may be replaced while the About page is open.
            }
        }

        return string.Format(TranslationManager.Instance[translationKey], "Unknown");
    }
}
