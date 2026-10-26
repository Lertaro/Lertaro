using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfFonts = System.Windows.Media.Fonts;

namespace Lertaro.App.Services;

/// <summary>
/// The Windows font dialog, reduced to what a fallback chain needs: one family name.
/// </summary>
internal static class SystemFontPicker
{
    internal static string? Pick(string? currentFamily)
    {
        using var dialog = new FontDialog
        {
            FontMustExist = true,
            ShowEffects = false,
            ShowColor = false,
            AllowScriptChange = true,
            ShowHelp = false,
        };
        Font? seed = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(currentFamily))
            {
                try
                {
                    seed = new Font(currentFamily, 9f);
                    dialog.Font = seed;
                }
                catch (ArgumentException)
                {
                    // A family that was uninstalled since it was saved; the dialog keeps its default.
                }
            }

            var owner = ActiveWindow();
            var handle = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
            var result = handle == IntPtr.Zero
                ? dialog.ShowDialog()
                : dialog.ShowDialog(new WindowHandle(handle));
            if (result != DialogResult.OK) return null;
            using var selected = dialog.Font;
            return ResolveWpfFamilyName(selected.FontFamily.Name);
        }
        finally
        {
            seed?.Dispose();
        }
    }

    private static Window? ActiveWindow() =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;

    // GDI fills the dialog and WPF resolves the family, and the two name some families differently --
    // notably the localized names of CJK fonts. Keep the picked name when WPF already knows it;
    // otherwise return the WPF family carrying that name so the choice cannot silently degrade.
    private static string ResolveWpfFamilyName(string gdiName)
    {
        var picked = new WpfFontFamily(gdiName);
        var typeface = new Typeface(picked, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (typeface.TryGetGlyphTypeface(out _)) return picked.Source;
        foreach (var family in WpfFonts.SystemFontFamilies)
        {
            if (family.FamilyNames.Values.Contains(gdiName, StringComparer.OrdinalIgnoreCase))
                return family.Source;
        }
        return picked.Source;
    }

    private sealed class WindowHandle(IntPtr handle) : Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
