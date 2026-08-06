using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Helper providing drag-restore behavior for custom window chromes,
/// allowing maximized windows to be un-maximized and repositioned seamlessly while dragging.
/// ponytail: Extracted to decouple window drag restoration logic across custom chrome windows and maintain line limits.
/// </summary>
public static class WindowMaximizedDragHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>
    /// Drag-moves the specified window when restored, or restores and seamlessly drags it when maximized.
    /// </summary>
    public static void DragMoveOrRestore(Window window, MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(e);

        if (e.LeftButton != MouseButtonState.Pressed || e.ClickCount >= 2)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            var mousePos = e.GetPosition(window);
            var percentX = window.ActualWidth > 0 ? mousePos.X / window.ActualWidth : 0.5;

            var matrix = Matrix.Identity;
            var source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget != null)
            {
                matrix = source.CompositionTarget.TransformFromDevice;
            }

            if (GetCursorPos(out var physPoint))
            {
                var screenLogicalX = physPoint.X * matrix.M11;
                var screenLogicalY = physPoint.Y * matrix.M22;

                window.WindowState = WindowState.Normal;

                var targetWidth = window.RestoreBounds.Width > 0 ? window.RestoreBounds.Width : window.Width;
                if (double.IsNaN(targetWidth) || targetWidth <= 0) targetWidth = 800;

                var grabOffset = targetWidth * percentX;
                grabOffset = Math.Max(20, Math.Min(targetWidth - 20, grabOffset));

                window.Left = screenLogicalX - grabOffset;
                window.Top = screenLogicalY - mousePos.Y;
            }
            else
            {
                window.WindowState = WindowState.Normal;
            }
        }

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // Ignore standard DragMove state exceptions when primary mouse button is released
        }
    }
}
