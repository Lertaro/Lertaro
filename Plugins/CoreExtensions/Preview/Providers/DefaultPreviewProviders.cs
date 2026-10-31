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
    private const int PreviewCharLimit = 1500;
    // Enough bytes for PreviewCharLimit chars in any supported encoding (UTF-8 tops out at ~4 bytes
    // per char, UTF-16/GB18030 at ~2-4), so the display cap is always reached before the read cap.
    private const int PreviewByteLimit = 16 * 1024;
    private const int BinaryProbeLength = 4096;

    static TextPreviewProvider() =>
        // GB18030 (a superset of GBK/GB2312) is not in the default .NET Core encoding set; the code
        // pages provider ships in the shared framework and only needs registering once per process.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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
    // needing a real format sniffer. UTF-16 text is NUL-interleaved by definition, so a Unicode BOM or
    // the BOM-less UTF-16 shape below exempts it.
    private static bool LooksBinary(string path)
    {
        try
        {
            var buffer = new byte[BinaryProbeLength];
            int bytesRead;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                bytesRead = fs.Read(buffer, 0, buffer.Length);
            var head = buffer.AsSpan(0, bytesRead);
            if (HasUnicodeBom(head)) return false;
            if (!head.Contains((byte)0)) return false;
            return !TrySniffBomlessUtf16(head, out _);
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
            FontSize = 13,
            Margin = new Thickness(4),
            TextWrapping = TextWrapping.Wrap
        };
        txt.SetResourceReference(TextBlock.FontFamilyProperty, "AppMonospaceFontFamily");
        txt.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        scroll.Content = txt;
        try
        {
            if (LooksBinary(path))
            {
                return new DefaultMetadataPreviewProvider().CreatePreview(path, isDir);
            }
            string textContent;
            bool truncated;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var buffer = new byte[PreviewByteLimit];
                var total = 0;
                int read;
                while (total < buffer.Length && (read = fs.Read(buffer, total, buffer.Length - total)) > 0)
                    total += read;
                // Read as raw bytes rather than through StreamReader: StreamReader only falls back to
                // UTF-8 replacement characters for non-UTF-8 bytes, so a BOM-less GBK file previewed as
                // mojibake. DecodeText applies the intended precedence instead.
                (textContent, truncated) = DecodePreview(buffer, total, fs.Position < fs.Length);
            }
            if (truncated) textContent += "\r\n" + TranslationService.Get("QuickLook_Truncated");
            txt.Text = textContent;
        }
        catch (Exception ex)
        {
            txt.Text = $"Error loading text: {ex.Message}";
        }
        return scroll;
    }

    internal static (string Text, bool Truncated) DecodePreview(byte[] bytes, int length, bool hasMoreBytes)
    {
        var text = DecodeText(bytes, length, hasMoreBytes);
        return text.Length <= PreviewCharLimit ? (text, hasMoreBytes) : (text[..PreviewCharLimit], true);
    }

    // Precedence: BOM (UTF-8/UTF-16 LE/BE/UTF-32) > BOM-less UTF-16 shape > strict UTF-8 > GB18030.
    // BOM-less UTF-16 is sniffed before the byte decoders because NUL is valid UTF-8 and GBK accepts
    // any byte pair, so neither would ever reject it.
    internal static string DecodeText(byte[] bytes, int length, bool hasMoreBytes)
    {
        if (length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return DecodeChunk(Encoding.UTF32, bytes, length, 4, hasMoreBytes);
        if (length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return DecodeChunk(new UTF32Encoding(bigEndian: true, byteOrderMark: false), bytes, length, 4, hasMoreBytes);
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return TryDecodeUtf8(bytes, 3, length, hasMoreBytes) ?? DecodeChunk(Encoding.UTF8, bytes, length, 3, hasMoreBytes);
        if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return DecodeChunk(Encoding.Unicode, bytes, length, 2, hasMoreBytes);
        if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return DecodeChunk(Encoding.BigEndianUnicode, bytes, length, 2, hasMoreBytes);

        if (TrySniffBomlessUtf16(bytes.AsSpan(0, length), out var utf16))
            return DecodeChunk(utf16, bytes, length, 0, hasMoreBytes);

        // A UTF-8 file must never be misread as GB18030 (the two encodings overlap on plain ASCII
        // and on nothing else worth trusting), while GBK content almost always holds byte sequences
        // that are invalid UTF-8.
        return TryDecodeUtf8(bytes, 0, length, hasMoreBytes) ?? DecodeChunk(Encoding.GetEncoding("GB18030"), bytes, length, 0, hasMoreBytes);
    }

    private static string? TryDecodeUtf8(byte[] bytes, int offset, int length, bool hasMoreBytes)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        // A chunk cut mid-character ends in 1-3 stray bytes; drop them before concluding it isn't UTF-8.
        var maxTrim = hasMoreBytes ? Math.Min(3, length - offset) : 0;
        for (var trim = 0; trim <= maxTrim; trim++)
        {
            try { return strict.GetString(bytes, offset, length - offset - trim); }
            catch (DecoderFallbackException) { }
        }
        return null;
    }

    private static string DecodeChunk(Encoding encoding, byte[] bytes, int length, int bomLength, bool hasMoreBytes)
    {
        var count = length - bomLength;
        // Same mid-character cut, for the fixed-width UTF-16/UTF-32 code units.
        if (hasMoreBytes)
        {
            var unitSize = encoding is UnicodeEncoding ? 2 : encoding is UTF32Encoding ? 4 : 1;
            count -= count % unitSize;
        }
        return encoding.GetString(bytes, bomLength, Math.Max(0, count));
    }

    internal static bool HasUnicodeBom(ReadOnlySpan<byte> bytes) =>
        (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00) ||
        (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF) ||
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ||
        (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)));

    // BOM-less UTF-16 has no marker, but its NUL bytes sit on one lane only (ASCII text puts a NUL
    // on every high byte in LE, every low byte in BE). Requiring the other lane to be nearly NUL-free
    // keeps ordinary binary files -- which have NULs spread across both lanes -- from matching.
    internal static bool TrySniffBomlessUtf16(ReadOnlySpan<byte> bytes, out Encoding encoding)
    {
        encoding = Encoding.Unicode;
        if (bytes.Length < 8) return false;

        var pairs = bytes.Length / 2;
        var evenZeros = 0;
        var oddZeros = 0;
        for (var i = 0; i < pairs; i++)
        {
            if (bytes[2 * i] == 0) evenZeros++;
            if (bytes[2 * i + 1] == 0) oddZeros++;
        }

        var threshold = Math.Max(2, pairs / 3);
        if (oddZeros >= threshold && evenZeros * 20 <= oddZeros) return true;
        if (evenZeros >= threshold && oddZeros * 20 <= evenZeros)
        {
            encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
            return true;
        }
        return false;
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
