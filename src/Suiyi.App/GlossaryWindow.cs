using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Suiyi.Core;

namespace Suiyi.App;

public sealed class GlossaryWindow : Window
{
    public List<GlossaryEntry> Entries { get; private set; }
    public GlossaryWindow(IEnumerable<GlossaryEntry> entries)
    {
        Style = (Style)FindResource(typeof(Window));
        Entries = entries.ToList();
        Title = "随译 · 术语表"; Width = 540; Height = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(24) }; Content = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock { Text = "每行一个术语：原文 = 首选表达\n术语是翻译参考；其他语言保持其含义。最多 100 项。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        root.Children.Add(heading);
        var edit = new TextBox { Text = string.Join("\n", Entries.Select(x => $"{x.Source} = {x.Target}")), AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(edit, 1); root.Children.Add(edit);
        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(bottom, 2); root.Children.Add(bottom);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true }; var save = new Button { Content = "保存术语" }; buttons.Children.Add(cancel); buttons.Children.Add(save); bottom.Children.Add(buttons);
        save.Click += (_, _) =>
        {
            var next = new List<GlossaryEntry>();
            foreach (string line in edit.Text.Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || parts.Any(x => x.Trim().Length > 100)) { error.Text = "每行需包含原文和首选表达，以 = 分隔；每项最多 100 字。"; return; }
                next.Add(new GlossaryEntry(parts[0].Trim(), parts[1].Trim()));
            }
            if (next.Count > 100 || next.Select(x => x.Source).Distinct().Count() != next.Count) { error.Text = "最多 100 项，原文术语不能重复。"; return; }
            Entries = next; DialogResult = true;
        };
    }
}
