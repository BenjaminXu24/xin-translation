using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Suiyi.App;

public sealed class SettingsWindow : Window
{
    public Func<AppSettings, string, string>? Apply { get; set; }
    public SettingsWindow(AppSettings settings, string sessionKey)
    {
        Style = (Style)FindResource(typeof(Window));
        Title = "随译 · 设置"; Width = 600; Height = 700; MinWidth = 540; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "服务与快捷键", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 15) });
        root.Children.Add(new TextBlock { Text = "协议：Chat Completions + JSON object。DeepSeek 官方地址已单独适配，使用非思考模式。其他服务需支持完整协议。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var endpoint = AddField(root, "完整接口地址（HTTPS）", settings.Endpoint);
        var model = AddField(root, "模型名（按服务提供商填写）", settings.Model);
        root.Children.Add(new TextBlock { Text = "API Key", Margin = new Thickness(0, 9, 0, 4) });
        var password = new PasswordBox { Password = sessionKey, Padding = new Thickness(10), BorderBrush = new SolidColorBrush(Color.FromRgb(206, 221, 215)) };
        root.Children.Add(password);
        var rememberKey = new CheckBox { Content = "记住 API Key（仅当前 Windows 用户）", IsChecked = settings.RememberApiKey, Margin = new Thickness(0, 10, 0, 3) };
        root.Children.Add(rememberKey);
        root.Children.Add(new TextBlock { Text = "勾选后使用 Windows 安全存储加密，重启自动恢复。取消勾选并保存，会删除此服务已保存的密钥。留空使用模拟模式。", FontSize = 12, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        endpoint.TextChanged += (_, _) =>
        {
            bool same;
            try { same = ServiceConsent.NormalizeEndpoint(endpoint.Text) == ServiceConsent.NormalizeEndpoint(settings.Endpoint); }
            catch (ArgumentException) { same = false; }
            if (!same) { password.Clear(); rememberKey.IsChecked = false; }
        };
        var shortcut = AddField(root, "全局快捷键，例如 Ctrl+Alt+T / Ctrl+Shift+F8", settings.Shortcut);
        var timeout = AddField(root, "请求超时（5–180 秒）", settings.TimeoutSeconds.ToString());
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10), MinHeight = 36 };
        root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true };
        var save = new Button { Content = "保存并应用", Background = new SolidColorBrush(Color.FromRgb(8, 127, 105)), Foreground = Brushes.White, IsDefault = true };
        buttons.Children.Add(cancel); buttons.Children.Add(save); root.Children.Add(buttons);
        save.Click += (_, _) =>
        {
            string address = endpoint.Text.Trim();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query)) { error.Text = "请输入没有用户名、查询参数或片段的完整 HTTPS 接口地址。"; return; }
            if (!int.TryParse(timeout.Text, out int seconds) || seconds is < 5 or > 180) { error.Text = "超时必须是 5–180 秒的整数。"; return; }
            if (model.Text.Length > 200 || password.Password.Contains('\n') || password.Password.Contains('\r')) { error.Text = "模型名或密钥格式无效。"; return; }
            if (rememberKey.IsChecked == true && string.IsNullOrWhiteSpace(password.Password)) { error.Text = "记住密钥前请先输入 API Key；要删除已保存密钥，请取消勾选后保存。"; return; }
            if (uri.IdnHost.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase) && uri.Port == 443 && model.Text.Trim().Equals("deepseek", StringComparison.OrdinalIgnoreCase))
            { error.Text = "DeepSeek 需要具体模型名，例如 deepseek-flash，不能只填 deepseek。"; return; }
            var candidate = new AppSettings { Endpoint = address, Model = model.Text.Trim(), TimeoutSeconds = seconds, Shortcut = shortcut.Text.Trim(), Glossary = settings.Glossary,
                RememberApiKey = rememberKey.IsChecked == true, AcceptedServiceEndpoints = new List<string>(settings.AcceptedServiceEndpoints) };
            string problem = Apply?.Invoke(candidate, password.Password.Trim()) ?? "设置未能应用。";
            if (problem.Length > 0) { error.Text = problem; return; }
            password.Clear(); DialogResult = true;
        };
        Closed += (_, _) => password.Clear();
    }
    private static TextBox AddField(Panel root, string label, string value)
    {
        root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 9, 0, 4) });
        var input = new TextBox { Text = value };
        root.Children.Add(input); return input;
    }
}
