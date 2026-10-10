using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Suiyi.App;
using Suiyi.Core;

namespace Suiyi.Tests;

/// <summary>Exercises the actual WPF window inside its own STA process; never drives other apps.</summary>
internal static class UiSmoke
{
    private const string EnglishPayment = "Hi Maria, we haven’t received the PHP 12,500 payment for INV-1042.";
    private const string ChineseCondition = "我们还没收到款，先别开通 API，等财务确认后再开。";
    private const string EnglishQuestion = "Could you check the API callback?";
    private const string FilipinoQuestion = "Pakicheck po kung pumasok na ang payment.";
    private static readonly List<CheckResult> Checks = [];
    private static int exitCode = 1;
    private static string evidenceDirectory = "";

    public static int Run()
    {
        evidenceDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tests"));
        Directory.CreateDirectory(evidenceDirectory);
        var dispatcher = Dispatcher.CurrentDispatcher;
        Suiyi.App.App? app = null;
        MainWindow? window = null;
        try
        {
            app = new Suiyi.App.App();
            app.InitializeComponent();
            window = new MainWindow();
            // Avoid the user's saved endpoint/shortcut; do not invoke settings persistence.
            SetField(window, "settings", new AppSettings { Shortcut = "Ctrl+Alt+Shift+F22" });
            app.MainWindow = window;
            window.Show();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await ExerciseAsync(window);
                    exitCode = Checks.All(check => check.Success) ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Checks.Add(new("UI harness", false, ex.GetType().Name + ": " + ex.Message));
                }
                finally
                {
                    SaveResults();
                    try { Invoke(window, "ExitApp"); }
                    catch { app.Shutdown(); }
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }), DispatcherPriority.ApplicationIdle);
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            Checks.Add(new("WPF 初始化", false, ex.GetType().Name + ": " + ex.Message));
            SaveResults();
            try { if (window is not null) Invoke(window, "ExitApp"); else app?.Shutdown(); } catch { }
        }
        Console.WriteLine($"UI RESULT total={Checks.Count} passed={Checks.Count(check => check.Success)} failed={Checks.Count(check => !check.Success)}");
        return exitCode;
    }

    private static async Task ExerciseAsync(MainWindow window)
    {
        await Task.Delay(120);
        await CheckAsync("真实 WPF 窗口与模拟模式可见", () =>
        {
            Assert(window.IsVisible && window.IsLoaded && window.ActualWidth >= 600 && window.ActualHeight >= 620);
            Equal(14d, window.FontSize); Equal("Microsoft YaHei UI", window.FontFamily.Source);
            Contains(Find<TextBlock>(window, "ModeText").Text, "模拟模式");
            Assert((string)GetField(window, "apiKey") == "");
            return Task.CompletedTask;
        });
        await CheckAsync("英文付款样例：金额、订单、姓名与中文结果", async () =>
        {
            await TranslateAsync(window, EnglishPayment);
            var body = Body(window);
            Contains(body, "PHP 12,500"); Contains(body, "INV-1042"); Contains(body, "Maria"); Contains(body, "还没有");
            Assert(Find<StackPanel>(window, "OptimizedPanel").Visibility == Visibility.Collapsed);
            Assert(Find<Button>(window, "CopyButton").IsEnabled); Contains(Find<TextBlock>(window, "ResultTitle").Text, "中文");
        });
        await CheckAsync("中文样例：优化中文与英语分区显示", async () =>
        {
            await TranslateAsync(window, ChineseCondition);
            Assert(Find<StackPanel>(window, "OptimizedPanel").Visibility == Visibility.Visible);
            Contains(Find<TextBox>(window, "OptimizedBox").Text, "API"); Contains(Body(window), "until Finance");
            Contains(Find<TextBlock>(window, "ResultTitle").Text, "English");
            await RenderAsync(window);
            SaveScreenshot(window, Path.Combine(evidenceDirectory, "ui-evidence.png"));
        });
        await CheckAsync("真实设置窗口：默认接口、协议与空密钥提示布局", async () =>
        {
            var settingsWindow = new SettingsWindow(new AppSettings(), "") { Owner = window };
            try
            {
                settingsWindow.Show(); await RenderAsync(settingsWindow);
                Assert(settingsWindow.IsVisible && settingsWindow.IsLoaded);
                Equal(14d, settingsWindow.FontSize);
                Assert(Descendants<PasswordBox>(settingsWindow).Single().Password.Length == 0);
                Assert(Descendants<TextBlock>(settingsWindow).Any(block => block.Text.Contains("Chat Completions", StringComparison.Ordinal)));
                Assert(Descendants<TextBlock>(settingsWindow).Any(block => block.Text.Contains("首版不提供记住密钥", StringComparison.Ordinal)));
                SaveScreenshot(settingsWindow, Path.Combine(evidenceDirectory, "ui-settings-evidence.png"));
            }
            finally { settingsWindow.Close(); }
        });
        await CheckAsync("设置保存：DeepSeek 裸模型名被拒绝且不调用 Apply", async () =>
        {
            var applied = false;
            var settingsWindow = new SettingsWindow(new AppSettings(), "") { Owner = window, Apply = (_, _) => { applied = true; return ""; } };
            try
            {
                settingsWindow.Show(); await RenderAsync(settingsWindow);
                var fields = Descendants<TextBox>(settingsWindow).ToArray();
                fields.Single(field => field.Text == "https://api.openai.com/v1/chat/completions").Text = "https://api.deepseek.com/chat/completions";
                fields.Single(field => field.Text.Length == 0).Text = "deepseek";
                Descendants<PasswordBox>(settingsWindow).Single().Password = "ui-fixture-key-never-real";
                Descendants<Button>(settingsWindow).Single(button => (string?)button.Content == "保存并应用").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await RenderAsync(settingsWindow);
                Assert(!applied && settingsWindow.IsVisible);
                Assert(Descendants<TextBlock>(settingsWindow).Any(block => block.Text.Contains("具体模型名", StringComparison.Ordinal) && block.Text.Contains("deepseek-flash", StringComparison.Ordinal)));
            }
            finally { settingsWindow.Close(); }
        });
        await CheckAsync("真实术语窗口：默认支付术语编辑布局", async () =>
        {
            var glossaryWindow = new GlossaryWindow(new AppSettings().Glossary) { Owner = window };
            try
            {
                glossaryWindow.Show(); await RenderAsync(glossaryWindow);
                Assert(glossaryWindow.IsVisible && glossaryWindow.IsLoaded); Equal(14d, glossaryWindow.FontSize);
                var editor = Descendants<TextBox>(glossaryWindow).Single(); Contains(editor.Text, "商户 = merchant"); Assert(editor.AcceptsReturn);
                SaveScreenshot(glossaryWindow, Path.Combine(evidenceDirectory, "ui-glossary-evidence.png"));
            }
            finally { glossaryWindow.Close(); }
        });
        await CheckAsync("英文问句：翻译按钮事件只产生译文", async () =>
        {
            Input(window, EnglishQuestion);
            ButtonWithContent(window, "翻译  Ctrl+Enter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Body(window).Length > 0);
            Contains(Body(window), "API 回调"); Assert(!Body(window).Contains("我可以", StringComparison.Ordinal));
        });
        await CheckAsync("Filipino 样例：自动识别并显示中文", async () =>
        {
            await TranslateAsync(window, FilipinoQuestion);
            Contains(Find<TextBlock>(window, "DetectedText").Text, "Filipino"); Contains(Body(window), "款项");
            Contains(Find<TextBlock>(window, "ResultTitle").Text, "中文");
        });
        await CheckAsync("中英文手动目标 Filipino", async () =>
        {
            await TranslateAsync(window, ChineseCondition, 3); Contains(Body(window), "Hindi");
            Contains(Find<TextBlock>(window, "ResultTitle").Text, "Filipino");
            await TranslateAsync(window, EnglishQuestion, 3); Contains(Body(window), "Puwede");
        });
        await CheckAsync("连续请求：改变输入后旧请求不能覆盖新结果", async () =>
        {
            Input(window, EnglishPayment); var old = TranslationTask(window);
            await Task.Delay(70); Input(window, ChineseCondition); var current = TranslationTask(window);
            await Task.WhenAll(old, current); await Task.Delay(100);
            Contains(Body(window), "until Finance"); Assert(!Body(window).Contains("INV-1042", StringComparison.Ordinal));
            Assert(!Find<Button>(window, "CancelButton").IsEnabled);
        });
        await CheckAsync("取消按钮：迟到结果不得渲染", async () =>
        {
            Input(window, EnglishPayment); var pending = TranslationTask(window);
            await Task.Delay(70); Find<Button>(window, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending; await Task.Delay(550);
            Assert(Body(window).Length == 0); Contains(Find<TextBlock>(window, "StatusText").Text, "已取消");
            Assert(!Find<Button>(window, "CancelButton").IsEnabled);
        });
        await CheckAsync("关闭收回托盘：取消请求，重新显示后没有迟到结果", async () =>
        {
            Input(window, EnglishPayment); var pending = TranslationTask(window);
            await Task.Delay(70); window.Close(); Assert(!window.IsVisible);
            await pending; await Task.Delay(550); Assert(Body(window).Length == 0);
            Invoke(window, "ShowFromTray"); await RenderAsync(window); Assert(window.IsVisible);
        });
        await CheckAsync("重试按钮事件重新完成模拟请求", async () =>
        {
            Input(window, EnglishQuestion); ButtonWithContent(window, "重试").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Body(window).Length > 0); Contains(Body(window), "API 回调");
        });
        await CheckAsync("清空按钮清除原文、结果与复制状态", () =>
        {
            ButtonWithContent(window, "清空").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(Find<TextBox>(window, "InputBox").Text.Length == 0 && Body(window).Length == 0);
            Assert(!Find<Button>(window, "CopyButton").IsEnabled);
            Assert(Find<StackPanel>(window, "OptimizedPanel").Visibility == Visibility.Collapsed);
            Contains(Find<TextBlock>(window, "StatusText").Text, "已清空"); return Task.CompletedTask;
        });
        await CheckAsync("多段 fixture：真实 WPF 逐段控件与可滚动布局", async () =>
        {
            var paragraphs = Enumerable.Range(1, 3).Select(i => string.Join(" ", Enumerable.Repeat($"Payment for INV-1042 is still pending; keep API disabled until Finance confirms PHP 12,500. ({i})", 4))).ToArray();
            Input(window, "固定长文布局样例（仅验证显示）：\n" + string.Join("\n\n", paragraphs));
            Invoke(window, "RenderResult", new TranslationResult(Language.Chinese, Language.English, "款项仍待确认，先别开通 API。", paragraphs, true));
            await RenderAsync(window);
            var panel = Find<StackPanel>(window, "SegmentsPanel"); Equal(3, panel.Children.Count);
            for (int i = 0; i < panel.Children.Count; i++)
            {
                var card = (StackPanel)panel.Children[i]; var box = (TextBox)card.Children[1];
                Equal(paragraphs[i], box.Text); Assert(box.IsReadOnly && box.TextWrapping == TextWrapping.Wrap && box.ActualWidth > 0);
                Assert(Descendants<Button>(card).Any(button => (string?)button.Content == "复制此段"));
            }
            Contains(Find<TextBlock>(window, "ResultTitle").Text, "3 段");
            Assert(Descendants<ScrollViewer>(window).Any(scroll => scroll.ScrollableHeight > 0));
            SaveScreenshot(window, Path.Combine(evidenceDirectory, "ui-long-evidence.png"));
        });
        await CheckAsync("主窗口最小尺寸：结果区可滚动且保留完整正文", async () =>
        {
            window.Width = window.MinWidth; window.Height = window.MinHeight; await RenderAsync(window);
            Equal(600d, window.ActualWidth); Equal(620d, window.ActualHeight);
            var scroll = Ancestor<ScrollViewer>(Find<StackPanel>(window, "SegmentsPanel"));
            Assert(scroll.ViewportHeight > 20 && scroll.ScrollableHeight > 0);
            scroll.ScrollToEnd(); await RenderAsync(window); Assert(scroll.VerticalOffset > 0);
            Assert(Find<StackPanel>(window, "SegmentsPanel").Children.Count == 3);
            SaveScreenshot(window, Path.Combine(evidenceDirectory, "ui-min-evidence.png"));
            window.Width = 700; window.Height = 790; await RenderAsync(window);
        });
        await CheckAsync("中文 IME 支持与合成组合事件状态复位", () =>
        {
            var input = Find<TextBox>(window, "InputBox"); Assert(InputMethod.GetIsInputMethodEnabled(input));
            var keyboard = InputManager.Current.PrimaryKeyboardDevice;
            var composition = new TextComposition(InputManager.Current, input, "ni");
            input.RaiseEvent(new TextCompositionEventArgs(keyboard, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
            Assert((bool)GetField(window, "composing"));
            input.RaiseEvent(new TextCompositionEventArgs(keyboard, new TextComposition(InputManager.Current, input, "你")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            Assert(!(bool)GetField(window, "composing"));
            input.RaiseEvent(new TextCompositionEventArgs(keyboard, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
            input.RaiseEvent(new KeyboardFocusChangedEventArgs(keyboard, 0, input, Find<ComboBox>(window, "TargetCombo")) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert(!(bool)GetField(window, "composing"));
            return Task.CompletedTask;
        });
    }

    private static async Task CheckAsync(string name, Func<Task> action)
    {
        try { await action(); Checks.Add(new(name, true, "")); Console.WriteLine("UI PASS " + name); }
        catch (Exception ex) { Checks.Add(new(name, false, ex.GetType().Name + ": " + ex.Message)); Console.WriteLine("UI FAIL " + name + ": " + ex.Message); }
    }
    private static void Input(MainWindow window, string text, int target = 0)
    {
        Find<ComboBox>(window, "SourceCombo").SelectedIndex = 0;
        Find<ComboBox>(window, "TargetCombo").SelectedIndex = target;
        Find<TextBox>(window, "InputBox").Text = text;
    }
    private static async Task TranslateAsync(MainWindow window, string text, int target = 0)
    {
        Input(window, text, target); await TranslationTask(window); await RenderAsync(window);
    }
    private static Task TranslationTask(MainWindow window) => (Task)Invoke(window, "TranslateAsync")!;
    private static string Body(MainWindow window) => string.Join("\n\n", Find<StackPanel>(window, "SegmentsPanel").Children.OfType<StackPanel>().Select(card => card.Children.OfType<TextBox>().Single().Text));
    private static T Find<T>(MainWindow window, string name) where T : class => window.FindName(name) as T ?? throw new InvalidOperationException("控件未找到：" + name);
    private static Button ButtonWithContent(MainWindow window, string content) => Descendants<Button>(window).Single(button => button.Content is string text && text == content);
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i); if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static T Ancestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(node); parent is not null; parent = VisualTreeHelper.GetParent(parent)) if (parent is T value) return value;
        throw new InvalidOperationException("控件缺少预期的滚动祖先");
    }
    private static async Task WaitUntilAsync(Func<bool> done)
    {
        for (int i = 0; i < 40; i++) { if (done()) return; await Task.Delay(50); }
        throw new InvalidOperationException("等待模拟 UI 结果超时");
    }
    private static async Task RenderAsync(Window window) { window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render); await Task.Delay(30); }
    private static void SaveScreenshot(Window window, string path)
    {
        Assert(window.IsVisible); var dpi = VisualTreeHelper.GetDpi(window);
        var content = window.Content as FrameworkElement ?? throw new InvalidOperationException("窗口缺少可截图内容");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            context.DrawRectangle(window.Background ?? Brushes.White, null, bounds);
            context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill }, null, bounds);
        }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static object GetField(MainWindow window, string name) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void SetField(MainWindow window, string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static void SaveResults() => File.WriteAllText(Path.Combine(evidenceDirectory, "ui-smoke-results.json"), JsonSerializer.Serialize(new { mode = "in-process WPF fixture smoke test", realApi = false, realClipboard = false, realImeInput = false, checks = Checks }, new JsonSerializerOptions { WriteIndented = true }));
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("UI 断言应为 true"); }
    private static void Contains(string text, string part) => Assert(text.Contains(part, StringComparison.Ordinal));
    private static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual));
    private sealed record CheckResult(string Name, bool Success, string Details);
}
