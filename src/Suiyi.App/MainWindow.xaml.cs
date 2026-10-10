using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Suiyi.Core;
using Suiyi.App.Platform;
using ChatLanguage = Suiyi.Core.Language;

namespace Suiyi.App;

public partial class MainWindow : Window
{
    private AppSettings settings;
    private string apiKey = ""; // Never part of settings serialization; optional persistence uses Windows DPAPI.
    private readonly CredentialStore credentials;
    private readonly string settingsPath;
    private readonly Func<string, string, bool> confirmService;
    private readonly Func<ServiceOptions, ITranslationService> serviceFactory;
    private readonly GlobalHotkey hotkey = new();
    private readonly ClipboardService clipboard = new();
    private TrayHost? tray;
    private CancellationTokenSource? active;
    private readonly LatestRequestGate requests = new();
    private bool exiting;
    private bool composing;
    private bool ready;
    private TranslationResult? result;

    public MainWindow() : this(null, null) { }

    // Explicit dependencies let tests use fixture directories without reading real credentials or sending text.
    public MainWindow(AppSettings? initialSettings, CredentialStore? credentialStore, string? settingsFilePath = null,
        Func<string, string, bool>? confirmService = null, Func<ServiceOptions, ITranslationService>? serviceFactory = null)
    {
        settingsPath = settingsFilePath ?? SettingsStore.FilePath;
        string warning = "";
        settings = initialSettings ?? SettingsStore.Load(settingsPath, out warning);
        credentials = credentialStore ?? new CredentialStore();
        this.confirmService = confirmService ?? ConfirmService;
        this.serviceFactory = serviceFactory ?? (options => new ChatCompletionsService(options));
        if (settings.RememberApiKey && !credentials.TryLoad(settings.Endpoint, out apiKey, out string keyWarning))
            warning = keyWarning.Length > 0 ? keyWarning : "此服务尚无保存的密钥，请在设置中输入并勾选记住密钥。";
        InitializeComponent();
        SourceCombo.ItemsSource = new[] { "自动识别", "中文", "English", "Filipino / Tagalog" };
        TargetCombo.ItemsSource = new[] { "自动（中→英，其余→中）", "中文", "English", "Filipino / Tagalog" };
        ToneCombo.ItemsSource = new[] { "即时聊天", "正式" };
        SourceCombo.SelectedIndex = TargetCombo.SelectedIndex = ToneCombo.SelectedIndex = 0;
        TextCompositionManager.AddPreviewTextInputStartHandler(InputBox, (_, _) => composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(InputBox, (_, _) => composing = false);
        InputBox.LostKeyboardFocus += (_, _) => composing = false;
        SourceInitialized += (_, _) =>
        {
            if (!hotkey.TryRegister(new WindowInteropHelper(this).Handle, settings.Shortcut, out string error)) SetStatus(error, true);
            UpdateMode();
            hotkey.Pressed += (_, _) => Dispatcher.Invoke(ShowFromTray);
            tray = new TrayHost(() => Dispatcher.Invoke(ShowFromTray), () => Dispatcher.Invoke(ExitApp));
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HideToTray(); };
        ready = true;
        UpdateMode();
        if (warning.Length > 0) SetStatus(warning, true);
        Loaded += (_, _) => InputBox.Focus();
    }

    private void UpdateMode()
    {
        ModeText.Text = string.IsNullOrWhiteSpace(apiKey)
            ? "模拟模式 · 未设置 API Key。仅用于验证界面与固定样例，不能作为真实翻译验收。"
            : $"API 模式 · 文本与术语表将发送至 {settings.Endpoint}；可能产生 API 费用。";
        string shortcutText = hotkey.RegisteredShortcut is string shortcut ? $"{shortcut} 唤起" : "全局快捷键未启用，可从托盘打开";
        string keyState = string.IsNullOrWhiteSpace(apiKey) ? "未设置密钥" : settings.RememberApiKey ? "密钥已安全保存" : "密钥仅本次会话";
        FooterText.Text = $"{shortcutText} · 关闭窗口收回托盘 · 不保存翻译历史 · {keyState}";
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush(error ? Color.FromRgb(163, 58, 45) : Color.FromRgb(48, 101, 83));
    }

    private void InvalidateRequest()
    {
        requests.Cancel();
        active?.Cancel();
        active = null;
        if (CancelButton is not null) CancelButton.IsEnabled = false;
    }

    private void ClearResult()
    {
        result = null;
        SegmentsPanel.Children.Clear();
        OptimizedPanel.Visibility = Visibility.Collapsed;
        OptimizedBox.Text = "";
        CopyButton.IsEnabled = false;
        EmptyText.Visibility = Visibility.Visible;
        ResultTitle.Text = "译文";
    }

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        if (!ready) return;
        InvalidateRequest();
        ClearResult();
        CountText.Text = $"{InputBox.Text.Length:N0} / 20,000";
        var lang = LanguageDetector.Detect(InputBox.Text);
        DetectedText.Text = InputBox.Text.Length == 0 ? "输入聊天内容" : $"检测：{LanguageName(lang)} · 混合内容可手动指定";
        SetStatus("准备就绪");
    }
    private void Options_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        InvalidateRequest();
        ClearResult();
        SetStatus("语言或风格已更新，点击翻译应用。 ");
    }
    private static string LanguageName(ChatLanguage language) => language switch
    {
        ChatLanguage.Chinese => "中文", ChatLanguage.English => "English", ChatLanguage.Filipino => "Filipino / Tagalog", _ => "自动"
    };

    private async void Translate_Click(object sender, RoutedEventArgs e) => await TranslateAsync();
    private async Task TranslateAsync()
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text)) { SetStatus("请先输入需要翻译的内容。", true); InputBox.Focus(); return; }
        InvalidateRequest();
        ClearResult();
        bool mock = string.IsNullOrWhiteSpace(apiKey);
        bool confirmationSaved = true;
        if (!mock)
        {
            if (string.IsNullOrWhiteSpace(settings.Model)) { SetStatus("请在设置中填写你有权限使用的模型名。", true); return; }
            if (!ServiceConsent.IsAccepted(settings, settings.Endpoint))
            {
                if (!confirmService(settings.Endpoint, settings.Model)) { SetStatus("未发送。你可以在设置中更换服务或清空密钥使用模拟模式。"); return; }
                try { ServiceConsent.Accept(settings, settings.Endpoint); }
                catch (ArgumentException) { SetStatus("服务地址无效，请在设置中填写完整 HTTPS 接口地址。", true); return; }
                confirmationSaved = SettingsStore.Save(settings, settingsPath);
            }
        }
        using var cts = new CancellationTokenSource();
        var lease = requests.Begin(cts.Token);
        active = cts;
        CancelButton.IsEnabled = true;
        SetStatus(mock ? "正在运行模拟样例…" : "正在翻译…可取消或发起新请求。");
        var request = new TranslationRequest(InputBox.Text, (ChatLanguage)SourceCombo.SelectedIndex,
            (ChatLanguage)TargetCombo.SelectedIndex, (Tone)ToneCombo.SelectedIndex, settings.Glossary.ToArray());
        ITranslationService? service = null;
        try
        {
            service = mock ? new MockTranslationService()
                : serviceFactory(new ServiceOptions(settings.Endpoint, settings.Model, apiKey, TimeSpan.FromSeconds(settings.TimeoutSeconds)));
            var value = await service.TranslateAsync(request, cts.Token);
            if (!requests.IsCurrent(lease) || !IsVisible || exiting) return;
            result = value;
            RenderResult(value);
            string complete = value.IsMock ? "模拟流程完成 · 固定样例 / 占位输出，不代表模型翻译质量。" : "翻译完成。默认复制目标语言译文。";
            SetStatus(confirmationSaved ? complete : complete + " 发送目标确认仅本次有效，无法保存到本地，重启后可能再次提示。", !confirmationSaved);
        }
        catch (OperationCanceledException) { if (requests.IsCurrent(lease)) SetStatus("已取消。"); }
        catch (Exception error)
        {
            if (!requests.IsCurrent(lease) || !IsVisible || exiting) return;
            // Only display our sanitized domain exception. Unknown errors may contain sensitive data.
            SetStatus(error is TranslationException ? error.Message : "翻译失败。请检查接口配置与网络，然后重试。", true);
        }
        finally
        {
            (service as IDisposable)?.Dispose();
            if (requests.IsCurrent(lease)) { active = null; CancelButton.IsEnabled = false; }
        }
    }
    private bool ConfirmService(string endpoint, string model) => MessageBox.Show(this,
        $"翻译会把你输入的文本和术语表发送到：\n\n{endpoint}\n\n当前模型：{model}\n\n该服务可能按用量收取 API 费用。请确认服务地址可信，并有权向其发送这些内容。\n\n确认后会记住此服务地址，重启和修改模型后不再提示；更换新服务地址时提示一次。\n\n是否继续？",
        "确认发送目标与 API 费用", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void RenderResult(TranslationResult value)
    {
        EmptyText.Visibility = Visibility.Collapsed;
        ResultTitle.Text = $"{LanguageName(value.Target)} · {value.Segments.Count} 段{(value.IsMock ? " · 模拟" : "")}";
        if (!string.IsNullOrEmpty(value.OptimizedChinese))
        {
            OptimizedPanel.Visibility = Visibility.Visible;
            OptimizedBox.Text = value.OptimizedChinese;
        }
        foreach (string segment in value.Segments)
        {
            var card = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var copy = new Button { Content = "复制此段", FontSize = 12, Padding = new Thickness(9, 4, 9, 4) };
            copy.Click += async (_, _) => await CopyTextAsync(segment);
            DockPanel.SetDock(copy, Dock.Right);
            heading.Children.Add(copy);
            heading.Children.Add(new TextBlock { Text = $"第 {SegmentsPanel.Children.Count + 1} 段", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            card.Children.Add(heading);
            card.Children.Add(new TextBox { Text = segment, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = Brushes.Transparent });
            SegmentsPanel.Children.Add(card);
        }
        CopyButton.IsEnabled = value.Segments.Count > 0;
    }
    private async Task CopyTextAsync(string text)
    {
        var outcome = await clipboard.CopyAsync(text);
        SetStatus(outcome.Success ? "已复制正文。" : outcome.Error, !outcome.Success);
    }
    private async void CopyAll_Click(object sender, RoutedEventArgs e) { if (result is not null) await CopyTextAsync(result.Text); }
    private async void CopyChinese_Click(object sender, RoutedEventArgs e) { if (result?.OptimizedChinese is string text) await CopyTextAsync(text); }
    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        var value = await clipboard.PasteAsync();
        if (value.Success) { InputBox.Text = value.Text; InputBox.Focus(); InputBox.CaretIndex = InputBox.Text.Length; }
        else SetStatus(value.Error, true);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { InvalidateRequest(); SetStatus("已取消，迟到响应不会更新结果。"); }
    private void Clear_Click(object sender, RoutedEventArgs e) { InvalidateRequest(); InputBox.Clear(); ClearResult(); SetStatus("已清空。"); InputBox.Focus(); }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && !composing && e.ImeProcessedKey == Key.None)
        { e.Handled = true; await TranslateAsync(); }
        else if (e.Key == Key.Escape && !composing) { e.Handled = true; HideToTray(); }
    }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (!exiting) { e.Cancel = true; HideToTray(); } }
    private void HideToTray() { InvalidateRequest(); SetStatus("窗口已收回托盘，未完成请求已取消。"); Hide(); }
    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); InputBox.Focus(); }
    private void ExitApp()
    {
        exiting = true;
        InvalidateRequest();
        apiKey = "";
        hotkey.Dispose();
        requests.Dispose();
        tray?.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        InvalidateRequest();
        var dialog = new SettingsWindow(settings, apiKey) { Owner = this };
        dialog.Apply = (candidate, key) =>
        {
            if (!hotkey.TryRegister(new WindowInteropHelper(this).Handle, candidate.Shortcut, out string error)) return error;
            bool persisted = candidate.RememberApiKey
                ? credentials.Save(candidate.Endpoint, key, out error)
                : credentials.Delete(candidate.Endpoint, out error);
            if (!persisted)
            {
                hotkey.TryRegister(new WindowInteropHelper(this).Handle, settings.Shortcut, out _);
                return error;
            }
            if (!SettingsStore.Save(candidate, settingsPath))
            {
                hotkey.TryRegister(new WindowInteropHelper(this).Handle, settings.Shortcut, out _);
                return "密钥存储选项已处理，但普通设置无法保存。请检查本地目录权限后重试。";
            }
            settings = candidate;
            apiKey = key;
            ClearResult();
            UpdateMode();
            SetStatus(settings.RememberApiKey ? "设置已保存；密钥已加密保存，重启后自动恢复。" : "设置已保存；此服务已删除保存的密钥，仅本次会话有效。");
            return "";
        };
        dialog.ShowDialog();
    }
    private void Glossary_Click(object sender, RoutedEventArgs e)
    {
        InvalidateRequest();
        var dialog = new GlossaryWindow(settings.Glossary) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            settings.Glossary = dialog.Entries;
            ClearResult();
            SetStatus(SettingsStore.Save(settings, settingsPath) ? "术语表已保存。" : "术语表已应用，但无法保存本地文件。");
        }
    }
}
