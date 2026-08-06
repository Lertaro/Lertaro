using System.Buffers;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lertaro.App.Services.ShellIcons;

/// <summary>
/// Computes SHA1 pixel hashes for WPF BitmapSource instances to enable 
/// memory deduplication of identical icon images.
/// </summary>
public static class ShellIconImageHash
{
    public static string? GetHashFromImage(ImageSource imageSource)
    {
        if (imageSource is not BitmapSource { IsFrozen: true } image)
        {
            return null;
        }

        try
        {
            var normalized = image;
            if (normalized.Format != PixelFormats.Pbgra32)
            {
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = normalized;
                converted.DestinationFormat = PixelFormats.Pbgra32;
                converted.EndInit();
                converted.Freeze();

                normalized = converted;
            }

            const int bytesPerPixel = 4;
            var stride = normalized.PixelWidth * bytesPerPixel;
            var bufferSize = stride * normalized.PixelHeight;

            if (bufferSize <= 0)
            {
                return null;
            }

            var rentedBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                normalized.CopyPixels(rentedBuffer, stride, 0);
                var hashBytes = SHA1.HashData(rentedBuffer.AsSpan(0, bufferSize));
                return Convert.ToBase64String(hashBytes);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
        catch
        {
            return null;
        }
    }
}
