using Lertaro.Core.Hook;

using Lertaro.Core.Wire;
namespace Lertaro.App.Services;

public class KeyboardHookService : IDisposable
{
    private bool _isActive;

    public event Action<char>? OnCharacterTyped;
    public event Action? OnBackspacePressed;
    public event Action? OnEscapePressed;
    public event Action? OnEnterPressed;
    public event Action? OnUpPressed;
    public event Action? OnDownPressed;
    public event Action? OnLeftPressed;
    public event Action? OnRightPressed;
    public event Action<int>? OnCtrlNumberPressed;

    public bool IsActive => _isActive;

    private bool _isInlineSearchVisible;
    public bool IsInlineSearchVisible
    {
        get => _isInlineSearchVisible;
        set
        {
            _isInlineSearchVisible = value;
            App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.SetInlineSearchVisible, BoolVal = value });
        }
    }

    // "The inline window is on screen", as opposed to IsInlineSearchVisible above, which means "forward
    // keystrokes to me" and is cleared as soon as that window takes focus for itself. Anything that has
    // to stay in effect for as long as the window is up has to hang off this one instead.
    private bool _isInlineWindowOnScreen;
    public bool IsInlineWindowOnScreen
    {
        get => _isInlineWindowOnScreen;
        set
        {
            _isInlineWindowOnScreen = value;
            App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.SetInlineWindowOnScreen, BoolVal = value });
        }
    }

    private bool _isQuickSearchWindowVisible;
    public bool IsQuickSearchWindowVisible
    {
        get => _isQuickSearchWindowVisible;
        set
        {
            _isQuickSearchWindowVisible = value;
            App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.SetQuickSearchVisible, BoolVal = value });
        }
    }

    public KeyboardHookService(ExplorerTracker tracker)
    {
        if (App.HookClient != null)
        {
            App.HookClient.OnCharacterTyped += ch => OnCharacterTyped?.Invoke(ch);
            App.HookClient.OnBackspacePressed += () => OnBackspacePressed?.Invoke();
            App.HookClient.OnEscapePressed += () => OnEscapePressed?.Invoke();
            App.HookClient.OnEnterPressed += () => OnEnterPressed?.Invoke();
            App.HookClient.OnUpPressed += () => OnUpPressed?.Invoke();
            App.HookClient.OnDownPressed += () => OnDownPressed?.Invoke();
            App.HookClient.OnLeftPressed += () => OnLeftPressed?.Invoke();
            App.HookClient.OnRightPressed += () => OnRightPressed?.Invoke();
            App.HookClient.OnCtrlNumberPressed += num => OnCtrlNumberPressed?.Invoke(num);
        }
    }

    public void Start() => _isActive = true;

    public void Stop() => _isActive = false;

    public void Dispose()
    {
    }
}
