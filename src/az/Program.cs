using System.Diagnostics;
using System.Text;
using Az.Compiler;

Console.OutputEncoding = new UTF8Encoding(false);

const string Help = """
Азъ — православный языкъ программированія.
Трансляторъ: исходникъ .азъ → C# → .exe (встроенный въ Windows .NET Framework).

    az собери  файлъ.азъ [-o имя.exe]   — собрать исполнимый файлъ
    az пусти   файлъ.азъ [доводы...]    — собрать и пустить
    az си      файлъ.азъ                — показать порожденный C#-кодъ
    az провѣрь файлъ.азъ                — только провѣрить на прегрѣшенія
""";

var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["build"] = "build", ["собери"] = "build", ["собрати"] = "build",
    ["run"] = "run", ["пусти"] = "run", ["пуск"] = "run", ["пустити"] = "run",
    ["c#"] = "cs", ["cs"] = "cs", ["си"] = "cs", ["c"] = "cs",
    ["check"] = "check", ["проверь"] = "check", ["провѣрь"] = "check", ["испытай"] = "check",
};

if (args.Length == 0 || args[0] is "-h" or "--help" or "помощь" or "?") { Console.WriteLine(Help); return 0; }
if (args[0] is "-v" or "--version") { Console.WriteLine($"Азъ {Lang.Version}"); return 0; }

string cmd;
List<string> rest;
if (aliases.TryGetValue(args[0], out var c)) { cmd = c; rest = args[1..].ToList(); }
else { cmd = "build"; rest = args.ToList(); }
if (rest.Count == 0) { Console.Error.WriteLine("Укажите исходный файлъ .азъ"); return 2; }

var srcPath = Path.GetFullPath(rest[0]);
rest.RemoveAt(0);
string? outPath = null;
int oi = rest.IndexOf("-o");
if (oi >= 0 && oi + 1 < rest.Count) { outPath = rest[oi + 1]; rest.RemoveRange(oi, 2); }

string src;
try { src = File.ReadAllText(srcPath, Encoding.UTF8); }
catch (Exception e) { Console.Error.WriteLine($"Не можно прочести {srcPath}: {e.Message}"); return 2; }

try
{
    switch (cmd)
    {
        case "cs":
            Console.Write(AzCompiler.ToCSharp(src, Path.GetFileName(srcPath)));
            return 0;
        case "check":
            AzCompiler.Check(src);
            Console.WriteLine("Прегрѣшеній не обрѣтено. Аминь.");
            return 0;
        case "build":
        {
            var outExe = Path.GetFullPath(outPath ?? Path.ChangeExtension(srcPath, ".exe"));
            var r = AzCompiler.Build(src, Path.GetFileName(srcPath), outExe, srcPath);
            Console.WriteLine($"Собрано: {outExe}  (csc изъ .NET Framework). Слава Богу!");
            return 0;
        }
        default: // run
        {
            var outExe = Path.GetFullPath(outPath ?? Path.Combine(Path.GetTempPath(), "az_run", Path.GetFileNameWithoutExtension(srcPath) + ".exe"));
            AzCompiler.Build(src, Path.GetFileName(srcPath), outExe, srcPath);
            var psi = new ProcessStartInfo(outExe) { UseShellExecute = false };
            foreach (var a in rest) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
    }
}
catch (AzError e)
{
    var loc = e.Line > 0 ? $"{srcPath}:{e.Line}:{e.Col}" : srcPath;
    Console.Error.WriteLine($"{loc}: прегрѣшеніе: {e.Message}");
    return 1;
}
