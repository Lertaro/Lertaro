using System.IO;
using Lertaro.App.Services;

namespace Lertaro.App.Views.Settings;

/// <summary>
/// Resolves optional component versions for the About page. Kept separate so the view remains under the
/// repository's per-file line limit without changing how missing or unreadable components are displayed.
/// </summary>
internal static class AboutComponentVersionSupport
{
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
                    return string.Format(TranslationManager.Instance[translationKey], version.ToString(3));
            }
            catch
            {
                // The component may be replaced while the About page is open.
            }
        }

        return string.Format(TranslationManager.Instance[translationKey], "Unknown");
    }
}
