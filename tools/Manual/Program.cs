// Сборщикъ руководства: docs/manual/руководство.html → PDF.
//  • подсвѣчиваетъ код «Аза» тѣмъ же Highlighter, что и IDE;
//  • компилируетъ и запускаетъ каждый примѣръ съ атрибутомъ run и вставляетъ настоящій выводъ;
//  • печатаетъ чрезъ Edge headless, по закладкамъ PDF вычисляетъ страницы и строитъ оглавленіе.
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Az.Compiler;

Console.OutputEncoding = new UTF8Encoding(false);
var root = FindRoot();
var srcPath = Path.Combine(root, "docs", "manual", "руководство.html");
var buildDir = Path.Combine(root, "docs", "manual", ".build");
var outPdf = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "docs", "Азъ — руководство.pdf");
Directory.CreateDirectory(buildDir);

var html = File.ReadAllText(srcPath, Encoding.UTF8).Replace("\r\n", "\n")
    .Replace("<!--CSS-->", File.ReadAllText(Path.Combine(root, "docs", "manual", "manual.css"), Encoding.UTF8));
int failures = 0, examples = 0;

// ---------- блоки кода ----------
html = Regex.Replace(html, @"<pre class=""az""(?<attrs>[^>]*)>\n?(?<code>.*?)</pre>", m =>
{
    var attrs = ParseAttrs(m.Groups["attrs"].Value);
    var code = WebUtility.HtmlDecode(m.Groups["code"].Value).TrimEnd('\n', ' ');
    var sb = new StringBuilder();
    sb.Append("<figure class=\"code\">");
    if (attrs.TryGetValue("title", out var title))
        sb.Append($"<figcaption><span class=\"file\">☦</span>{WebUtility.HtmlEncode(title)}</figcaption>");
    bool nonum = attrs.ContainsKey("nonum");
    sb.Append("<pre class=\"src\"><code>");
    var lines = code.Split('\n');
    for (int i = 0; i < lines.Length; i++)
    {
        if (!nonum) sb.Append($"<span class=\"ln\">{i + 1}</span>");
        sb.Append(HighlightLine(lines[i])).Append('\n');
    }
    sb.Append("</code></pre>");
    if (attrs.ContainsKey("run") || attrs.ContainsKey("error"))
    {
        examples++;
        var stdin = attrs.GetValueOrDefault("stdin", "").Replace("\\n", "\n");
        var (ok, text, isErr) = RunExample(code, stdin, attrs.GetValueOrDefault("title", "примѣръ.азъ"), attrs.ContainsKey("error"));
        if (!ok) { failures++; Console.WriteLine($"✗ примѣръ «{attrs.GetValueOrDefault("title", code.Split('\n')[0])}»: {text}"); }
        var label = attrs.ContainsKey("error") ? "Прегрѣше́нїе" : "Исхо́дъ";
        sb.Append($"<div class=\"out{(isErr ? " err" : "")}\"><div class=\"out-h\">{label}{(stdin.Length > 0 ? " <span class=\"stdin\">вводъ: " + WebUtility.HtmlEncode(stdin.Replace("\n", " ⏎ ").Trim()) + "</span>" : "")}</div><pre>{text}</pre></div>");
    }
    sb.Append("</figure>");
    return sb.ToString();
}, RegexOptions.Singleline);

// ---------- строчный кодъ <az>…</az> ----------
html = Regex.Replace(html, "<az>(.*?)</az>", m => "<code class=\"az\">" + HighlightLine(WebUtility.HtmlDecode(m.Groups[1].Value)) + "</code>", RegexOptions.Singleline);

html = html.Replace("{{VERSION}}", Lang.Version).Replace("{{YEAR}}", DateTime.Now.Year.ToString())
           .Replace("{{FONT}}", new Uri(Path.Combine(root, "src", "Skriptorij", "Assets", "Fonts", "Ponomar-Regular.ttf")).AbsoluteUri)
           .Replace("{{ASSETS}}", new Uri(Path.Combine(root, "docs", "manual", "assets") + Path.DirectorySeparatorChar).AbsoluteUri)
           .Replace("{{ICONS}}", new Uri(Path.Combine(root, "src", "Skriptorij", "Assets", "Icons") + Path.DirectorySeparatorChar).AbsoluteUri);

if (failures > 0) { Console.WriteLine($"Провалено примѣровъ: {failures}. Руководство не собрано."); return 1; }
Console.WriteLine($"Примѣровъ запущено и провѣрено: {examples}.");

// ---------- оглавленіе ----------
var heads = Regex.Matches(html, @"<h(?<lvl>[12])(?<attrs>[^>]*\bid=""(?<id>[^""]+)""[^>]*)>(?<body>.*?)</h\k<lvl>>", RegexOptions.Singleline)
    .Select(m => (lvl: int.Parse(m.Groups["lvl"].Value), id: m.Groups["id"].Value, text: PlainText(m.Groups["body"].Value),
                  shown: PlainText(Regex.Replace(m.Groups["body"].Value, @"<span class=""(no|n)"">(?:[^<]|<span class=""cs"">[^<]*</span>)*</span>", "")),
                  num: Regex.Match(m.Groups["attrs"].Value, @"data-num=""([^""]*)""").Groups[1].Value))
    .Where(h => !h.id.StartsWith("notoc"))
    .ToList();

string Toc(Dictionary<string, int>? pages)
{
    var sb = new StringBuilder("<ol class=\"toc\">");
    foreach (var h in heads)
    {
        var pg = pages != null && Find(pages, h.text) is int p ? p.ToString() : "&#8199;&#8199;";
        sb.Append($"<li class=\"l{h.lvl}\"><a href=\"#{h.id}\"><span class=\"tn\">{h.num}</span><span class=\"tt\">{WebUtility.HtmlEncode(h.shown)}</span><span class=\"dots\"></span><span class=\"pg\">{pg}</span></a></li>");
    }
    return sb.Append("</ol>").ToString();
}

var pass1 = Path.Combine(buildDir, "руководство.html");
File.WriteAllText(pass1, html.Replace("<!--TOC-->", Toc(null)), new UTF8Encoding(false));
var tmpPdf = Path.Combine(buildDir, "проходъ1.pdf");
Print(pass1, tmpPdf);
var pages1 = OutlinePages(File.ReadAllBytes(tmpPdf), out int count1);
Console.WriteLine($"Первый проходъ: {count1} страницъ, закладокъ {pages1.Count}.");
var missing = heads.Where(h => Find(pages1, h.text) == null).Select(h => h.text).ToList();
if (missing.Count > 0) Console.WriteLine("Не обрѣтены въ закладкахъ: " + string.Join("; ", missing));

File.WriteAllText(pass1, html.Replace("<!--TOC-->", Toc(pages1)), new UTF8Encoding(false));
Print(pass1, outPdf);
var pages2 = OutlinePages(File.ReadAllBytes(outPdf), out int count2);
bool stable = heads.All(h => Find(pages1, h.text) == Find(pages2, h.text));
Console.WriteLine($"Готово: {outPdf} ({count2} страницъ){(stable ? "" : " — ВНИМАНІЕ: страницы сдвинулись между проходами")}.");
return 0;

// ════════════════════════════════════════════════════════════
static string Key(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");

static int? Find(Dictionary<string, int> pages, string text)
{
    var k = Key(text);
    if (pages.TryGetValue(k, out var p)) return p;
    var pre = k[..Math.Min(10, k.Length)];
    foreach (var (key, pg) in pages) if (key.StartsWith(pre)) return pg;
    // запасной путь: по номеру раздѣла («8.4…»)
    var num = Regex.Match(text, @"^\s*(\d+(?:\.\d+)*)").Groups[1].Value.Replace(".", "");
    if (num.Length > 0)
        foreach (var (key, pg) in pages)
            if (key.StartsWith(num) && key.Length > num.Length && char.IsLetter(key[num.Length])) return pg;
    return null;
}

static string PlainText(string html) =>
    Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<br\\s*/?>", " "), "<[^>]+>", "")), @"\s+", " ").Trim();

static Dictionary<string, string> ParseAttrs(string s) =>
    Regex.Matches(s, @"(\w+)(?:=""([^""]*)"")?").ToDictionary(m => m.Groups[1].Value, m => WebUtility.HtmlDecode(m.Groups[2].Value));

static string HighlightLine(string line)
{
    var sb = new StringBuilder();
    int pos = 0;
    foreach (var s in Highlighter.ClassifyLine(line))
    {
        if (s.Start > pos) sb.Append(WebUtility.HtmlEncode(line[pos..s.Start]));
        var t = line.Substring(s.Start, s.Length);
        var cls = s.Kind switch
        {
            Hl.Keyword => "k", Hl.Def => "d", Hl.FuncName => "f", Hl.Operator => "o", Hl.Const => "c", Hl.Type => "t",
            Hl.Builtin => "b", Hl.String => "s", Hl.Number => "n", Hl.Comment => "m", _ => "",
        };
        if (s.Kind == Hl.FuncName)
        {
            int n = 1;
            while (n < t.Length && Lang.IsMark(t[n])) n++;
            sb.Append($"<span class=\"f\"><span class=\"buk\">{WebUtility.HtmlEncode(t[..n])}</span>{WebUtility.HtmlEncode(t[n..])}</span>");
        }
        else sb.Append($"<span class=\"{cls}\">{WebUtility.HtmlEncode(t)}</span>");
        pos = s.Start + s.Length;
    }
    if (pos < line.Length) sb.Append(WebUtility.HtmlEncode(line[pos..]));
    return sb.ToString();
}

static (bool ok, string html, bool isErr) RunExample(string code, string stdin, string name, bool expectError)
{
    if (expectError)
    {
        try { AzCompiler.Check(code); return (false, "ожидалось прегрѣшеніе, но его нѣтъ", true); }
        catch (AzError e) { return (true, WebUtility.HtmlEncode($"{name}:{e.Line}:{e.Col}: прегрѣшеніе: {e.Message}"), true); }
    }
    var exe = Path.Combine(Path.GetTempPath(), "az_manual", "примѣръ.exe");
    try { AzCompiler.Build(code, name, exe); }
    catch (AzError e) { return (false, $"строка {e.Line}: {e.Message}", true); }
    var psi = new ProcessStartInfo(exe)
    {
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, CreateNoWindow = true,
    };
    using var p = Process.Start(psi)!;
    var so = p.StandardOutput.ReadToEndAsync();
    var se = p.StandardError.ReadToEndAsync();
    p.StandardInput.BaseStream.Write(Encoding.UTF8.GetBytes(stdin));
    p.StandardInput.Close();
    if (!p.WaitForExit(20000)) { p.Kill(); return (false, "примѣръ бѣжалъ слишкомъ долго", true); }
    var outText = so.Result.Replace("\r\n", "\n").TrimEnd('\n');
    var errText = se.Result.Replace("\r\n", "\n").Trim('\n');
    var h = WebUtility.HtmlEncode(outText);
    if (errText.Length > 0) h += (h.Length > 0 ? "\n" : "") + "<span class=\"e\">" + WebUtility.HtmlEncode(errText) + "</span>";
    // анаѳема въ примѣрѣ допустима — она показываетъ ошибку выполненія
    return (true, h, false);
}

static void Print(string html, string pdf)
{
    var edge = new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" }
        .FirstOrDefault(File.Exists) ?? throw new Exception("Edge не обрѣтенъ");
    if (File.Exists(pdf)) File.Delete(pdf);
    var prof = Path.Combine(Path.GetTempPath(), "az_manual_edge_" + Guid.NewGuid().ToString("N")[..8]);
    var psi = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var a in new[] { "--headless=new", "--disable-gpu", "--no-pdf-header-footer", "--generate-pdf-document-outline",
                 "--virtual-time-budget=10000", "--run-all-compositor-stages-before-draw", $"--user-data-dir={prof}",
                 $"--print-to-pdf={pdf}", new Uri(html).AbsoluteUri })
        psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    p.WaitForExit(180000);
    for (int i = 0; i < 120 && !File.Exists(pdf); i++) Thread.Sleep(500);
    Thread.Sleep(500);
    if (!File.Exists(pdf)) throw new Exception("Edge не напечаталъ PDF");
    try { Directory.Delete(prof, true); } catch { }
}

// Закладки (outline) PDF → номеръ страницы по тексту заголовка.
static Dictionary<string, int> OutlinePages(byte[] pdf, out int pageCount)
{
    var s = Encoding.Latin1.GetString(pdf);
    var objs = new Dictionary<int, string>();
    foreach (Match m in Regex.Matches(s, @"(\d+) 0 obj\s*(.*?)endobj", RegexOptions.Singleline))
        objs[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
    var pageOrder = new List<int>();
    var rootPages = objs.First(o => Regex.IsMatch(o.Value, @"/Type /Pages") && !o.Value.Contains("/Parent")).Key;
    void Walk(int id)
    {
        var o = objs[id];
        if (Regex.IsMatch(o, @"/Type /Pages"))
            foreach (Match k in Regex.Matches(Regex.Match(o, @"/Kids \[(.*?)\]", RegexOptions.Singleline).Groups[1].Value, @"(\d+) 0 R"))
                Walk(int.Parse(k.Groups[1].Value));
        else pageOrder.Add(id);
    }
    Walk(rootPages);
    pageCount = pageOrder.Count;
    var res = new Dictionary<string, int>();
    foreach (var o in objs.Values)
    {
        var t = Regex.Match(o, @"/Title <FEFF([0-9A-F]+)>");
        var d = Regex.Match(o, @"/Dest \[(\d+) 0 R");
        if (!t.Success || !d.Success) continue;
        var hex = t.Groups[1].Value;
        var bytes = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        var title = Encoding.BigEndianUnicode.GetString(bytes);
        int idx = pageOrder.IndexOf(int.Parse(d.Groups[1].Value));
        if (idx >= 0) res.TryAdd(Key(title), idx + 1);
    }
    return res;
}

static string FindRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "Az.sln"))) return d.FullName;
    throw new Exception("не обрѣтенъ корень проекта (Az.sln)");
}
