using Application = System.Windows.Application;
using Lertaro.App.Helpers;

namespace Lertaro.App.Services;

/// <summary>
/// Routes keyboard hook events from <see cref="KeyboardHookService"/> to the active
/// <see cref="InlineSearchWindow"/>, keeping all navigation/input logic out of InlineSearchManager.
/// </summary>
internal sealed class InlineSearchKeyboardEventRouter
{
    private readonly KeyboardHookService _keyboardHook;
    private readonly Func<InlineSearchWindow?> _getWindow;
    private readonly Action<char> _onCharacterTyped;
    private readonly Action _onBackspacePressed;

    public InlineSearchKeyboardEventRouter(
        KeyboardHookService keyboardHook,
        Func<InlineSearchWindow?> getWindow,
        Action<char> onCharacterTyped,
        Action onBackspacePressed)
    {
        _keyboardHook = keyboardHook;
        _getWindow = getWindow;
        _onCharacterTyped = onCharacterTyped;
        _onBackspacePressed = onBackspacePressed;
    }

    public void Wire()
    {
        _keyboardHook.OnCharacterTyped += ch => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window != null && window.MenuPresenter.IsInActionsMode)
                {
                    window.SearchTextBox.AppendText(ch.ToString());
                    return;
                }
                _onCharacterTyped(ch);
            }));



        _keyboardHook.OnBackspacePressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window != null && window.MenuPresenter.IsInActionsMode)
                {
                    var text = window.SearchTextBox.Text;
                    if (text.Length > 0)
                    {
                        window.SearchTextBox.Text = text.Substring(0, text.Length - 1);
                        window.SearchTextBox.CaretIndex = window.SearchTextBox.Text.Length;
                    }
                    return;
                }
                _onBackspacePressed();
            }));

        _keyboardHook.OnEscapePressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window != null && window.MenuPresenter.IsInActionsMode)
                    SearchInputHelper.HandleActionsEscape(window, window.MenuPresenter);
                else
                    InlineSearchManager.Instance.CloseInlineSearch();
            }));

        _keyboardHook.OnLeftPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window != null && window.MenuPresenter.IsInActionsMode)
                    window.MenuPresenter.GoBackMenuOrExit();
            }));

        _keyboardHook.OnRightPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window == null) return;
                if (window.MenuPresenter.IsInActionsMode)
                    window.MenuPresenter.EnterSubMenu();
                else if (window.LstResults.SelectedItem is AppSearchResult result)
                    window.MenuPresenter.EnterActionsMode(result);
            }));

        _keyboardHook.OnEnterPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window == null) return;
                if (window.MenuPresenter.IsInActionsMode)
                {
                    window.MenuPresenter.ExecuteSelectedAction();
                    return;
                }

                if (window.LstResults.SelectedItem is AppSearchResult result)
                {
                    window.ExecuteSearchResult(result);
                }
                else if (window.LstResults.Items.Count > 0)
                {
                    window.LstResults.SelectedIndex = 0;
                    if (window.LstResults.SelectedItem is AppSearchResult firstResult)
                        window.ExecuteSearchResult(firstResult);
                }
            }));

        _keyboardHook.OnUpPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window == null) return;
                if (window.MenuPresenter.IsInActionsMode)
                {
                    window.MenuPresenter.NavigateActionsList(-1);
                    return;
                }

                MoveResultSelection(window, -1);
            }));

        _keyboardHook.OnDownPressed += () => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _getWindow();
                if (window == null) return;
                if (window.MenuPresenter.IsInActionsMode)
                {
                    window.MenuPresenter.NavigateActionsList(1);
                    return;
                }

                MoveResultSelection(window, 1);
            }));

        _keyboardHook.OnCtrlNumberPressed += num => Application.Current.Dispatcher.BeginInvoke(new Action(() => _getWindow()?.LaunchByShortcutIndex(num)));
    }

    // Wraps like the actions list's NavigateActionsList (ShellMenuPresenter.cs) -- past the last item
    // goes back to the first, and vice versa -- matching InlineSearchWindowInputHandler.MoveResultSelection,
    // used when the window doesn't have WPF focus and this global hook drives it instead.
    private static void MoveResultSelection(InlineSearchWindow window, int direction)
    {
        var count = window.LstResults.Items.Count;
        if (count == 0) return;
        var next = ListSelectionNavigator.NextSelectable(window.LstResults.SelectedIndex, direction, count,
            i => window.LstResults.Items[i] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader);
        if (next < 0) return;

        window.LstResults.SelectedIndex = next;
        window.LstResults.ScrollIntoView(window.LstResults.SelectedItem);
    }
}
