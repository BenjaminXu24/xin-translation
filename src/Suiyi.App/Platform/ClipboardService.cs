using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Suiyi.App.Platform;

/// <summary>Explicit clipboard actions only; never polls or observes clipboard contents.</summary>
public sealed class ClipboardService
{
    private readonly Action<string> setText;
    private readonly Func<string> getText;
    private readonly Func<bool> containsText;
    private readonly int maxAttempts;
    private readonly int retryDelayMs;
    private readonly Dispatcher? dispatcher;

    public ClipboardService()
        : this(text => Clipboard.SetText(text, TextDataFormat.UnicodeText),
            () => Clipboard.GetText(TextDataFormat.UnicodeText),
            () => Clipboard.ContainsText(TextDataFormat.UnicodeText))
    {
        dispatcher = Application.Current?.Dispatcher;
    }

    /// <summary>Injected actions allow failure tests without reading or changing the real clipboard.</summary>
    public ClipboardService(Action<string> setText, Func<string> getText, Func<bool>? containsText = null,
        int maxAttempts = 4, int retryDelayMs = 35)
    {
        this.setText = setText ?? throw new ArgumentNullException(nameof(setText));
        this.getText = getText ?? throw new ArgumentNullException(nameof(getText));
        this.containsText = containsText ?? (() => true);
        if (maxAttempts < 1 || maxAttempts > 10) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (retryDelayMs < 0 || retryDelayMs > 500) throw new ArgumentOutOfRangeException(nameof(retryDelayMs));
        this.maxAttempts = maxAttempts;
        this.retryDelayMs = retryDelayMs;
    }

    public async Task<(bool Success, string Error)> CopyAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (false, "没有可复制的正文。");
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await InvokeAsync(() => { setText(text); return true; });
                return (true, string.Empty);
            }
            catch (ExternalException) when (attempt + 1 < maxAttempts)
            {
                await Task.Delay(retryDelayMs);
            }
            catch (ExternalException)
            {
                return (false, "剪贴板正被其他程序占用，复制失败，请重试。");
            }
            catch (ThreadStateException)
            {
                return (false, "复制失败：剪贴板需要窗口线程，请重新打开随译后重试。");
            }
            catch (Exception)
            {
                return (false, "无法写入剪贴板，请手动选中文本复制，或稍后重试。");
            }
        }
        return (false, "复制失败，请重试。");
    }

    public async Task<(bool Success, string Text, string Error)> PasteAsync()
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var result = await InvokeAsync(() => containsText() ? getText() : string.Empty);
                return result.Length == 0
                    ? (false, string.Empty, "剪贴板中没有文本。")
                    : (true, result, string.Empty);
            }
            catch (ExternalException) when (attempt + 1 < maxAttempts)
            {
                await Task.Delay(retryDelayMs);
            }
            catch (ExternalException)
            {
                return (false, string.Empty, "剪贴板正被其他程序占用，粘贴失败，请重试。");
            }
            catch (ThreadStateException)
            {
                return (false, string.Empty, "粘贴失败：剪贴板需要窗口线程，请重新打开随译后重试。");
            }
            catch (Exception)
            {
                return (false, string.Empty, "无法读取剪贴板，请手动粘贴，或稍后重试。");
            }
        }
        return (false, string.Empty, "粘贴失败，请重试。");
    }

    private Task<T> InvokeAsync<T>(Func<T> action)
    {
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
        return Task.FromResult(action());
    }
}
