namespace Lertaro.App.Services;

public class MouseHookService : IDisposable
{
    private readonly Func<int, int, bool> _checkInsideWindowCallback;

    public event Action? OnClickOutside;

    public MouseHookService(Func<int, int, bool> checkInsideWindowCallback)
    {
        _checkInsideWindowCallback = checkInsideWindowCallback;
        App.HookClient?.OnMouseClick += (x, y) =>
            {
                if (!_checkInsideWindowCallback(x, y))
                {
                    OnClickOutside?.Invoke();
                }
            };
    }

    // No-ops by design: the real low-level mouse hook is Core/Hook/InlineSearch/MouseHookService.cs, which
    // lives in the elevated Hook process and stays installed for its whole lifetime, reaching this window
    // only through App.HookClient.OnMouseClick. Start/Stop exist so callers read as if they scoped the
    // hook to a window.
    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
