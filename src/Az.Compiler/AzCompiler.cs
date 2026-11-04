using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Az.Compiler;

public sealed record BuildResult(string ExePath, string Csc, string Log, bool FromCache);

/// <summary>Главный входъ: .азъ → C# → .exe чрезъ встроенный въ Windows csc.exe (.NET Framework 4.x).</summary>
public static class AzCompiler
{
    static readonly Lazy<string> runtime = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream("Az.Compiler.Runtime.AzRuntime.cs")
            ?? throw new InvalidOperationException("рантаймъ не вложенъ въ сборку");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    });

    public static string RuntimeSource => runtime.Value;

    /// <summary>Только провѣрка: лексеръ, парсеръ и кодогенъ (безъ компиляціи C#).</summary>
    public static void Check(string src) => ToCSharp(src, "провѣрка");

    public static string ToCSharp(string src, string srcName, string? linePath = null) =>
        CodeGen.Generate(Parser.Parse(src), srcName, linePath);

    /// <summary>Ищетъ csc.exe: AZ_CSC, затѣмъ .NET Framework 4.x въ %WINDIR%.</summary>
    public static string FindCsc()
    {
        var forced = Environment.GetEnvironmentVariable("AZ_CSC");
        if (!string.IsNullOrEmpty(forced))
        {
            if (File.Exists(forced)) return forced;
            throw new AzError($"AZ_CSC указываетъ на несуществующій файлъ: {forced}");
        }
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var fw in new[] { "Framework64", "Framework" })
        {
            var p = Path.Combine(win, "Microsoft.NET", fw, "v4.0.30319", "csc.exe");
            if (File.Exists(p)) return p;
        }
        throw new AzError("не обрѣтенъ csc.exe изъ .NET Framework 4.x (обычно C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319). " +
                          "Включите «.NET Framework 4.8» въ компонентахъ Windows или задайте AZ_CSC.");
    }

    public static BuildResult Build(string src, string srcName, string outExe, string? srcPath = null)
    {
        var cs = ToCSharp(src, srcName, srcPath);
        return BuildCSharp(cs, outExe);
    }

    public static BuildResult BuildCSharp(string cs, string outExe)
    {
        var csc = FindCsc();
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(cs + "\0" + RuntimeSource + "\0" + Lang.Version)))[..16];
        var wd = Path.Combine(Path.GetTempPath(), "az_build", key);
        var exe = Path.Combine(wd, "prog.exe");
        bool cached = File.Exists(exe);
        string log = "";
        if (!cached)
        {
            Directory.CreateDirectory(wd);
            var utf8bom = new UTF8Encoding(true);
            File.WriteAllText(Path.Combine(wd, "prog.cs"), cs, utf8bom);
            File.WriteAllText(Path.Combine(wd, "AzRuntime.cs"), RuntimeSource, utf8bom);
            var psi = new ProcessStartInfo(csc)
            {
                WorkingDirectory = wd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var a in new[] { "/nologo", "/utf8output", "/codepage:65001", "/target:exe", "/optimize+",
                         "/platform:anycpu", "/nowarn:162,168,219,1717", "/out:prog.tmp.exe", "prog.cs", "AzRuntime.cs" })
                psi.ArgumentList.Add(a);
            using var pr = Process.Start(psi)!;
            var so = pr.StandardOutput.ReadToEndAsync();
            var se = pr.StandardError.ReadToEndAsync();
            pr.WaitForExit();
            log = (so.Result + se.Result).Trim();
            var tmpExe = Path.Combine(wd, "prog.tmp.exe");
            if (pr.ExitCode != 0 || !File.Exists(tmpExe))
                throw new AzError("C#-компиляторъ возроптал (сіе ошибка транслятора, а не ваша):\n" + log);
            File.Move(tmpExe, exe, true);
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(outExe));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(exe, outExe, true);
        return new BuildResult(outExe, csc, log, cached);
    }
}
