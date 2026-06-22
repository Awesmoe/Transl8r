using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace Transl8r.Interop;

/// <summary>
/// Global hotkeys via RegisterHotKey on a message-only window (HwndSource).
/// Combos look like "ctrl+alt+r". Emits <see cref="Triggered"/> with the binding
/// name on press. Mirrors the Python hotkeys.py.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private static readonly Dictionary<string, uint> Mods = new()
    {
        ["alt"] = 0x1,
        ["ctrl"] = 0x2,
        ["shift"] = 0x4,
        ["win"] = 0x8,
    };

    private HwndSource? _source;
    private readonly Dictionary<int, string> _ids = new();
    private int _nextId = 1;

    public event Action<string>? Triggered;

    private void EnsureWindow()
    {
        if (_source != null)
        {
            return;
        }
        var p = new HwndSourceParameters("transl8r_hotkeys")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: message-only window
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Replace all bindings ({name: combo}); returns combos that failed
    /// to register (already taken, or unparseable).</summary>
    public List<string> SetBindings(IDictionary<string, string> bindings)
    {
        EnsureWindow();
        IntPtr hwnd = _source!.Handle;

        foreach (int id in _ids.Keys)
        {
            NativeMethods.UnregisterHotKey(hwnd, id);
        }
        _ids.Clear();

        var failed = new List<string>();
        foreach ((string name, string combo) in bindings)
        {
            if (string.IsNullOrWhiteSpace(combo))
            {
                continue;
            }
            if (!TryParse(combo, out uint mods, out uint vk))
            {
                failed.Add(combo);
                continue;
            }
            int id = _nextId++;
            if (NativeMethods.RegisterHotKey(hwnd, id, mods | NativeMethods.MOD_NOREPEAT, vk))
            {
                _ids[id] = name;
            }
            else
            {
                failed.Add(combo);
            }
        }
        return failed;
    }

    private static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        foreach (string rawPart in combo.Split('+'))
        {
            string part = rawPart.Trim().ToLowerInvariant();
            if (part.Length == 0)
            {
                continue;
            }
            if (Mods.TryGetValue(part, out uint m))
            {
                mods |= m;
            }
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
            {
                vk = char.ToUpperInvariant(part[0]);
            }
            else if (part[0] == 'f' && int.TryParse(part[1..], out int fn) && fn is >= 1 and <= 24)
            {
                vk = (uint)(0x70 + fn - 1); // F1..F24
            }
        }
        return vk != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _ids.TryGetValue(wParam.ToInt32(), out string? name))
        {
            Triggered?.Invoke(name);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void UnregisterAll()
    {
        if (_source == null)
        {
            return;
        }
        foreach (int id in _ids.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }
        _ids.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.Dispose();
        _source = null;
    }
}
