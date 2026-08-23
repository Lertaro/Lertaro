using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
namespace Lertaro.Plugins.CoreExtensions.Preview.Providers;
public class ImagePreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_ImageProviderName");
    public int Priority => 20;
    public bool CanPreview(string path, bool isDir)
    {
        if (isDir) return false;
        var ext = Path.GetExtension(path).ToLower();
        string[] imgExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" };
        return imgExts.Contains(ext);
    }
    public UIElement CreatePreview(string path, bool isDir)
    {
        var grid = new Grid();
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };
        border.SetResourceReference(Border.BorderBrushProperty, "SeparatorBrush");
        grid.Children.Add(border);
        var img = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        border.Child = img;
        try
        {
            var bmi = new BitmapImage();
            bmi.BeginInit();
            bmi.CacheOption = BitmapCacheOption.OnLoad;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bmi.StreamSource = stream;
                bmi.EndInit();
            }
            bmi.Freeze();
            img.Source = bmi;
        }
        catch
        {
            border.Child = new TextBlock { Text = "Failed to load image", Foreground = Brushes.Red, Margin = new Thickness(8) };
        }
        return grid;
    }
}
public class TextPreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_TextProviderName");
    public int Priority => 5;
    public bool CanPreview(string path, bool isDir)
    {
        if (isDir) return false;
        var ext = Path.GetExtension(path).ToLower();
        string[] txtExts = {
            ".txt", ".log", ".cs", ".xml", ".json", ".md", ".js", ".ts", ".py",
            ".html", ".css", ".ini", ".cfg", ".bat", ".cmd", ".sh", ".yml",
            ".yaml", ".sql", ".csproj", ".sln", ".config", ".properties"
        };
        if (txtExts.Contains(ext)) return true;
        try
        {
            var fileInfo = new FileInfo(path);
            // Under 100KB is necessary but not sufficient -- a small binary file (e.g. a small .zip) would
            // otherwise get claimed here too, pre-empting every lower-priority provider (including ones
            // that could actually preview it) only to immediately give up and hand off to
            // DefaultMetadataPreviewProvider in CreatePreview anyway. Deciding that here instead means a
            // small binary file is never claimed in the first place, so those providers get a real chance.
            if (fileInfo.Length < 102400 && !LooksBinary(path)) return true;
        }
        catch { }
        return false;
    }

    // Null bytes in the first 4KB is the same heuristic file(1) and most editors use to flag binary
    // content -- cheap, and good enough to keep archives/executables/etc. out of the text view without
    // needing a real format sniffer.
    private static bool LooksBinary(string path)
    {
        try
        {
            var buffer = new byte[4096];
            int bytesRead;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                bytesRead = fs.Read(buffer, 0, buffer.Length);
            for (var i = 0; i < bytesRead; i++)
                if (buffer[i] == 0) return true;
            return false;
        }
        catch
        {
            // Can't tell -- don't block on it here, let CreatePreview's own read surface the real error.
            return false;
        }
    }

    public UIElement CreatePreview(string path, bool isDir)
    {
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.PreviewMouseWheel += (s, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        };
        var txt = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12.5,
            Margin = new Thickness(4),
            TextWrapping = TextWrapping.Wrap
        };
        txt.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        scroll.Content = txt;
        try
        {
            if (LooksBinary(path))
            {
                return new DefaultMetadataPreviewProvider().CreatePreview(path, isDir);
            }
            string textContent;
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                var chars = new char[1500];
                var read = reader.ReadBlock(chars, 0, chars.Length);
                textContent = new string(chars, 0, read);
                if (reader.Peek() >= 0) textContent += "\r\n" + TranslationService.Get("QuickLook_Truncated");
            }
            txt.Text = textContent;
        }
        catch (Exception ex)
        {
            txt.Text = $"Error loading text: {ex.Message}";
        }
        return scroll;
    }
}
public class DefaultMetadataPreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_MetadataProviderName");
    public int Priority => 1;
    public bool CanPreview(string path, bool isDir) => isDir || File.Exists(path);
    public UIElement CreatePreview(string path, bool isDir)
    {
        // Filename and size/date already live in the QuickLook header and footer, so this fallback shows
        // just a large real thumbnail (video frame / document page / image), or a small shell icon if none.
        //
        // GetThumbnail is a synchronous shell COM call -- for a video file on a network drive, the shell
        // has to actually read/decode frame data over the network to produce it, which can take seconds
        // and, called here, blocked the whole window (and the search window under it) until it returned.
        // Show the small-icon placeholder layout immediately instead, then fetch the real thumbnail in the
        // background and restyle the same Image element into the "real thumbnail" layout once it arrives.
        var control = PePreviewProvider.BuildMetadataControl(path, null, null, null, out var img);
        Task.Run(() => IconService.GetThumbnail(path, 512)).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return;
            // No staleness check needed: img belongs only to this specific control instance. If the user
            // has since navigated away, this control (and img) is simply no longer in the visual tree, and
            // restyling it is a harmless no-op rather than something that could show a stale result.
            img.Source = t.Result;
            img.Stretch = Stretch.Uniform;
            img.HorizontalAlignment = HorizontalAlignment.Stretch;
            img.MaxHeight = 420;
            img.Width = double.NaN;
            img.Height = double.NaN;
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return control;
    }
}
