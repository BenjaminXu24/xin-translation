using System;
using System.Threading;
using System.Windows;

namespace Suiyi.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\Suiyi.PersonalClient", out bool created);
        if (!created)
        {
            MessageBox.Show("随译已在运行，请从系统托盘打开，或按已配置的全局快捷键。", "随译");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            // Deliberately never expose exception text, request data or credentials.
            MessageBox.Show("操作未完成。请重试；如果持续发生，请重新启动随译。", "随译");
            args.Handled = true;
        };
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
