using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;
using Az.Compiler;
using Skriptorij.Editor;

namespace Skriptorij;

/// <summary>Настройки въ %APPDATA%\Skriptorij\settings.json.</summary>
public sealed class Settings
{
    public string? LastProject { get; set; }
    public double CodeFontSize { get; set; } = 19;
    public bool ShowIconostasis { get; set; } = true;
    public bool ShowKeyboard { get; set; } = true;
    public List<string> OpenFiles { get; set; } = [];

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skriptorij", "settings.json");

    public static Settings Current { get; } = Load();

    static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* не бѣда */ }
    }
}

public sealed record Saint(string Id, string Mono, string Cap, string Title, string Feast, string Life, string Why);

public static class Saints
{
    public static readonly List<Saint> All = Load();

    static List<Saint> Load()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("saints.json")!;
        return JsonSerializer.Deserialize<List<Saint>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public static ImageSource? Image(string id)
    {
        foreach (var ext in new[] { "jpg", "png" })
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Assets/Icons/{id}.{ext}");
                var info = Application.GetResourceStream(uri);
                if (info == null) continue;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = info.Stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (IOException) { }
        }
        return null;
    }

    /// <summary>Плитка иконы: настоящій образъ или рисованная заставка (позолота, нимбъ, надписаніе).</summary>
    public static FrameworkElement Tile(Saint s, double w, double h)
    {
        var img = Image(s.Id);
        var grid = new Grid { Width = w, Height = h, ClipToBounds = true };
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x2A, 0x17)),
            BorderThickness = new Thickness(img != null ? 2 : 3),
            CornerRadius = new CornerRadius(2),
            Child = grid,
            ToolTip = s.Title,
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.6 },
        };
        double capH = h * 0.17;
        if (img != null)
        {
            grid.Children.Add(new Image { Source = img, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top });
        }
        else
        {
            grid.Background = (Brush)Application.Current.Resources["GoldLeaf"];
            grid.Children.Add(new Border
            {
                Width = w * 0.68, Height = h * 0.38, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, capH),
                CornerRadius = new CornerRadius(w * 0.32, w * 0.32, 2, 2),
                Background = new LinearGradientBrush(Color.FromRgb(0x8B, 0x1E, 0x16), Color.FromRgb(0x5C, 0x14, 0x10), 90),
            });
            grid.Children.Add(new Ellipse(w * 0.6, h * 0.12));
            grid.Children.Add(new TextBlock
            {
                Text = s.Mono, FontFamily = (FontFamily)Application.Current.Resources["Slav"], FontSize = h * 0.15,
                Foreground = Palette.Cinnabar, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, h * 0.18, 0, 0), LineHeight = h * 0.16,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            });
        }
        grid.Children.Add(new Border
        {
            Height = capH, VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0xFA, 0xEC, 0xBE)),
            Child = new TextBlock
            {
                Text = s.Cap, FontFamily = (FontFamily)Application.Current.Resources["Slav"], FontSize = capH * 0.72,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x0D, 0x06)), TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        return border;
    }

    sealed class Ellipse : Border
    {
        public Ellipse(double d, double top)
        {
            Width = Height = d;
            CornerRadius = new CornerRadius(d / 2);
            VerticalAlignment = VerticalAlignment.Top;
            HorizontalAlignment = HorizontalAlignment.Center;
            Margin = new Thickness(0, top, 0, 0);
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9B, 0x6E, 0x1D));
            BorderThickness = new Thickness(1);
            Background = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(0xFF, 0xF3, 0xB8), 0), new(Color.FromRgb(0xF0, 0xCF, 0x62), 0.75),
                new(Color.FromRgb(0xB9, 0x8A, 0x25), 1),
            });
        }
    }
}

/// <summary>Малый переводчикъ Markdown → FlowDocument для окна «Сѵнтаксисъ».</summary>
public static class Markdown
{
    static readonly FontFamily Slav = (FontFamily)Application.Current.Resources["Slav"];
    static readonly Brush Red = Palette.Cinnabar;

    public static FlowDocument ToFlow(string md)
    {
        var doc = new FlowDocument { FontFamily = Slav, FontSize = 17, PagePadding = new Thickness(24, 12, 24, 24), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xF6)) };
        var lines = md.Replace("\r", "").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            var l = lines[i];
            if (l.StartsWith("```"))
            {
                bool az = l.Contains("азъ") || l.Contains("az");
                var buf = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].StartsWith("```")) buf.Add(lines[i++]);
                i++;
                var p = new Paragraph { Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF3, 0xE3)), Padding = new Thickness(10, 6, 10, 6),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xD6, 0xB0)), BorderThickness = new Thickness(1), Foreground = Palette.Ident };
                for (int k = 0; k < buf.Count; k++)
                {
                    if (k > 0) p.Inlines.Add(new LineBreak());
                    AddCode(p.Inlines, buf[k], az);
                }
                doc.Blocks.Add(p);
                continue;
            }
            var m = Regex.Match(l, @"^(#{1,4})\s+(.*)");
            if (m.Success)
            {
                int lvl = m.Groups[1].Length;
                var p = new Paragraph { FontSize = lvl == 1 ? 30 : lvl == 2 ? 23 : 19, Foreground = Red, Margin = new Thickness(0, lvl == 1 ? 4 : 14, 0, 4) };
                Inline(p.Inlines, m.Groups[2].Value);
                doc.Blocks.Add(p);
                i++;
                continue;
            }
            if (l.StartsWith('|'))
            {
                var rows = new List<string>();
                while (i < lines.Length && lines[i].StartsWith('|')) rows.Add(lines[i++]);
                rows = rows.Where(r => !Regex.IsMatch(r, @"^\|[\s:|-]+\|$")).ToList();
                var table = new Table { CellSpacing = 0, BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xC9, 0xA0)), BorderThickness = new Thickness(1) };
                var group = new TableRowGroup();
                table.RowGroups.Add(group);
                bool head = true;
                foreach (var r in rows)
                {
                    var cells = r.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
                    while (table.Columns.Count < cells.Count) table.Columns.Add(new TableColumn());
                    var row = new TableRow();
                    foreach (var c in cells)
                    {
                        var p = new Paragraph { Margin = new Thickness(0) };
                        Inline(p.Inlines, c);
                        row.Cells.Add(new TableCell(p)
                        {
                            Padding = new Thickness(6, 2, 6, 2), BorderBrush = table.BorderBrush, BorderThickness = new Thickness(0.5),
                            Background = head ? new SolidColorBrush(Color.FromRgb(0xF3, 0xE7, 0xC4)) : null,
                        });
                    }
                    group.Rows.Add(row);
                    head = false;
                }
                doc.Blocks.Add(table);
                continue;
            }
            if (Regex.IsMatch(l, @"^\s*([-*]|\d+\.)\s"))
            {
                bool ordered = Regex.IsMatch(l, @"^\s*\d+\.");
                var list = new List { MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(0, 2, 0, 6) };
                while (i < lines.Length && Regex.IsMatch(lines[i], @"^\s*([-*]|\d+\.)\s"))
                {
                    var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                    Inline(p.Inlines, Regex.Replace(lines[i++], @"^\s*([-*]|\d+\.)\s", ""));
                    list.ListItems.Add(new ListItem(p));
                }
                doc.Blocks.Add(list);
                continue;
            }
            if (l.Trim().Length == 0) { i++; continue; }
            var para = new Paragraph { Margin = new Thickness(0, 4, 0, 6) };
            var parts = new List<string>();
            while (i < lines.Length && lines[i].Trim().Length > 0 && !Regex.IsMatch(lines[i], @"^(#|```|\||\s*([-*]|\d+\.)\s)"))
                parts.Add(lines[i++].Trim());
            Inline(para.Inlines, string.Join(" ", parts));
            doc.Blocks.Add(para);
        }
        return doc;
    }

    static void AddCode(InlineCollection inl, string line, bool az)
    {
        if (!az) { inl.Add(new Run(line)); return; }
        int pos = 0;
        foreach (var s in Highlighter.ClassifyLine(line))
        {
            if (s.Start > pos) inl.Add(new Run(line[pos..s.Start]));
            var brush = s.Kind switch
            {
                Hl.Keyword => Palette.Keyword, Hl.Def or Hl.FuncName => Palette.Def, Hl.Operator => Palette.Operator,
                Hl.Const => Palette.Const, Hl.Type => Palette.Type, Hl.Builtin => Palette.Builtin, Hl.String => Palette.Str,
                Hl.Number => Palette.Number, Hl.Comment => Palette.Comment, _ => Palette.Ident,
            };
            inl.Add(new Run(line.Substring(s.Start, s.Length)) { Foreground = brush });
            pos = s.Start + s.Length;
        }
        if (pos < line.Length) inl.Add(new Run(line[pos..]));
    }

    static void Inline(InlineCollection inl, string s)
    {
        foreach (Match m in Regex.Matches(s, @"`([^`]+)`|\*\*([^*]+)\*\*|\*([^*]+)\*|\[([^\]]+)\]\(([^)]+)\)|([^`*\[]+|[`*\[])"))
        {
            if (m.Groups[1].Success)
                inl.Add(new Run(m.Groups[1].Value) { Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xEC, 0xD6)) });
            else if (m.Groups[2].Success) inl.Add(new Bold(new Run(m.Groups[2].Value)));
            else if (m.Groups[3].Success) inl.Add(new Italic(new Run(m.Groups[3].Value)));
            else if (m.Groups[4].Success) inl.Add(new Underline(new Run(m.Groups[4].Value)));
            else inl.Add(new Run(m.Value));
        }
    }
}

/// <summary>Простое окно съ FlowDocument (житіе, синтаксисъ, молитва, о программѣ).</summary>
public sealed class InfoWindow : Window
{
    public InfoWindow(Window owner, string title, FlowDocument doc, double w = 860, double h = 640)
    {
        Owner = owner;
        Title = title;
        Width = w; Height = h;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = owner.Icon;
        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xF6));
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x3A, 0x1E)), BorderThickness = new Thickness(3),
            Child = new FlowDocumentScrollViewer { Document = doc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }
}
