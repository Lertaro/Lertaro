using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Converters;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

/// <summary>
/// Chooses the compact path text for an inline result and falls back to static text trimming when even
/// the compact form cannot fit in the path column.
/// </summary>
public static class InlinePathDisplayBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(InlinePathDisplayBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;
        textBlock.Loaded -= TextBlockLoaded;
        textBlock.Unloaded -= TextBlockUnloaded;
        textBlock.DataContextChanged -= TextBlockDataContextChanged;
        textBlock.SizeChanged -= TextBlockSizeChanged;
        if ((bool)e.NewValue)
        {
            textBlock.Loaded += TextBlockLoaded;
            textBlock.Unloaded += TextBlockUnloaded;
            textBlock.DataContextChanged += TextBlockDataContextChanged;
            textBlock.SizeChanged += TextBlockSizeChanged;
            if (textBlock.IsLoaded) Attach(textBlock);
        }
        else
        {
            Detach(textBlock);
        }
    }

    private static void TextBlockLoaded(object sender, RoutedEventArgs e) => Attach((TextBlock)sender);
    private static void TextBlockUnloaded(object sender, RoutedEventArgs e) => Detach((TextBlock)sender);
    private static void TextBlockDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueRefresh((TextBlock)sender);
    private static void TextBlockSizeChanged(object sender, SizeChangedEventArgs e) => QueueRefresh((TextBlock)sender);

    private static void Attach(TextBlock textBlock)
    {
        if (GetState(textBlock) != null) return;
        var parent = textBlock.Parent as FrameworkElement;
        var state = new State(textBlock, parent);
        SetState(textBlock, state);
        parent?.SizeChanged += state.ParentSizeChanged;
        QueueRefresh(textBlock);
    }

    private static void Detach(TextBlock textBlock)
    {
        if (GetState(textBlock) is not { } state) return;
        state.Dispose();
        SetState(textBlock, null);
    }

    private static void QueueRefresh(TextBlock textBlock)
        => textBlock.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Refresh(textBlock)));

    private static void Refresh(TextBlock textBlock)
    {
        if (GetState(textBlock) == null || textBlock.DataContext is not AppSearchResult result)
            return;

        var fullText = result.IsJumpToExplorerPath || result.ResultKind == "OpenedFolder"
            ? result.Name
            : result.ParentDir;
        var availableWidth = (textBlock.Parent as FrameworkElement)?.ActualWidth ?? 0;
        if (string.IsNullOrEmpty(fullText) || availableWidth <= 0)
            return;

        var display = InlinePathDisplayFormatter.Format(fullText, availableWidth, value => InlineTextMetrics.Measure(value, textBlock));
        TextHighlighter.SetText(textBlock, display.Text);
        textBlock.TextTrimming = display.NeedsTextTrimming
            ? TextTrimming.CharacterEllipsis
            : TextTrimming.None;
    }

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(State), typeof(InlinePathDisplayBehavior), new PropertyMetadata(null));
    private static State? GetState(DependencyObject obj) => (State?)obj.GetValue(StateProperty);
    private static void SetState(DependencyObject obj, State? value) => obj.SetValue(StateProperty, value);

    private sealed class State : IDisposable
    {
        public State(TextBlock textBlock, FrameworkElement? parent)
        {
            TextBlock = textBlock;
            Parent = parent;
        }

        public TextBlock TextBlock { get; }
        public FrameworkElement? Parent { get; }
        public void ParentSizeChanged(object? sender, SizeChangedEventArgs e) => QueueRefresh(TextBlock);
        public void Dispose() => Parent?.SizeChanged -= ParentSizeChanged;
    }
}
