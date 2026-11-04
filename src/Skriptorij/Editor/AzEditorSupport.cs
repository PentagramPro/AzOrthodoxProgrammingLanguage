using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Az.Compiler;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;

namespace Skriptorij.Editor;

/// <summary>Цвѣта подсвѣтки — какъ на мемѣ.</summary>
public static class Palette
{
    static SolidColorBrush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
    public static readonly Brush Keyword = B("#000080");
    public static readonly Brush Def = B("#000000");
    public static readonly Brush Operator = B("#1A3CC8");
    public static readonly Brush Const = B("#7A1FA2");
    public static readonly Brush Type = B("#1A7D2A");
    public static readonly Brush Builtin = B("#0D6E86");
    public static readonly Brush Str = B("#11801E");
    public static readonly Brush Number = B("#C0201A");
    public static readonly Brush Comment = B("#8A8A8A");
    public static readonly Brush Cinnabar = B("#C0201A");
    public static readonly Brush Ident = B("#14256B");
}

/// <summary>Раскрашиваетъ строки «Аза»; первая буква имени функціи — киноварью.</summary>
public sealed class AzColorizer : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        int off = line.Offset;
        foreach (var s in Highlighter.ClassifyLine(text))
        {
            var brush = s.Kind switch
            {
                Hl.Keyword => Palette.Keyword, Hl.Def => Palette.Def, Hl.FuncName => Palette.Def,
                Hl.Operator => Palette.Operator, Hl.Const => Palette.Const, Hl.Type => Palette.Type,
                Hl.Builtin => Palette.Builtin, Hl.String => Palette.Str, Hl.Number => Palette.Number,
                Hl.Comment => Palette.Comment, _ => null,
            };
            if (brush == null) continue;
            ChangeLinePart(off + s.Start, off + s.Start + s.Length, el => el.TextRunProperties.SetForegroundBrush(brush));
            if (s.Kind == Hl.FuncName)
            {
                // буквица: первая буква со всѣми надстрочными знаками
                int len = 1;
                while (s.Start + len < text.Length && Lang.IsMark(text[s.Start + len])) len++;
                ChangeLinePart(off + s.Start, off + s.Start + len, el => el.TextRunProperties.SetForegroundBrush(Palette.Cinnabar));
            }
        }
    }
}

/// <summary>Волнистое подчеркиваніе прегрѣшенія и розовая строка.</summary>
public sealed class DiagnosticRenderer(TextEditor editor) : IBackgroundRenderer
{
    public AzError? Error { get; set; }
    public KnownLayer Layer => KnownLayer.Selection;

    static readonly Brush LineBg = Freeze(new SolidColorBrush(Color.FromArgb(40, 230, 40, 30)));
    static readonly Pen Wave = FreezePen(new Pen(Brushes.Red, 1.2));
    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (Error == null || Error.Line < 1 || Error.Line > editor.Document.LineCount) return;
        textView.EnsureVisualLines();
        var vl = textView.GetVisualLine(Error.Line);
        if (vl == null) return;
        double y = vl.VisualTop - textView.ScrollOffset.Y;
        dc.DrawRectangle(LineBg, null, new Rect(0, y, textView.ActualWidth, vl.Height));

        // слово подъ ошибкою (или послѣдній знакъ, если ошибка въ концѣ строки)
        var line = editor.Document.GetLineByNumber(Error.Line);
        var text = editor.Document.GetText(line);
        int s = Math.Clamp(Error.Col - 1, 0, text.Length), e = s;
        while (e < text.Length && !char.IsWhiteSpace(text[e])) e++;
        if (e == s) { if (s == 0) return; s--; e = s + 1; }
        var seg = new TextSegment { StartOffset = line.Offset + s, EndOffset = line.Offset + e };
        foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                double yb = r.Bottom - 1;
                ctx.BeginFigure(new Point(r.Left, yb), false, false);
                bool up = true;
                for (double x = r.Left + 3; x <= Math.Max(r.Right, r.Left + 8); x += 3, up = !up)
                    ctx.LineTo(new Point(x, up ? yb - 2.5 : yb), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, Wave, geo);
        }
    }
}

/// <summary>Отступъ: какъ у прежней строки, и на 4 болѣе послѣ двоеточія.</summary>
public sealed class AzIndentation : IIndentationStrategy
{
    public void IndentLine(TextDocument document, DocumentLine line)
    {
        var prev = line.PreviousLine;
        if (prev == null) return;
        var ptext = document.GetText(prev);
        var indent = ptext[..(ptext.Length - ptext.TrimStart(' ').Length)];
        var code = StripCommentAndStrings(ptext).TrimEnd();
        if (code.EndsWith(':')) indent += "    ";
        var cur = document.GetText(line);
        int ws = cur.Length - cur.TrimStart(' ', '\t').Length;
        document.Replace(line.Offset, ws, indent);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine) { }

    static string StripCommentAndStrings(string s)
    {
        var sb = new System.Text.StringBuilder();
        char? q = null;
        foreach (var ch in s)
        {
            if (q != null) { if (ch == q) q = null; continue; }
            if (ch == '«') { q = '»'; continue; }
            if (ch == '"') { q = '"'; continue; }
            if (ch == '#') break;
            sb.Append(ch);
        }
        return sb.ToString();
    }
}

public static class EditorFactory
{
    public static TextEditor Create(string text, string mode, bool readOnly)
    {
        var ed = new TextEditor
        {
            Text = text,
            FontFamily = (FontFamily)Application.Current.Resources["Slav"],
            FontSize = Settings.Current.CodeFontSize,
            ShowLineNumbers = true,
            IsReadOnly = readOnly,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Foreground = Palette.Ident,
            Padding = new Thickness(6, 4, 0, 0),
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
        };
        ed.Options.ConvertTabsToSpaces = true;
        ed.Options.IndentationSize = 4;
        ed.Options.EnableHyperlinks = false;
        ed.Options.EnableEmailHyperlinks = false;
        ed.Options.HighlightCurrentLine = true;
        ed.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xE8));
        ed.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromRgb(0xF3, 0xEC, 0xC8)), 1);
        ICSharpCode.AvalonEdit.Search.SearchPanel.Install(ed);
        if (mode == "az")
        {
            ed.TextArea.TextView.LineTransformers.Add(new AzColorizer());
            ed.TextArea.IndentationStrategy = new AzIndentation();
        }
        else if (mode == "cs")
        {
            ed.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("C#");
            ed.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            ed.FontSize = 14;
        }
        return ed;
    }
}
