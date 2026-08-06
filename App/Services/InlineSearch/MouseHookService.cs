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
