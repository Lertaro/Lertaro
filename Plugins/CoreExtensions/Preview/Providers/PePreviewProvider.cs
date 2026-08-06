using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
namespace Lertaro.Plugins.CoreExtensions.Preview.Providers;
public class PePreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_PeProviderName");
    public int Priority => 15;
    public bool CanPreview(string path, bool isDir)
    {
        if (isDir) return false;
        var ext = Path.GetExtension(path).ToLower();
        return ext == ".exe" || ext == ".dll";
    }
    public UIElement CreatePreview(string path, bool isDir)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            var arch = GetPeArchitecture(path);
            var desc = !string.IsNullOrEmpty(versionInfo.FileDescription) ? versionInfo.FileDescription : TranslationService.Get("QuickLook_PeExecutable");
            var ver = !string.IsNullOrEmpty(versionInfo.ProductVersion) ? versionInfo.ProductVersion : versionInfo.FileVersion ?? "Unknown version";
            var details = $"{TranslationService.Get("QuickLook_Version")}: {ver}\n" +
                             $"{TranslationService.Get("QuickLook_Architecture")}: {arch}\n" +
                             $"{TranslationService.Get("QuickLook_Company")}: {versionInfo.CompanyName ?? "N/A"}\n" +
                             $"{TranslationService.Get("QuickLook_Product")}: {versionInfo.ProductName ?? "N/A"}";
            return BuildMetadataControl(path, desc, details);
        }
        catch
        {
            return new DefaultMetadataPreviewProvider().CreatePreview(path, isDir);
        }
    }
    private string GetPeArchitecture(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            fs.Seek(0x3c, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            fs.Seek(peOffset, SeekOrigin.Begin);
            var peHead = br.ReadUInt32();
            if (peHead == 0x00004550)
            {
                var machineType = br.ReadUInt16();
                return machineType switch
                {
                    0x014c => "x86 (32-bit)",
                    0x8664 => "x64 (64-bit)",
                    0xaa64 => "ARM64",
                    _ => "Unknown (" + machineType.ToString("X") + ")"
                };
            }
        }
        catch { }
        return "Unknown Architecture";
    }
    public static UIElement BuildMetadataControl(string path, string? title, string? details, ImageSource? image = null)
        => BuildMetadataControl(path, title, details, image, out _);

    // imageElement: the actual Image control used for the icon/thumbnail slot, so a caller that built this
    // with image=null (a placeholder) can restyle it into the "real thumbnail" layout later once one loads
    // asynchronously, instead of only being able to swap Source (which would leave a large thumbnail stuck
    // rendering at the small placeholder icon's fixed 64x64 box).
    public static UIElement BuildMetadataControl(string path, string? title, string? details, ImageSource? image, out Image imageElement)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(16) };
        var panel = new StackPanel();
        grid.Children.Add(panel);
        Image img;
        if (image != null)
        {
            // Real thumbnail — stretch to fill the pane width (keeping aspect), capped in height.
            img = new Image
            {
                Source = image,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxHeight = 420,
                Margin = new Thickness(0, 0, 0, 16)
            };
        }
        else
        {
            // No thumbnail (generic file / executable) — a small centered shell icon, not an upscaled blur.
            img = new Image
            {
                Source = IconService.GetIcon(path, false),
                Width = 64,
                Height = 64,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            };
        }
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        imageElement = img;
        panel.Children.Add(img);
        if (!string.IsNullOrEmpty(title))
        {
            var titleText = new TextBlock
            {
                Text = title,
                TextAlignment = TextAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            panel.Children.Add(titleText);
        }
        if (!string.IsNullOrEmpty(details))
        {
            var detailsText = new TextBlock
            {
                Text = details,
                TextAlignment = TextAlignment.Center,
                FontSize = 12
            };
            detailsText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            panel.Children.Add(detailsText);
        }
        return grid;
    }
}
