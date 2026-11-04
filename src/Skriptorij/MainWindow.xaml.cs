using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Az.Compiler;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Skriptorij.Editor;

namespace Skriptorij;

public sealed class EditorTab
{
    public required string Path;          // полный путь (или §… для виртуальныхъ вкладокъ)
    public required string Name;
    public required TextEditor Ed;
    public DiagnosticRenderer? Diag;
    public string Saved = "";
    public bool Dirty;
    public bool ReadOnly;
    public bool IsAz;
    public Border? TabEl;
    public DispatcherTimer? CheckTimer;
    public int CheckGen;
}

public partial class MainWindow : Window
{
    string root = "";
    readonly List<EditorTab> tabs = [];
    EditorTab? active;
    Process? running;
    readonly HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase);
    readonly Settings cfg = Settings.Current;

    public MainWindow(string? project, string? open, string? snapshot, bool runFirst = false)
    {
        InitializeComponent();
        BuildMenu();
        BuildIconostasis();
        BuildKeyboard();
        WireButtons();
        LangText.Text = "А҆́зъ " + Lang.Version;
        IconoBar.Visibility = cfg.ShowIconostasis ? Visibility.Visible : Visibility.Collapsed;
        KbdBar.Visibility = cfg.ShowKeyboard ? Visibility.Visible : Visibility.Collapsed;

        OpenProject(ResolveProject(project));
        var toOpen = open != null ? [Path.GetFullPath(open)] : cfg.OpenFiles.Where(File.Exists).ToList();
        if (toOpen.Count == 0)
        {
            var veda = Directory.EnumerateFiles(root, "вѣды.азъ", SearchOption.AllDirectories).FirstOrDefault();
            if (veda != null) toOpen.Add(veda);
        }
        foreach (var f in toOpen) OpenFile(f);

        PreviewKeyDown += OnKey;
        Closing += OnClosing;
        if (snapshot != null) ContentRendered += async (_, _) =>
        {
            await Task.Delay(1200);
            if (runFirst) { await RunAction(RunKind.Run); await Task.Delay(300); }
            Snapshot(snapshot);
        };
    }

    // ════════════════════════ проектъ ════════════════════════
    string ResolveProject(string? arg)
    {
        if (arg != null && Directory.Exists(arg)) return Path.GetFullPath(arg);
        if (cfg.LastProject != null && Directory.Exists(cfg.LastProject)) return cfg.LastProject;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var ex = Path.Combine(d.FullName, "examples", "Адонаи");
            if (Directory.Exists(ex)) return ex;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    void OpenProject(string dir)
    {
        root = Path.GetFullPath(dir);
        cfg.LastProject = root;
        expanded.Clear();
        expanded.Add(root);
        foreach (var d in Directory.EnumerateDirectories(root)) expanded.Add(d);
        LoadTree();
        UpdateTitle();
    }

    static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "__pycache__", ".idea" };

    void LoadTree()
    {
        Tree.Items.Clear();
        var top = MakeTreeItem(root, true, $"{Path.GetFileName(root)}", root);
        Tree.Items.Add(top);
        FillDir(top, root);
    }

    void FillDir(TreeViewItem item, string dir)
    {
        item.Items.Clear();
        IEnumerable<string> dirs = [], files = [];
        try
        {
            dirs = Directory.EnumerateDirectories(dir).Where(d => !Hidden.Contains(Path.GetFileName(d)) && !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase);
            files = Directory.EnumerateFiles(dir).Where(f => !Path.GetFileName(f).StartsWith('.'))
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        foreach (var d in dirs)
        {
            var ti = MakeTreeItem(d, true, Path.GetFileName(d));
            item.Items.Add(ti);
            FillDir(ti, d);
        }
        foreach (var f in files) item.Items.Add(MakeTreeItem(f, false, Path.GetFileName(f)));
    }

    TreeViewItem MakeTreeItem(string path, bool isDir, string name, string? note = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(isDir ? FolderIcon() : FileIcon(path));
        sp.Children.Add(new TextBlock { Text = name, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = note != null ? FontWeights.Bold : FontWeights.Normal });
        if (note != null)
            sp.Children.Add(new TextBlock { Text = $" ({note})", Foreground = Brushes.Gray, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var ti = new TreeViewItem { Header = sp, Tag = path, IsExpanded = isDir && expanded.Contains(path) };
        if (isDir)
        {
            ti.Expanded += (_, e) => { expanded.Add(path); e.Handled = true; };
            ti.Collapsed += (_, e) => { expanded.Remove(path); e.Handled = true; };
        }
        else
        {
            ti.MouseDoubleClick += (_, e) => { OpenFile(path); e.Handled = true; };
            ti.Selected += (_, e) => { if (Path.GetExtension(path) == ".азъ" || IsTextFile(path)) OpenFile(path); e.Handled = true; };
        }
        return ti;
    }

    static bool IsTextFile(string p) => Path.GetExtension(p).ToLowerInvariant() is ".md" or ".txt" or ".json" or ".cs" or ".азъ";

    static FrameworkElement FolderIcon() => new Border
    {
        Width = 18, Height = 13, CornerRadius = new CornerRadius(1, 3, 2, 2), BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x88, 0x2A)),
        Background = new LinearGradientBrush(Color.FromRgb(0xF5, 0xD7, 0x7C), Color.FromRgb(0xE2, 0xB5, 0x3F), 90),
        VerticalAlignment = VerticalAlignment.Center,
    };

    static FrameworkElement FileIcon(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var (glyph, fg, bg, bd) = ext switch
        {
            ".азъ" => ("☦", "#C0501A", "#FFF8E8", "#B48A3A"),
            ".exe" => ("⚙", "#334455", "#E9F0FF", "#6680B0"),
            _ => ("≡", "#555577", "#F4F4F4", "#999999"),
        };
        var conv = new BrushConverter();
        return new Border
        {
            Width = 15, Height = 18, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = (Brush)conv.ConvertFromString(bd)!, Background = (Brush)conv.ConvertFromString(bg)!,
            Child = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 11, Foreground = (Brush)conv.ConvertFromString(fg)!,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    // ════════════════════════ вкладки ════════════════════════
    public void OpenFile(string path, string? content = null, string? name = null, bool readOnly = false, bool az = true)
    {
        var tab = tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (tab == null)
        {
            if (content == null)
            {
                if (!File.Exists(path)) return;
                if (!IsTextFile(path)) { Status("Се́й фа́йлъ не чита́емъ.", false); return; }
                content = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
            }
            bool isAz = az && path.EndsWith(".азъ", StringComparison.OrdinalIgnoreCase);
            var mode = isAz ? "az" : path.StartsWith("§cs:") ? "cs" : "plain";
            var ed = EditorFactory.Create(content, mode, readOnly);
            tab = new EditorTab { Path = path, Name = name ?? Path.GetFileName(path), Ed = ed, Saved = content, ReadOnly = readOnly, IsAz = isAz };
            if (tab.IsAz)
            {
                tab.Diag = new DiagnosticRenderer(ed);
                ed.TextArea.TextView.BackgroundRenderers.Add(tab.Diag);
            }
            var t = tab;
            ed.TextChanged += (_, _) => OnEdit(t);
            ed.TextArea.Caret.PositionChanged += (_, _) => UpdatePos(t);
            ed.Visibility = Visibility.Collapsed;
            EditorHost.Children.Add(ed);
            tabs.Add(tab);
            if (tab.IsAz && !readOnly) ScheduleCheck(tab, 0);
        }
        Activate(tab);
    }

    void Activate(EditorTab? tab)
    {
        active = tab;
        foreach (var t in tabs) t.Ed.Visibility = t == tab ? Visibility.Visible : Visibility.Collapsed;
        Welcome.Visibility = tab == null ? Visibility.Visible : Visibility.Collapsed;
        RenderTabs();
        UpdateTitle();
        if (tab != null)
        {
            Dispatcher.BeginInvoke(() => { tab.Ed.Focus(); tab.Ed.TextArea.Focus(); }, DispatcherPriority.Input);
            UpdatePos(tab);
            if (tab.Diag?.Error is { } e) Status($"Стрк. {e.Line}: {e.Message}", true);
        }
    }

    void RenderTabs()
    {
        TabStrip.Children.Clear();
        foreach (var t in tabs)
        {
            var name = new TextBlock { Text = t.Name + (t.Dirty ? " *" : ""), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 6, 0) };
            var close = new TextBlock { Text = "✕", FontFamily = new FontFamily("Segoe UI"), FontSize = 10, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "Затвори́ти" };
            close.MouseLeftButtonUp += (_, e) => { CloseTab(t); e.Handled = true; };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(t.ReadOnly ? FileIcon("x.cs") : FileIcon(t.Path));
            sp.Children.Add(name);
            sp.Children.Add(close);
            var b = new Border
            {
                Child = sp, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 1, t == active ? -1 : 0),
                BorderBrush = (Brush)FindResource("PanelBorder"), BorderThickness = new Thickness(1, 1, 1, t == active ? 0 : 1),
                CornerRadius = new CornerRadius(3, 3, 0, 0), Background = t == active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC)),
                ToolTip = t.Path.StartsWith('§') ? null : t.Path,
            };
            if (t.Dirty) name.Foreground = Brushes.DarkRed;
            b.MouseLeftButtonDown += (_, _) => Activate(t);
            b.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) CloseTab(t); };
            t.TabEl = b;
            TabStrip.Children.Add(b);
        }
    }

    void CloseTab(EditorTab? t)
    {
        t ??= active;
        if (t == null) return;
        if (t.Dirty)
        {
            var r = MessageBox.Show(this, $"«{t.Name}» не сохране́нъ. Сохрани́ти?", "Скрипторїй", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) Save(t);
        }
        int i = tabs.IndexOf(t);
        tabs.Remove(t);
        EditorHost.Children.Remove(t.Ed);
        t.CheckTimer?.Stop();
        Activate(tabs.Count == 0 ? null : tabs[Math.Min(i, tabs.Count - 1)]);
    }

    void OnEdit(EditorTab t)
    {
        bool dirty = !t.ReadOnly && t.Ed.Text != t.Saved;
        if (dirty != t.Dirty) { t.Dirty = dirty; RenderTabs(); }
        if (t.IsAz && !t.ReadOnly) ScheduleCheck(t, 450);
    }

    bool Save(EditorTab? t = null)
    {
        t ??= active;
        if (t == null || t.ReadOnly || t.Path.StartsWith('§')) return false;
        try
        {
            File.WriteAllText(t.Path, t.Ed.Text, new UTF8Encoding(false));
            t.Saved = t.Ed.Text;
            t.Dirty = false;
            RenderTabs();
            Status($"Сохране́но: {t.Name}", false);
            return true;
        }
        catch (Exception e) { Status("Не можно сохрани́ти: " + e.Message, true); return false; }
    }

    void UpdatePos(EditorTab t)
    {
        if (t != active) return;
        var c = t.Ed.TextArea.Caret;
        PosText.Text = $"Стрк. {c.Line}, стлб. {c.Column}";
    }

    void UpdateTitle()
    {
        var file = active == null ? "" : active.Path.StartsWith('§') ? active.Name : Path.GetRelativePath(root, active.Path);
        Title = active == null
            ? $"{Path.GetFileName(root)} - [{root}] — Скрипторїй"
            : $"{Path.GetFileName(root)} - [{root}] - …\\{file} — Скрипторїй";
        Crumbs.Children.Clear();
        var parts = new List<string> { Path.GetFileName(root) };
        if (active != null && !active.Path.StartsWith('§'))
            parts.AddRange(Path.GetRelativePath(root, active.Path).Split(Path.DirectorySeparatorChar));
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) Crumbs.Children.Add(new TextBlock { Text = "  ›  ", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(i == parts.Count - 1 && i > 0 ? FileIcon(parts[i]) : FolderIcon());
            sp.Children.Add(new TextBlock { Text = parts[i], Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Crumbs.Children.Add(sp);
        }
    }

    // ════════════════════════ провѣрка на лету ════════════════════════
    void ScheduleCheck(EditorTab t, int delayMs)
    {
        t.CheckTimer ??= new DispatcherTimer();
        t.CheckTimer.Stop();
        t.CheckTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs));
        t.CheckTimer.Tick -= CheckTick;
        t.CheckTimer.Tag = t;
        t.CheckTimer.Tick += CheckTick;
        t.CheckTimer.Start();
    }

    async void CheckTick(object? sender, EventArgs e)
    {
        var timer = (DispatcherTimer)sender!;
        timer.Stop();
        var t = (EditorTab)timer.Tag;
        var text = t.Ed.Text;
        int gen = ++t.CheckGen;
        var err = await Task.Run(() =>
        {
            try { AzCompiler.Check(text); return null; }
            catch (AzError ex) { return ex; }
            catch (Exception ex) { return new AzError("внутренняя ошибка транслятора: " + ex.Message); }
        });
        if (gen != t.CheckGen || t.Diag == null) return;
        t.Diag.Error = err;
        t.Ed.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        if (t == active)
            Status(err == null ? "Прегрѣше́нїй не ѡ҆брѣте́но." : $"Стрк. {err.Line}: {err.Message}", err != null);
    }

    // ════════════════════════ исходъ ════════════════════════
    void Out(string text, Brush? brush = null)
    {
        var doc = Output.Document;
        if (doc.Blocks.LastBlock is not Paragraph p) { p = new Paragraph { Margin = new Thickness(0) }; doc.Blocks.Add(p); }
        p.Inlines.Add(new Run(text) { Foreground = brush ?? Brushes.Black });
        Output.ScrollToEnd();
    }

    void OutDiag(EditorTab t, AzError e)
    {
        if (Output.Document.Blocks.LastBlock is not Paragraph p) { p = new Paragraph { Margin = new Thickness(0) }; Output.Document.Blocks.Add(p); }
        var link = new Hyperlink(new Run($"{t.Name}:{e.Line}:{e.Col}")) { Cursor = Cursors.Hand };
        link.Click += (_, _) => { Activate(t); GotoLine(t, e.Line, e.Col); };
        p.Inlines.Add(link);
        Out($": прегрѣше́нїе: {e.Message}\n", Brushes.DarkRed);
    }

    static readonly Brush Ok = new SolidColorBrush(Color.FromRgb(0x1A, 0x7D, 0x2A));
    static readonly Brush Sys = Brushes.Gray;

    void GotoLine(EditorTab t, int line, int col)
    {
        if (line < 1 || line > t.Ed.Document.LineCount) return;
        var l = t.Ed.Document.GetLineByNumber(line);
        t.Ed.CaretOffset = l.Offset + Math.Clamp(col - 1, 0, l.Length);
        t.Ed.ScrollToLine(line);
        t.Ed.TextArea.Focus();
    }

    void Status(string msg, bool err)
    {
        StatusText.Text = msg;
        StatusText.Foreground = err ? Brushes.DarkRed : Brushes.Black;
    }

    // ════════════════════════ сборка и пускъ ════════════════════════
    enum RunKind { Run, Build, Console }

    async Task RunAction(RunKind kind)
    {
        var t = active;
        if (t == null || !t.IsAz || t.ReadOnly) { Status("Сперва̀ ѿверзи́ фа́йлъ .азъ", true); return; }
        if (running != null) { Status("Программа ещё бѣжитъ. Ѡ҆станови́ её.", true); return; }
        BottomRow.Height = BottomRow.Height.Value < 60 ? new GridLength(210) : BottomRow.Height;
        Save(t);
        var verb = kind switch { RunKind.Run => "Пꙋ́скъ", RunKind.Build => "Сбо́рка", _ => "Пꙋ́скъ въ консо́ли" };
        Out($"\n☦ {verb}: {t.Name} …\n", Sys);
        Status(verb + "…", false);
        var src = t.Ed.Text;
        var sw = Stopwatch.StartNew();
        string outExe = kind == RunKind.Build
            ? Path.Combine(root, "собрано", Path.GetFileNameWithoutExtension(t.Path) + ".exe")
            : Path.Combine(Path.GetTempPath(), "az_run", Path.GetFileNameWithoutExtension(t.Path) + ".exe");
        BuildResult res;
        try
        {
            res = await Task.Run(() => AzCompiler.Build(src, t.Name, outExe, t.Path));
        }
        catch (AzError e) when (e.Line > 0)
        {
            OutDiag(t, e);
            t.Diag!.Error = e;
            t.Ed.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            GotoLine(t, e.Line, e.Col);
            Status("Ѡ҆брѣте́ны прегрѣше́нїѧ.", true);
            return;
        }
        catch (Exception e)
        {
            Out((e is AzError ? e.Message : e.ToString()) + "\n", Brushes.DarkRed);
            Status("Сбо́рка не ᲂу҆дала́сь.", true);
            return;
        }
        long buildMs = sw.ElapsedMilliseconds;
        switch (kind)
        {
            case RunKind.Build:
                Out($"Со́брано: {res.ExePath}  ({buildMs} мс{(res.FromCache ? ", изъ кэша" : "")}). Сла́ва Бо́гꙋ!\n", Ok);
                LoadTree();
                Status("Со́брано.", false);
                break;
            case RunKind.Console:
                var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(t.Path)! };
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add($"title {t.Name} & \"{res.ExePath}\" & echo. & pause");
                Process.Start(psi);
                Out($"Пꙋще́но во ѻ҆кнѣ̀ консо́ли: {res.ExePath}\n", Ok);
                break;
            default:
                await RunCaptured(res.ExePath, t);
                break;
        }
    }

    async Task RunCaptured(string exe, EditorTab t)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(t.Path)!,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        var sw = Stopwatch.StartNew();
        var p = Process.Start(psi)!;
        running = p;
        StopBtn.IsEnabled = true;
        Status("Программа бѣжитъ…", false);
        var stdin = StdinBox.Text.Replace("\r\n", "\n");
        if (stdin.Length > 0 && !stdin.EndsWith('\n')) stdin += "\n";
        _ = Task.Run(() =>
        {
            try { p.StandardInput.BaseStream.Write(Encoding.UTF8.GetBytes(stdin)); p.StandardInput.Close(); }
            catch (IOException) { }
        });
        async Task Pump(StreamReader r, Brush? brush)
        {
            var buf = new char[2048];
            int n;
            while ((n = await r.ReadAsync(buf)) > 0)
            {
                var s = new string(buf, 0, n).Replace("\r\n", "\n");
                await Dispatcher.InvokeAsync(() => Out(s, brush));
            }
        }
        await Task.WhenAll(Pump(p.StandardOutput, null), Pump(p.StandardError, Brushes.DarkRed), p.WaitForExitAsync());
        running = null;
        StopBtn.IsEnabled = false;
        int code = p.ExitCode;
        Out($"\n— Ко́нчисѧ съ ко́домъ {code} ({sw.ElapsedMilliseconds} мс). {(code == 0 ? "А҆ми́нь." : "Гдⷭ҇и, поми́лꙋй.")}\n", code == 0 ? Ok : Brushes.DarkRed);
        Status(code == 0 ? "Гото́во. А҆ми́нь." : $"Ко́дъ исхо́да {code}", code != 0);
        p.Dispose();
    }

    void StopRunning()
    {
        try { running?.Kill(true); Out("\n■ Ѡ҆стано́влено.\n", Sys); } catch { }
    }

    void ShowCSharp()
    {
        var t = active;
        if (t == null || !t.IsAz) return;
        string cs;
        try { cs = AzCompiler.ToCSharp(t.Ed.Text, t.Name); }
        catch (AzError e) { OutDiag(t, e); return; }
        var path = "§cs:" + t.Path;
        var old = tabs.FirstOrDefault(x => x.Path == path);
        if (old != null) { old.Dirty = false; CloseTab(old); }
        OpenFile(path, cs, Path.GetFileNameWithoutExtension(t.Path) + ".cs", readOnly: true, az: false);
    }

    // ════════════════════════ меню ════════════════════════
    void BuildMenu()
    {
        Add("Фа́йлъ", [
            ("Но́вый фа́йлъ…", "Alt+Ins", NewFile), ("Нова́ѧ па́пка…", "", NewFolder), ("Ѿве́рзти проектъ…", "", PickProject), null,
            ("Сохрани́ти", "Ctrl+S", () => Save()), ("Сохрани́ти всѧ̑", "Ctrl+Shift+S", SaveAll), null,
            ("Затвори́ти вкла́дкꙋ", "Ctrl+F4", () => CloseTab(null)), ("И҆зы́ти", "Alt+F4", Close)]);
        Add("Реда́кторъ", [
            ("Ѿмѣни́ти", "Ctrl+Z", () => active?.Ed.Undo()), ("Возврати́ти", "Ctrl+Y", () => active?.Ed.Redo()), null,
            ("Вста́вити «»", "", () => Insert("«»", 1)), ("Закомменти́ровати", "Ctrl+/", ToggleComment), null,
            ("Сла́вѧнскаѧ клавїатꙋ́ра", "", () => Toggle(KbdBar, v => cfg.ShowKeyboard = v))]);
        Add("По́искъ", [
            ("Найтѝ…", "Ctrl+F", () => ApplicationCommands.Find.Execute(null, active?.Ed.TextArea)),
            ("Найтѝ да́лѣе", "F3", () => ICSharpCode.AvalonEdit.Search.SearchCommands.FindNext.Execute(null, active?.Ed.TextArea)),
            ("Замѣни́ти…", "Ctrl+H", () => ApplicationCommands.Replace.Execute(null, active?.Ed.TextArea)),
            ("Перейти́ къ строцѣ̀…", "Ctrl+G", GotoLinePrompt)]);
        Add("Взо́ръ", [
            ("І҆коноста́съ", "", () => Toggle(IconoBar, v => cfg.ShowIconostasis = v)),
            ("Шрифтъ кꙋ́пнѣе", "Ctrl+=", () => Zoom(2)), ("Шрифтъ мѣ́льче", "Ctrl+-", () => Zoom(-2))]);
        Add("Ко́дъ", [("Показа́ти C#-ко́дъ", "", ShowCSharp), ("Провѣ́рити", "", CheckNow)]);
        Add("Пꙋ́скъ", [
            ("Пꙋсти́ти", "Shift+F10", () => _ = RunAction(RunKind.Run)),
            ("Собра́ти .exe", "Ctrl+F9", () => _ = RunAction(RunKind.Build)),
            ("Пꙋсти́ти въ консо́ли", "Ctrl+Shift+F10", () => _ = RunAction(RunKind.Console)), null,
            ("Ѡ҆станови́ти", "Ctrl+F2", StopRunning)]);
        Add("Нала́дка", [("Исповѣ́дати прегрѣше́нїѧ", "", CheckNow), ("Помоли́тисѧ предъ на́ладкою", "", Pray),
            ("Ѿла́дчикъ", "", () => Status("Ѿла́дчикъ подоба́етъ созда́ти по́слѣ. Пока́ — моли́тва и глаго́ли.", false))]);
        Add("Окно̀", [("Проектъ", "Alt+1", ToggleProject), ("Исхо́дъ", "Alt+4", ToggleBottom)]);
        Add("По́мощь", [("Сѵнта́ксисъ ꙗ҆зы́ка", "F1", ShowDocs), ("Покрови́тели", "", ShowSaints), ("Ѡ҆ програ́ммѣ", "", About)]);
    }

    void Add(string title, (string, string, Action)?[] items)
    {
        var top = new MenuItem { Header = title };
        foreach (var it in items)
        {
            if (it == null) { top.Items.Add(new Separator()); continue; }
            var (h, key, act) = it.Value;
            var mi = new MenuItem { Header = h, InputGestureText = key, FontSize = 16 };
            mi.Click += (_, _) => act();
            top.Items.Add(mi);
        }
        MainMenu.Items.Add(top);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        var m = Keyboard.Modifiers;
        Action? a = (k, m) switch
        {
            (Key.S, ModifierKeys.Control) => () => Save(),
            (Key.S, ModifierKeys.Control | ModifierKeys.Shift) => SaveAll,
            (Key.F9, ModifierKeys.Control) => () => _ = RunAction(RunKind.Build),
            (Key.F10, ModifierKeys.Shift) => () => _ = RunAction(RunKind.Run),
            (Key.F10, ModifierKeys.Control | ModifierKeys.Shift) => () => _ = RunAction(RunKind.Console),
            (Key.F2, ModifierKeys.Control) => StopRunning,
            (Key.F4, ModifierKeys.Control) => () => CloseTab(null),
            (Key.F1, ModifierKeys.None) => ShowDocs,
            (Key.OemPlus or Key.Add, ModifierKeys.Control) => () => Zoom(2),
            (Key.OemMinus or Key.Subtract, ModifierKeys.Control) => () => Zoom(-2),
            (Key.OemQuestion or Key.Divide, ModifierKeys.Control) => ToggleComment,
            (Key.G, ModifierKeys.Control) => GotoLinePrompt,
            (Key.Insert, ModifierKeys.Alt) => NewFile,
            (Key.D1, ModifierKeys.Alt) => ToggleProject,
            (Key.D4, ModifierKeys.Alt) => ToggleBottom,
            _ => null,
        };
        if (a != null) { e.Handled = true; a(); }
    }

    // ════════════════════════ дѣйства ════════════════════════
    void SaveAll() { foreach (var t in tabs.Where(t => t.Dirty)) Save(t); }

    void Insert(string s, int caretBack = 0)
    {
        if (active == null || active.ReadOnly) return;
        var ta = active.Ed.TextArea;
        ta.Selection.ReplaceSelectionWithText(s);
        active.Ed.CaretOffset -= caretBack;
        ta.Focus();
    }

    void ToggleComment()
    {
        if (active == null || active.ReadOnly) return;
        var doc = active.Ed.Document;
        var sel = active.Ed.TextArea.Selection;
        int l1 = sel.IsEmpty ? active.Ed.TextArea.Caret.Line : doc.GetLineByOffset(active.Ed.SelectionStart).LineNumber;
        int l2 = sel.IsEmpty ? l1 : doc.GetLineByOffset(Math.Max(active.Ed.SelectionStart, active.Ed.SelectionStart + active.Ed.SelectionLength - 1)).LineNumber;
        var lines = Enumerable.Range(l1, l2 - l1 + 1).Select(doc.GetLineByNumber).ToList();
        bool all = lines.Where(l => doc.GetText(l).Trim().Length > 0).All(l => doc.GetText(l).TrimStart().StartsWith('#'));
        doc.BeginUpdate();
        foreach (var l in lines.AsEnumerable().Reverse())
        {
            var text = doc.GetText(l);
            int ind = text.Length - text.TrimStart().Length;
            if (all)
            {
                if (ind < text.Length && text[ind] == '#')
                    doc.Remove(l.Offset + ind, ind + 1 < text.Length && text[ind + 1] == ' ' ? 2 : 1);
            }
            else if (text.Trim().Length > 0) doc.Insert(l.Offset + ind, "# ");
        }
        doc.EndUpdate();
    }

    void Zoom(double d)
    {
        cfg.CodeFontSize = Math.Clamp(cfg.CodeFontSize + d, 11, 40);
        foreach (var t in tabs.Where(t => t.IsAz)) t.Ed.FontSize = cfg.CodeFontSize;
    }

    void Toggle(FrameworkElement el, Action<bool> remember)
    {
        el.Visibility = el.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        remember(el.Visibility == Visibility.Visible);
    }

    double savedProjectWidth = 290, savedBottom = 210;
    void ToggleProject()
    {
        if (ProjectCol.Width.Value > 0) { savedProjectWidth = ProjectCol.Width.Value; ProjectCol.Width = new GridLength(0); }
        else ProjectCol.Width = new GridLength(savedProjectWidth);
    }
    void ToggleBottom()
    {
        if (BottomRow.Height.Value > 0) { savedBottom = BottomRow.Height.Value; BottomRow.Height = new GridLength(0); }
        else BottomRow.Height = new GridLength(savedBottom);
    }

    void CheckNow()
    {
        var t = active;
        if (t == null || !t.IsAz) return;
        try { AzCompiler.Check(t.Ed.Text); Out("\nПрегрѣше́нїй не ѡ҆брѣте́но. А҆ми́нь.\n", Ok); t.Diag!.Error = null; }
        catch (AzError e) { OutDiag(t, e); t.Diag!.Error = e; }
        t.Ed.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    void GotoLinePrompt()
    {
        if (active == null) return;
        var s = InputBox.Ask(this, "Перейти́ къ строцѣ̀", "Но́мѣръ строкѝ:", active.Ed.TextArea.Caret.Line.ToString());
        if (int.TryParse(s, out var n)) GotoLine(active, n, 1);
    }

    string CurrentDir()
    {
        if (Tree.SelectedItem is TreeViewItem { Tag: string p })
            return Directory.Exists(p) ? p : Path.GetDirectoryName(p)!;
        if (active != null && !active.Path.StartsWith('§')) return Path.GetDirectoryName(active.Path)!;
        return root;
    }

    void NewFile()
    {
        var dir = CurrentDir();
        var name = InputBox.Ask(this, "Но́вый фа́йлъ", $"И҆́мѧ фа́йла (въ {Path.GetRelativePath(root, dir)}):", "но́вый.азъ");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!Path.HasExtension(name)) name += ".азъ";
        var path = Path.Combine(dir, name);
        if (File.Exists(path)) { Status("Таковый фа́йлъ ужѐ є҆́сть.", true); return; }
        File.WriteAllText(path, $"# {name}\n\nглаго́ли «Бл҃гослове́нъ Бг҃ъ на́шъ»\n", new UTF8Encoding(false));
        expanded.Add(dir);
        LoadTree();
        OpenFile(path);
    }

    void NewFolder()
    {
        var dir = CurrentDir();
        var name = InputBox.Ask(this, "Нова́ѧ па́пка", "И҆́мѧ па́пки:", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        Directory.CreateDirectory(Path.Combine(dir, name));
        expanded.Add(dir);
        LoadTree();
    }

    void PickProject()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Ѿве́рзти проектъ", InitialDirectory = root };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var t in tabs.ToList()) CloseTab(t);
        if (tabs.Count > 0) return;
        OpenProject(dlg.FolderName);
    }

    // ════════════════════════ окна ════════════════════════
    void ShowDocs()
    {
        using var s = typeof(MainWindow).Assembly.GetManifestResourceStream("docs.syntax.md")!;
        using var r = new StreamReader(s, Encoding.UTF8);
        new InfoWindow(this, "Сѵнта́ксисъ ꙗ҆зы́ка А҆́зъ", Markdown.ToFlow(r.ReadToEnd()), 900, 760).Show();
    }

    void ShowSaints()
    {
        var doc = Markdown.ToFlow("# Покрови́тели пи́шꙋщихъ и҆ программи́рꙋющихъ");
        foreach (var s in Saints.All) doc.Blocks.Add(SaintBlock(s));
        new InfoWindow(this, "Покрови́тели", doc, 900, 760).Show();
    }

    static Block SaintBlock(Saint s)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = Saints.Tile(s, 120, 150);
        tile.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(tile);
        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = s.Title, FontSize = 22, Foreground = Palette.Cinnabar, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = "Па́мѧть: " + s.Feast, Foreground = Palette.Cinnabar, Margin = new Thickness(0, 2, 0, 6) });
        text.Children.Add(new TextBlock { Text = s.Life, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        text.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xEF, 0xD8)), BorderBrush = (Brush)Application.Current.Resources["Gold"],
            BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 5, 8, 5),
            Child = new TextBlock(new Run("Чемꙋ̀ покрови́тельствꙋетъ: ") { FontWeight = FontWeights.Bold }) { TextWrapping = TextWrapping.Wrap }
                .Also(tb => tb.Inlines.Add(new Run(s.Why))),
        });
        grid.Children.Add(text);
        return new BlockUIContainer(grid);
    }

    void ShowSaint(Saint s)
    {
        var doc = new FlowDocument { FontFamily = (FontFamily)FindResource("Slav"), FontSize = 17, PagePadding = new Thickness(20) };
        doc.Blocks.Add(SaintBlock(s));
        new InfoWindow(this, s.Title, doc, 820, 360).Show();
    }

    void Pray()
    {
        var md = "## Моли́тва предъ на́чатїемъ всѧ́кагѡ дѣ́ла\n\n" +
                 "Царю̀ Нбⷭ҇ный, Ᲊтѣ́шителю, Дꙋ́ше и҆́стины, И҆̀же вездѣ̀ сы́й и҆ всѧ̑ и҆сполнѧ́ѧй, Сокро́вище бл҃ги́хъ и҆ жи́зни Пода́телю, " +
                 "прїидѝ и҆ всели́сѧ въ ны̀, и҆ ѡ҆чи́сти ны̀ ѿ всѧ́кїѧ скве́рны, и҆ спасѝ, бл҃же, дꙋ́шы на́шѧ.\n\n" +
                 "*Пото́мъ — провѣ́рь ло́ги, пере́чти ᲂу҆сло́вїѧ ци́кла и҆ не грешѝ на компиля́торъ.*";
        md = md.Replace("Ᲊтѣ́шителю", "Ѹ҆тѣ́шителю");
        var doc = Markdown.ToFlow(md);
        doc.FontSize = 20;
        new InfoWindow(this, "Моли́тва", doc, 700, 380).Show();
    }

    void About()
    {
        string csc;
        try { csc = AzCompiler.FindCsc(); } catch (AzError e) { csc = e.Message; }
        var md = $"# ☦ Скрипторїй\n\nСреда̀ разрабо́тки для ꙗ҆зы́ка **А҆́зъ**, версія {Lang.Version}.\n\n" +
                 $"- Трансля́торъ: А҆́зъ → C# 5 → .exe на встроенномъ въ Windows .NET Framework 4.x.\n- C#-компиля́торъ: `{csc}`\n" +
                 $"- Сама́ среда̀: .NET {Environment.Version}, WPF, AvalonEdit.\n- Шрифтъ «Пономарь» — Slavonic Computing Initiative (SIL OFL).\n" +
                 "- І҆ко́ны — Викискла́дъ, ѻ҆́бщее достоѧ́нїе (см. Assets/Icons/CREDITS.md).\n\n*«Въ нача́лѣ бѣ̀ сло́во»* (І҆ѡ҃ а҃, а҃).";
        new InfoWindow(this, "Ѡ҆ програ́ммѣ", Markdown.ToFlow(md), 720, 460).Show();
    }

    // ════════════════════════ иконостасъ, клавиатура, кнопки ════════════════════════
    void BuildIconostasis()
    {
        foreach (var s in Saints.All)
        {
            var tile = Saints.Tile(s, 74, 92);
            tile.Margin = new Thickness(0, 0, 6, 0);
            tile.MouseLeftButtonUp += (_, _) => ShowSaint(s);
            Iconostasis.Children.Add(tile);
        }
    }

    void BuildKeyboard()
    {
        Kbd.Children.Add(new TextBlock { Text = "Бꙋ́квы:", Foreground = Brushes.Gray, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var keys = new (string ch, string tip)[]
        {
            ("ѣ", "ѧть"), ("і", "і десѧтеричное"), ("ї", "ї"), ("ѵ", "и҆жи́ца"), ("ѳ", "ѳита̀"), ("ѿ", "ѿ"), ("ѡ", "ѡ҆мега"),
            ("ѻ", "ѻ҆́нъ широ́кїй"), ("ꙋ", "ᲂу҆̀къ"), ("ѧ", "ю҆съ ма́лый"), ("ꙗ", "ꙗ҆"), ("є", "є҆сть ᲂу҆́зкое"), ("ѕ", "ѕѣло̀"),
            ("ѯ", "кси"), ("ѱ", "пси"), ("ъ", "ѥръ"), ("҂", "ты́сѧща"), ("҃", "ти́тло"), ("́", "ѻ҆́ксїа"),
            ("̀", "варі́а"), ("̑", "камо́ра"), ("҆", "звательце"), ("«", "«"), ("»", "»"), ("☦", "крестъ"),
        };
        foreach (var (ch, tip) in keys)
        {
            bool mark = Lang.IsMark(ch[0]);
            var b = new Button { Content = mark ? "◌" + ch : ch, ToolTip = tip, Style = (Style)FindResource("KbdBtn") };
            b.Click += (_, _) => Insert(ch);
            Kbd.Children.Add(b);
        }
    }

    void WireButtons()
    {
        RunBtn.Click += (_, _) => _ = RunAction(RunKind.Run);
        BuildBtn.Click += (_, _) => _ = RunAction(RunKind.Build);
        ConsoleBtn.Click += (_, _) => _ = RunAction(RunKind.Console);
        StopBtn.Click += (_, _) => StopRunning();
        ClearBtn.Click += (_, _) => Output.Document.Blocks.Clear();
        NewFileBtn.Click += (_, _) => NewFile();
        RefreshBtn.Click += (_, _) => LoadTree();
        ProjectStripTab.MouseLeftButtonUp += (_, _) => ToggleProject();
    }

    // ════════════════════════ затвореніе и снимокъ ════════════════════════
    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var dirty = tabs.Where(t => t.Dirty).ToList();
        if (dirty.Count > 0)
        {
            var r = MessageBox.Show(this, $"Не сохранены: {string.Join(", ", dirty.Select(t => t.Name))}. Сохрани́ти?",
                "Скрипторїй", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes) SaveAll();
        }
        StopRunning();
        cfg.OpenFiles = tabs.Where(t => !t.Path.StartsWith('§')).Select(t => t.Path).ToList();
        cfg.Save();
    }

    void Snapshot(string file)
    {
        var el = (FrameworkElement)Content;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap((int)(el.ActualWidth * dpi.DpiScaleX), (int)(el.ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(file)) enc.Save(fs);
        Application.Current.Shutdown();
    }

}

static class Ext
{
    public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
}

/// <summary>Простой вопросъ съ полемъ ввода.</summary>
public sealed class InputBox : Window
{
    readonly TextBox box;
    InputBox(Window owner, string title, string prompt, string value)
    {
        Owner = owner; Title = title; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = owner.FontFamily; FontSize = 16;
        box = new TextBox { Text = value, Margin = new Thickness(0, 6, 0, 10), FontSize = 17 };
        var ok = new Button { Content = "Бл҃гослови́", IsDefault = true, Width = 110, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Ѿмѣни́ти", IsCancel = true, Width = 100 };
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var sp = new StackPanel { Margin = new Thickness(14) };
        sp.Children.Add(new TextBlock { Text = prompt });
        sp.Children.Add(box);
        sp.Children.Add(buttons);
        Content = sp;
        Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    public static string? Ask(Window owner, string title, string prompt, string value)
    {
        var w = new InputBox(owner, title, prompt, value);
        return w.ShowDialog() == true ? w.box.Text : null;
    }
}
