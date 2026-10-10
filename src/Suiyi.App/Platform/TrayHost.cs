using System;
using System.Drawing;
using System.Windows.Forms;

namespace Suiyi.App.Platform;

/// <summary>Session-only tray presence; no clipboard listener or background translation.</summary>
public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu;
    private bool disposed;

    public TrayHost(Action show, Action exit)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(exit);
        menu = new ContextMenuStrip();
        menu.Items.Add("打开随译", null, (_, _) => show());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        icon = new NotifyIcon
        {
            Text = "随译 · 聊天翻译",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => show();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
    }
}
