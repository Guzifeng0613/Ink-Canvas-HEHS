using InkCanvas.Platform.Interop;

namespace InkCanvas.Platform.Input;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
    NoRepeat = 0x4000,
}

/// <summary>
/// Global hotkeys registered against a window that pumps WM_HOTKEY
/// (see <see cref="Windows.LayeredInkWindow.HotkeyPressed"/>).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0xB100;

    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public bool Register(HotkeyModifiers modifiers, uint virtualKey, Action action)
    {
        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_hwnd, id, (uint)modifiers | (uint)HotkeyModifiers.NoRepeat, virtualKey))
            return false;
        _actions[id] = action;
        return true;
    }

    /// <summary>Dispatch a WM_HOTKEY id. Returns true when it was ours.</summary>
    public bool Dispatch(int id)
    {
        if (!_actions.TryGetValue(id, out var action)) return false;
        action();
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _actions.Keys) NativeMethods.UnregisterHotKey(_hwnd, id);
        _actions.Clear();
    }

    public void Dispose() => UnregisterAll();
}