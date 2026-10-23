using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpenTabletArtist.Views;

/// <summary>R18: the in-app user manual. Renders Assets/manual/manual.md (the same source as the PDF) with a
/// small Markdown subset: #/##/### headings, paragraphs, "- " lists, tables, images and **bold**.
/// Opened by the '?' button and by Get help / User manual on Home.</summary>
public sealed class ManualWindow : Window
{
    private static ManualWindow? _open;
    private const string Root = "avares://OpenTabletArtist/Assets/manual/";

    public static void ShowManual()
    {
        if (_open is { } w) { w.Activate(); return; }
        _open = new ManualWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    public ManualWindow()
    {
        Title = "Tablet App for Mac – User Manual";
        Width = 860; Height = 900; MinWidth = 560;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));
        Content = new ScrollViewer { Content = Build(Load()), Padding = new Thickness(36, 28) };
    }

    internal static string Load()
    {
        using var s = AssetLoader.Open(new Uri(Root + "manual.md"));
        return new StreamReader(s).ReadToEnd();
    }

    private static readonly IBrush Text = new SolidColorBrush(Color.Parse("#D8D8D8"));
    private static readonly IBrush Head = new SolidColorBrush(Color.Parse("#E4E4E4"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#A8A8A8"));

    internal static StackPanel Build(string md)
    {
        var root = new StackPanel { Spacing = 8, MaxWidth = 780 };
        var para = new List<string>();
        void Flush()
        {
            if (para.Count == 0) return;
            root.Children.Add(Inline(string.Join(" ", para), 14));
            para.Clear();
        }
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("#"))
            {
                Flush();
                int level = line.TakeWhile(c => c == '#').Count();
                root.Children.Add(new TextBlock
                {
                    Text = line[level..].Trim(), Foreground = Head, TextWrapping = TextWrapping.Wrap,
                    FontSize = level == 1 ? 26 : level == 2 ? 20 : 16, FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, level == 1 ? 4 : 14, 0, 2),
                });
            }
            else if (line.StartsWith("- "))
            {
                Flush();
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*") };
                g.Children.Add(new TextBlock { Text = "•", Foreground = Text });
                var t = Inline(line[2..], 14); Grid.SetColumn(t, 1); g.Children.Add(t);
                root.Children.Add(g);
            }
            else if (line.StartsWith("|"))
            {
                Flush();
                if (line.Replace("|", "").Replace("-", "").Replace(":", "").Trim().Length == 0) continue;
                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", cells.Select((_, i) => i == 0 ? "220" : "*"))) };
                for (int i = 0; i < cells.Length; i++)
                {
                    var t = Inline(cells[i], 13); t.Margin = new Thickness(0, 0, 12, 0); Grid.SetColumn(t, i); g.Children.Add(t);
                }
                root.Children.Add(g);
            }
            else if (line.StartsWith("!["))
            {
                Flush();
                var path = line[(line.IndexOf('(') + 1)..line.LastIndexOf(')')];
                try
                {
                    using var s = AssetLoader.Open(new Uri(Root + path));
                    root.Children.Add(new Image { Source = new Bitmap(s), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4) });
                }
                catch (Exception) { root.Children.Add(new TextBlock { Text = $"[{path}]", Foreground = Muted }); }
            }
            else para.Add(line.Trim());
        }
        Flush();
        return root;
    }

    /// <summary>A paragraph with **bold** runs.</summary>
    private static TextBlock Inline(string s, double size)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Text, FontSize = size };
        var parts = s.Split("**");
        for (int i = 0; i < parts.Length; i++)
            tb.Inlines!.Add(new Run(parts[i].Replace("`", "")) { FontWeight = i % 2 == 1 ? FontWeight.SemiBold : FontWeight.Normal });
        return tb;
    }
}
