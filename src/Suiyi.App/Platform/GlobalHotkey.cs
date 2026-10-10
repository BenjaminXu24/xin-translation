using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Interop;

namespace Suiyi.App.Platform;

/// <summary>Owns one system-wide shortcut. A failed change leaves the previous shortcut active.</summary>
public sealed class GlobalHotkey : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;
    private static int nextId = 0x1100;
    private IntPtr window;
    private HwndSource? source;
    private int registrationId;
    private uint registeredModifiers;
    private uint registeredKey;
    private bool disposed;

    public event EventHandler? Pressed;
    public string? RegisteredShortcut { get; private set; }

    public bool TryRegister(IntPtr hwnd, string shortcut, out string error)
    {
        error = string.Empty;
        if (disposed)
        {
            error = "快捷键服务已关闭。";
            return false;
        }
        if (!TryParse(shortcut, out var modifiers, out var key, out error))
            return false;
        var newSource = HwndSource.FromHwnd(hwnd);
        if (hwnd == IntPtr.Zero || newSource is null || newSource.IsDisposed)
        {
            error = "窗口尚未准备好，无法注册全局快捷键。";
            return false;
        }
        if (!newSource.Dispatcher.CheckAccess())
        {
            error = "请在窗口线程上设置全局快捷键。";
            return false;
        }
        if (window == hwnd && registrationId != 0 && registeredModifiers == modifiers && registeredKey == key)
            return true;

        var newId = Interlocked.Increment(ref nextId);
        if (newId > 0xBFFF)
        {
            error = "快捷键注册次数已达上限，请重新启动随译。";
            return false;
        }
        // Register first: a conflict must not release the user's working shortcut.
        if (!RegisterHotKey(hwnd, newId, modifiers, key))
        {
            var nativeError = Marshal.GetLastWin32Error();
            var retained = registrationId != 0 ? "原快捷键保持有效。" : "当前未启用全局快捷键，可从系统托盘打开随译。";
            error = nativeError == 1409
                ? "此快捷键已被其他程序占用，请换一个组合。" + retained
                : $"无法注册此全局快捷键（Windows 错误 {nativeError}），可能被系统保留或已被占用。" + retained;
            return false;
        }
        var attachHook = newSource != source;
        try
        {
            if (attachHook)
                newSource.AddHook(WindowMessage);
        }
        catch (InvalidOperationException)
        {
            UnregisterHotKey(hwnd, newId);
            error = "窗口已关闭，无法注册全局快捷键。";
            return false;
        }

        if (registrationId != 0)
            UnregisterHotKey(window, registrationId);
        if (attachHook && source is not null && !source.IsDisposed)
            source.RemoveHook(WindowMessage);
        window = hwnd;
        source = newSource;
        registrationId = newId;
        registeredModifiers = modifiers;
        registeredKey = key;
        RegisteredShortcut = Format(modifiers, key);
        return true;
    }

    public static bool TryParse(string shortcut, out uint modifiers, out uint key, out string error)
    {
        modifiers = ModNoRepeat;
        key = 0;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            error = "请输入快捷键，例如 Ctrl+Alt+T。";
            return false;
        }
        var tokens = shortcut.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Any(string.IsNullOrWhiteSpace))
        {
            error = "快捷键格式无效，请用 + 连接按键，例如 Ctrl+Shift+F8。";
            return false;
        }
        foreach (var token in tokens)
        {
            var modifier = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" or "WINDOWS" => ModWin,
                _ => 0u
            };
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0)
                {
                    error = "快捷键中同一个修饰键不能重复。";
                    return false;
                }
                modifiers |= modifier;
                continue;
            }
            if (key != 0 || !TryParseKey(token, out key))
            {
                error = "快捷键须包含一个主键（字母、数字、F1–F24 或 Space 等），例如 Ctrl+Alt+T。";
                return false;
            }
        }
        if (key == 0 || (modifiers & ~ModNoRepeat) == 0)
        {
            error = "快捷键须包含 Ctrl、Alt、Shift 或 Win，以及一个主键。";
            return false;
        }
        return true;
    }

    public static string Normalize(string shortcut)
    {
        if (!TryParse(shortcut, out var modifiers, out var key, out var error))
            throw new ArgumentException(error, nameof(shortcut));
        return Format(modifiers, key);
    }

    private static bool TryParseKey(string token, out uint key)
    {
        var name = token.ToUpperInvariant() switch
        {
            "ESC" => "Escape",
            "BACKSPACE" => "Back",
            "PGUP" => "PageUp",
            "PGDN" or "PAGEDOWN" => "PageDown",
            "PLUS" => "OemPlus",
            "MINUS" => "OemMinus",
            _ => token
        };
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            name = "D" + name;
        if (Enum.TryParse<Key>(name, true, out var parsed) && IsMainKey(parsed))
        {
            key = (uint)KeyInterop.VirtualKeyFromKey(parsed);
            return key != 0;
        }
        key = 0;
        return false;
    }

    private static bool IsMainKey(Key key) =>
        key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.F1 and <= Key.F24
            or Key.Space or Key.Tab or Key.Enter or Key.Escape or Key.Back or Key.Delete
            or Key.Insert or Key.Home or Key.End or Key.PageUp or Key.PageDown
            or Key.Up or Key.Down or Key.Left or Key.Right or Key.OemPlus or Key.OemMinus;

    private static string Format(uint modifiers, uint key)
    {
        var parts = new List<string>(5);
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        var mainKey = KeyInterop.KeyFromVirtualKey((int)key);
        parts.Add(mainKey is >= Key.D0 and <= Key.D9 ? mainKey.ToString()[1..] : mainKey.ToString());
        return string.Join("+", parts);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && registrationId != 0 && wParam.ToInt64() == registrationId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (registrationId != 0)
            UnregisterHotKey(window, registrationId);
        registrationId = 0;
        if (source is not null && !source.IsDisposed)
            source.RemoveHook(WindowMessage);
        source = null;
        RegisteredShortcut = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
