// Эталонные испытанія: новый трансляторъ долженъ давать ровно тотъ же выводъ,
// что и Python-прототипъ (tests/эталоны.json снятъ съ него).
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Az.Compiler;

Console.OutputEncoding = new UTF8Encoding(false);
var root = FindRoot();
var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "эталоны.json"))).RootElement;
int ok = 0, bad = 0;

foreach (var c in cases.EnumerateArray())
{
    string name;
    List<string> fails = [];
    if (c.TryGetProperty("source", out var srcEl))
    {
        var src = srcEl.GetString()!;
        name = "прегрѣшеніе: " + src.Replace("\n", "⏎");
        try
        {
            AzCompiler.Check(src);
            fails.Add("ожидалось прегрѣшеніе, но всё прошло");
        }
        catch (AzError e)
        {
            var wantLine = c.GetProperty("error_line").GetInt32();
            var want = c.GetProperty("error").GetString();
            if (e.Line != wantLine) fails.Add($"строка {e.Line}, ожидалась {wantLine}");
            if (e.Message != want) fails.Add($"«{e.Message}» вмѣсто «{want}»");
        }
    }
    else
    {
        var file = c.GetProperty("file").GetString()!;
        var stdin = c.GetProperty("stdin").GetString()!;
        name = file + (stdin.Length > 0 ? $" ⟵ {stdin.Replace("\n", "⏎")}" : "");
        var path = Path.Combine(root, file);
        var exe = Path.Combine(Path.GetTempPath(), "az_tests", Path.GetFileNameWithoutExtension(file) + ".exe");
        try
        {
            AzCompiler.Build(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path), exe, path);
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            p.StandardInput.BaseStream.Write(Encoding.UTF8.GetBytes(stdin));
            p.StandardInput.Close();
            p.WaitForExit();
            Compare("вывод", c.GetProperty("stdout").GetString()!, so.Result.Replace("\r\n", "\n"), fails);
            Compare("stderr", c.GetProperty("stderr").GetString()!, se.Result.Replace("\r\n", "\n"), fails);
            if (p.ExitCode != c.GetProperty("code").GetInt32()) fails.Add($"кодъ {p.ExitCode}, ожидался {c.GetProperty("code").GetInt32()}");
        }
        catch (AzError e) { fails.Add($"прегрѣшеніе: {e.Message} (строка {e.Line})"); }
    }
    if (fails.Count == 0) { ok++; Console.WriteLine($"  ✓ {name}"); }
    else { bad++; Console.WriteLine($"  ✗ {name}"); foreach (var f in fails) Console.WriteLine("      " + f); }
}

Console.WriteLine(bad == 0 ? $"\nВсе {ok} испытаній пройдены. Аминь." : $"\nПройдено {ok}, провалено {bad}.");
return bad == 0 ? 0 : 1;

static void Compare(string what, string want, string got, List<string> fails)
{
    if (want == got) return;
    int i = 0;
    while (i < want.Length && i < got.Length && want[i] == got[i]) i++;
    string Cut(string s) => s.Substring(i, Math.Min(60, s.Length - i)).Replace("\n", "⏎");
    fails.Add($"{what} расходится съ знака {i}: ожидалось «{Cut(want)}», получено «{Cut(got)}»");
}

static string FindRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, "tests", "эталоны.json"))) d = d.Parent;
    return d?.FullName ?? throw new Exception("не обрѣтенъ корень проекта");
}
