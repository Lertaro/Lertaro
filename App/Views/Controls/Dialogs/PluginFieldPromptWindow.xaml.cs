using System.Windows;
using System.Windows.Input;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

using Lertaro.App.Services.Theme;
using Lertaro.App.Helpers.Visuals;
using Application = System.Windows.Application;
namespace Lertaro.App.Views.Controls.Dialogs;

// Backs PluginSdk's PluginPromptService.PromptFunc -- lets a plugin collect a few values from the
// user at runtime (e.g. "name this before adding it") using the exact same field schema/rendering
// the real Settings -> Plugins -> Configure dialog uses (FieldRowTemplate.xaml, merged in the XAML),
// without ever reading from or writing to that plugin's actual persisted settings.
public partial class PluginFieldPromptWindow : Window
{
    private PluginFieldPromptWindow(string title, List<PluginConfigFieldViewModel> fieldViewModels)
    {
        InitializeComponent();
        // The window kind is fixed for the life of the handle, so it is declared opaque in XAML. Rounding
        // the real window is the whole of it: this dialog declares its own painted radius, and the nested
        // RoundedClip that keeps content inside it must keep agreeing with it.
        DwmWindowCorners.ApplyRound(this);
        SystemMenuBlocker.Attach(this);
        AltTabExcluder.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo);

        TxtTitle.Text = string.IsNullOrEmpty(title) ? "Lertaro" : title;
        FieldsControl.ItemsSource = fieldViewModels;

        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) => TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (FieldsControl.ItemsSource is IEnumerable<PluginConfigFieldViewModel> fields)
        {
            foreach (var f in fields)
            {
                f.NotifyLanguageChanged();
            }
        }
    }

    private bool _isSaved;
    private bool _initialEditorFocused;

    /// <summary>
    /// Re-fits the window after its content changed height, and recentres it on its owner.
    /// </summary>
    /// <remarks>
    /// Called by FieldRowTemplate when an array field's detail panel resizes. It used to live on
    /// PluginConfigWindow, which was the only host that had it; that window is gone now that plugin
    /// config renders inline in its own card, where the settings page simply scrolls. This window has the
    /// same shape (the same ContentRow, switched from Auto to Star once SizeToContent has locked in a
    /// real size), so the behaviour moved here rather than being lost.
    /// </remarks>
    public void ResizeToFit()
    {
        if (SizeToContent != SizeToContent.Manual) return;

        // Temporarily reset row height to Auto to avoid the WPF degenerate size-to-content case, and drop the
        // width/height the last fit locked in -- SizeToContent only gets to pick a new size while they are
        // unset. Window_Loaded re-locks both, at the new size, on the way back out.
        ContentRow.Height = GridLength.Auto;
        ClearValue(WidthProperty);
        ClearValue(HeightProperty);
        SizeToContent = SizeToContent.WidthAndHeight;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLayout();
            SizeToContent = SizeToContent.Manual;
            Width = ActualWidth;
            Height = ActualHeight;
            ContentRow.Height = new GridLength(1, GridUnitType.Star);

            if (Owner != null)
            {
                Left = Owner.Left + (Owner.ActualWidth - Width) / 2;
                Top = Owner.Top + (Owner.ActualHeight - Height) / 2;
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    // Mirrors PluginConfigWindow.Window_Loaded: SizeToContent has already computed a real, finite
    // size against ContentRow's initial Auto height by the time this fires (deferred to ContextIdle so
    // every nested element's own Loaded/layout pass has settled first) -- the size is then locked to
    // that value, switching to Star lets the scroll area actually fill the window's height, and this
    // recenters against Owner since WindowStartupLocation="CenterOwner" positioned it using a stale size.
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Take the OS foreground, not merely WPF focus. Keys pressed while the inline window is up travel
        // through the HOOK process, which decides whether a keystroke is inline-search input by asking which
        // window is FOREGROUND. Opened over Explorer/DOpus, this dialog would leave them foreground, so every
        // character typed into the box below was ALSO forwarded to the App as a search character and re-ran
        // the search behind the dialog. Reuses the same elevated handoff the quick/inline windows rely on to
        // beat Windows' foreground-lock (see QuickSearchWindowNative.ForceForeground); deliberately here in
        // Loaded rather than before ShowModal, since a window that is not yet visible cannot be made
        // foreground.
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            _ = Task.Run(() => QuickSearchWindow.Helpers.QuickSearchWindowNative.ForceForeground(handle));

        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLayout();

            // SizeToContent="WidthAndHeight" clamps only the WINDOW to MinWidth/MinHeight/MaxWidth/MaxHeight
            // and arranges the content at its own, possibly smaller, desired size in the top-left corner. The
            // leftover strip is then painted with the window's class background -- black, since this window is
            // opaque. Measured at 14 DIP along the right and bottom edges for a short prompt, and absent once
            // the size is Manual. Lock in the size it has just settled on; ResizeToFit's guard expects Manual.
            SizeToContent = SizeToContent.Manual;
            Width = ActualWidth;
            Height = ActualHeight;

            ContentRow.Height = new GridLength(1, GridUnitType.Star);

            if (Owner != null)
            {
                // Width/Height, not ActualWidth/ActualHeight: those still report the pre-lock size until the
                // layout pass these assignments triggered has run.
                Left = Owner.Left + (Owner.ActualWidth - Width) / 2;
                Top = Owner.Top + (Owner.ActualHeight - Height) / 2;
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (_initialEditorFocused) return;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!IsActive || _initialEditorFocused) return;

            // Prefer the field that asked for a selection (RenameAction preselects the name so typing
            // replaces it), but fall back to the FIRST editable field. Requiring a selection meant a
            // prompt whose field has none -- "mkdir"/"touch" asking for a new name -- focused nothing at
            // all, so the dialog opened with the caret nowhere and the user had to click before typing.
            var editors = FindVisualChildren<System.Windows.Controls.TextBox>(FieldsControl).ToList();
            var editor = editors.FirstOrDefault(textBox => textBox.DataContext is PluginConfigFieldViewModel { SelectionLength: > 0 })
                         ?? editors.FirstOrDefault();
            if (editor is null) return;

            editor.Focus();
            Keyboard.Focus(editor);
            if (editor.DataContext is PluginConfigFieldViewModel { SelectionLength: > 0 } field)
            {
                var start = Math.Clamp(field.SelectionStart, 0, editor.Text.Length);
                var length = Math.Clamp(field.SelectionLength, 0, editor.Text.Length - start);
                editor.Select(start, length);
            }
            else
            {
                // Nothing to preselect: put the caret at the end so typing appends to a prefilled default
                // rather than silently overwriting it.
                editor.CaretIndex = editor.Text.Length;
            }
            _initialEditorFocused = true;
        }));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ScrollViewer scrollViewer && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (e.Delta < 0) scrollViewer.LineRight();
            else scrollViewer.LineLeft();
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        Close();
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        _isSaved = true;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    public static IReadOnlyDictionary<string, object?>? ShowPrompt(
        string title,
        IReadOnlyList<PluginConfigField> fields,
        IReadOnlyDictionary<string, object?>? initialValues)
    {
        if (Application.Current == null) return null;

        return Application.Current.Dispatcher.CheckAccess()
            ? ShowInternal(title, fields, initialValues)
            : Application.Current.Dispatcher.Invoke(() => ShowInternal(title, fields, initialValues));
    }

    private static IReadOnlyDictionary<string, object?>? ShowInternal(
        string title,
        IReadOnlyList<PluginConfigField> fields,
        IReadOnlyDictionary<string, object?>? initialValues)
    {
        // A throwaway instance, never the real loaded UserSettings singleton, and never .Save()d --
        // makes it structurally impossible for this prompt to read from or write to any plugin's real
        // settings, on top of PluginConfigFieldViewModel's own "detached mode" (see below) already
        // being behaviorally safe on its own.
        var throwawaySettings = new UserSettings();
        var fieldViewModels = fields.Select(f =>
        {
            var effectiveField = f;
            if (initialValues != null && initialValues.TryGetValue(f.Key, out var initial) && initial != null)
            {
                // Clone rather than mutate the caller's field definition -- it may be a static/reused
                // PluginConfigField shown with a different initial value on each prompt (e.g. a
                // different folder name every time "Add Current Folder" is clicked).
                effectiveField = new PluginConfigField
                {
                    Key = f.Key,
                    GroupKey = f.GroupKey,
                    LabelKey = f.LabelKey,
                    DescriptionKey = f.DescriptionKey,
                    FieldType = f.FieldType,
                    DefaultValue = initial,
                    Choices = f.Choices,
                    ChoiceOptions = f.ChoiceOptions,
                    SubFields = f.SubFields,
                    RequireModifier = f.RequireModifier,
                    RequireNonEmpty = f.RequireNonEmpty,
                    MaxLength = f.MaxLength,
                    SelectionStart = f.SelectionStart,
                    SelectionLength = f.SelectionLength
                };
            }
            // onValueChanged non-null puts the field VM in "detached" mode: LocalValueStore starts
            // from DefaultValue instead of Settings.GetPluginSetting, and Commit() never calls
            // Settings.SetPluginSetting -- see PluginConfigFieldViewModel.LocalValueStore/Commit.
            return new PluginConfigFieldViewModel("__plugin_prompt__", effectiveField, throwawaySettings, onValueChanged: () => { });
        }).ToList();

        var win = new PluginFieldPromptWindow(title, fieldViewModels);

        win.Owner = OwnedDialog.ResolveOwner(win);
        win.WindowStartupLocation = win.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        // Not win.ShowDialog(): see OwnedDialog.ShowModal for what an owner closing underneath a modal
        // dialog does to the rest of the app. _isSaved stays false, so this reads as a cancel.
        OwnedDialog.ShowModal(win);
        if (!win._isSaved) return null;

        foreach (var vm in fieldViewModels)
        {
            // A no-op for simple fields (Text/Boolean/...) in detached mode -- only Object/Array
            // fields need this to flatten their Children/ArrayItems into LocalValueStore before Value
            // is read below.
            vm.Commit();
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < fields.Count; i++)
        {
            result[fields[i].Key] = fieldViewModels[i].Value;
        }
        return result;
    }
}
