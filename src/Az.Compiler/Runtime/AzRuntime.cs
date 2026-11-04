// ============================================================
//  Рантаймъ языка «Азъ» для .NET Framework 4.x.
//  ВНИМАНІЕ: сей файлъ собирается встроеннымъ въ Windows
//  компиляторомъ csc.exe, который знаетъ лишь C# 5.
//  Не употребляйте здѣсь $"…", ?., =>-члены, nameof, out var и проч.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class AzList
{
    public readonly List<object> Items;
    public AzList() { Items = new List<object>(); }
    public AzList(List<object> items) { Items = items; }
}

public sealed class AzDict
{
    public readonly List<object> Keys = new List<object>();
    public readonly List<object> Values = new List<object>();
    readonly Dictionary<object, int> index = new Dictionary<object, int>(AzKeyComparer.Instance);
    int nullIndex = -1;

    public int Find(object k)
    {
        if (k == null) return nullIndex;
        int i;
        return index.TryGetValue(k, out i) ? i : -1;
    }
    public void Set(object k, object v)
    {
        int i = Find(k);
        if (i >= 0) { Values[i] = v; return; }
        Keys.Add(k); Values.Add(v);
        if (k == null) nullIndex = Keys.Count - 1; else index[k] = Keys.Count - 1;
    }
}

sealed class AzKeyComparer : IEqualityComparer<object>
{
    public static readonly AzKeyComparer Instance = new AzKeyComparer();
    public new bool Equals(object a, object b) { return Az.Eq(a, b); }
    public int GetHashCode(object o)
    {
        if (o == null) return 0;
        if (o is long) return ((long)o).GetHashCode();
        if (o is bool) return ((bool)o ? 1L : 0L).GetHashCode();
        if (o is double)
        {
            double d = (double)o;
            if (d == Math.Floor(d) && d >= long.MinValue && d <= long.MaxValue) return ((long)d).GetHashCode();
            return d.GetHashCode();
        }
        string s = o as string;
        if (s != null) return StringComparer.Ordinal.GetHashCode(s);
        AzList l = o as AzList;
        if (l != null) return l.Items.Count;
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }
}

public sealed class AzAnathema : Exception
{
    public AzAnathema(string msg) : base(msg) { }
}

public sealed class AzExit : Exception
{
    public readonly int Code;
    public AzExit(int code) { Code = code; }
}

public static class Az
{
    public static int Line;
    public static readonly object Nil = null;
    public static readonly object True = true;
    public static readonly object False = false;

    // ---------- вводъ-выводъ ----------
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool WriteConsoleW(IntPtr h, string s, int n, out int written, IntPtr reserved);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool ReadConsoleW(IntPtr h, [Out] char[] buf, int n, out int read, IntPtr ctl);

    static bool outConsole, errConsole, inConsole;
    static StreamWriter outStream, errStream;
    static StreamReader inStream;
    static StringBuilder line = new StringBuilder();
    static bool lineEmpty = true;

    static void Init()
    {
        UTF8Encoding utf8 = new UTF8Encoding(false);
        bool win = Environment.OSVersion.Platform == PlatformID.Win32NT;
        outConsole = win && !Console.IsOutputRedirected;
        errConsole = win && !Console.IsErrorRedirected;
        inConsole = win && !Console.IsInputRedirected;
        if (!outConsole) outStream = new StreamWriter(Console.OpenStandardOutput(), utf8);
        if (!errConsole) errStream = new StreamWriter(Console.OpenStandardError(), utf8);
        if (!inConsole) inStream = new StreamReader(Console.OpenStandardInput(), utf8);
    }

    static void Raw(string s, bool err)
    {
        if (s.Length == 0) return;
        if (err ? errConsole : outConsole)
        {
            int w;
            WriteConsoleW(GetStdHandle(err ? -12 : -11), s, s.Length, out w, IntPtr.Zero);
        }
        else
        {
            StreamWriter sw = err ? errStream : outStream;
            sw.Write(s);
        }
    }
    static void Flush()
    {
        if (outStream != null) outStream.Flush();
        if (errStream != null) errStream.Flush();
    }

    public static void Print(params object[] args)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(ToStr(args[i]));
        }
        sb.Append('\n');
        Raw(sb.ToString(), false);
        Flush();
    }

    public static object Input(object prompt)
    {
        if (prompt != null) Raw(ToStr(prompt), false);
        Flush();
        if (inConsole)
        {
            char[] buf = new char[4096];
            StringBuilder sb = new StringBuilder();
            IntPtr h = GetStdHandle(-10);
            while (true)
            {
                int got;
                if (!ReadConsoleW(h, buf, buf.Length, out got, IntPtr.Zero) || got == 0) break;
                sb.Append(buf, 0, got);
                if (sb.Length > 0 && sb[sb.Length - 1] == '\n') break;
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }
        string s = inStream.ReadLine();
        return s == null ? "" : s.TrimEnd('\r');
    }

    // ---------- запускъ ----------
    public static void Discard(object o) { }

    public static int Run(Action body)
    {
        Init();
        int code = 0;
        // тѣло бѣжитъ въ потокѣ съ большимъ стекомъ, дабы рекурсія была не скудна
        System.Threading.Thread th = new System.Threading.Thread(delegate () { code = Guarded(body); }, 256 * 1024 * 1024);
        th.Start();
        th.Join();
        Flush();
        return code;
    }

    static int Guarded(Action body)
    {
        int code = 0;
        try { body(); }
        catch (AzExit e) { code = e.Code; }
        catch (AzAnathema e)
        {
            Flush();
            Raw("\nАнаѳема! Строка " + Line + ": " + e.Message + "\n", true);
            code = 1;
        }
        catch (InsufficientExecutionStackException)
        {
            Flush();
            Raw("\nАнаѳема! Строка " + Line + ": бездна рекурсіи (слишкомъ глубокое самопризываніе)\n", true);
            code = 1;
        }
        catch (Exception e)
        {
            Flush();
            Raw("\nАнаѳема! Строка " + Line + ": внутренняя бѣда: " + e.Message + "\n", true);
            code = 2;
        }
        return code;
    }

    public static void Die(string msg) { throw new AzAnathema(msg); }
    public static void Anathema(object msg) { throw new AzAnathema(ToStr(msg)); }
    public static void ExitWith(object v)
    {
        if (v is long) throw new AzExit((int)(long)v);
        throw new AzExit(v == null || Truthy(v) ? 0 : 1);
    }
    public static void Enter() { System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack(); }

    // ---------- построеніе ----------
    public static object Bool(bool b) { return b ? True : False; }
    public static object List(params object[] items) { return new AzList(new List<object>(items)); }
    public static object Dict(params object[] kv)
    {
        AzDict d = new AzDict();
        for (int i = 0; i + 1 < kv.Length; i += 2) d.Set(kv[i], kv[i + 1]);
        return d;
    }

    public static string TypeName(object v)
    {
        if (v == null) return "безвѣстно";
        if (v is bool) return "истинность";
        if (v is long) return "цѣлое";
        if (v is double) return "дробное";
        if (v is string) return "слово";
        if (v is AzList) return "свитокъ";
        if (v is AzDict) return "словникъ";
        return "?";
    }

    // ---------- строки ----------
    public static string FormatFloat(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsPositiveInfinity(d)) return "inf";
        if (double.IsNegativeInfinity(d)) return "-inf";
        string s = d.ToString("G15", CultureInfo.InvariantCulture);
        int e = s.IndexOf('E');
        if (e >= 0)
        {
            string mant = s.Substring(0, e), exp = s.Substring(e + 1);
            char sign = exp[0] == '-' ? '-' : '+';
            exp = exp.TrimStart('+', '-').TrimStart('0');
            if (exp.Length < 2) exp = exp.PadLeft(2, '0');
            s = mant + "e" + sign + exp;
        }
        return s;
    }

    static void Repr(StringBuilder b, object v, bool quote)
    {
        if (v == null) { b.Append("безвѣстно"); return; }
        if (v is bool) { b.Append((bool)v ? "истина" : "ложь"); return; }
        if (v is long) { b.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
        if (v is double) { b.Append(FormatFloat((double)v)); return; }
        string s = v as string;
        if (s != null) { if (quote) b.Append('«'); b.Append(s); if (quote) b.Append('»'); return; }
        AzList l = v as AzList;
        if (l != null)
        {
            b.Append('[');
            for (int i = 0; i < l.Items.Count; i++) { if (i > 0) b.Append(", "); Repr(b, l.Items[i], true); }
            b.Append(']');
            return;
        }
        AzDict d = v as AzDict;
        if (d != null)
        {
            b.Append('{');
            for (int i = 0; i < d.Keys.Count; i++)
            {
                if (i > 0) b.Append(", ");
                Repr(b, d.Keys[i], true); b.Append(": "); Repr(b, d.Values[i], true);
            }
            b.Append('}');
            return;
        }
        b.Append(v.ToString());
    }
    public static string ToStr(object v)
    {
        string s = v as string;
        if (s != null) return s;
        StringBuilder b = new StringBuilder();
        Repr(b, v, false);
        return b.ToString();
    }

    // «буква» = знакъ съ надстрочными (ударенія, титла)
    static int[] Letters(string s) { return StringInfo.ParseCombiningCharacters(s); }

    // ---------- истинность и равенство ----------
    public static bool Truthy(object v)
    {
        if (v == null) return false;
        if (v is bool) return (bool)v;
        if (v is long) return (long)v != 0;
        if (v is double) return (double)v != 0.0;
        string s = v as string; if (s != null) return s.Length > 0;
        AzList l = v as AzList; if (l != null) return l.Items.Count > 0;
        AzDict d = v as AzDict; if (d != null) return d.Keys.Count > 0;
        return true;
    }
    static bool IsNum(object v) { return v is long || v is double || v is bool; }
    static bool IsIntLike(object v) { return v is long || v is bool; }
    static long AsLong(object v) { return v is bool ? ((bool)v ? 1L : 0L) : (long)v; }
    public static double Num(object v)
    {
        if (v is double) return (double)v;
        if (v is long) return (long)v;
        if (v is bool) return (bool)v ? 1.0 : 0.0;
        Die("ожидалось число, а дано " + TypeName(v));
        return 0;
    }

    public static bool Eq(object a, object b)
    {
        if (IsNum(a) && IsNum(b))
        {
            if (IsIntLike(a) && IsIntLike(b)) return AsLong(a) == AsLong(b);
            return Num(a) == Num(b);
        }
        if (a == null || b == null) return a == null && b == null;
        if (a.GetType() != b.GetType()) return false;
        string sa = a as string;
        if (sa != null) return string.Equals(sa, (string)b, StringComparison.Ordinal);
        AzList la = a as AzList;
        if (la != null)
        {
            AzList lb = (AzList)b;
            if (la.Items.Count != lb.Items.Count) return false;
            for (int i = 0; i < la.Items.Count; i++) if (!Eq(la.Items[i], lb.Items[i])) return false;
            return true;
        }
        return ReferenceEquals(a, b);
    }
    static bool Less(object a, object b)
    {
        if (IsNum(a) && IsNum(b))
        {
            if (IsIntLike(a) && IsIntLike(b)) return AsLong(a) < AsLong(b);
            return Num(a) < Num(b);
        }
        string sa = a as string, sb = b as string;
        if (sa != null && sb != null)
        {
            byte[] x = Encoding.UTF8.GetBytes(sa), y = Encoding.UTF8.GetBytes(sb);
            int n = Math.Min(x.Length, y.Length);
            for (int i = 0; i < n; i++) if (x[i] != y[i]) return x[i] < y[i];
            return x.Length < y.Length;
        }
        Die("не можно сравнити " + TypeName(a) + " съ " + TypeName(b));
        return false;
    }
    public static object OpEq(object a, object b) { return Bool(Eq(a, b)); }
    public static object OpNe(object a, object b) { return Bool(!Eq(a, b)); }
    public static object OpLt(object a, object b) { return Bool(Less(a, b)); }
    public static object OpGt(object a, object b) { return Bool(Less(b, a)); }
    public static object OpLe(object a, object b) { return Bool(!Less(b, a)); }
    public static object OpGe(object a, object b) { return Bool(!Less(a, b)); }
    public static object Not(object a) { return Bool(!Truthy(a)); }

    // ---------- аріѳметика ----------
    public static object Add(object a, object b)
    {
        if (a is long && b is long) return unchecked((long)a + (long)b);
        if (a is string || b is string) return ToStr(a) + ToStr(b);
        AzList la = a as AzList, lb = b as AzList;
        if (la != null && lb != null)
        {
            List<object> r = new List<object>(la.Items);
            r.AddRange(lb.Items);
            return new AzList(r);
        }
        if (IsNum(a) && IsNum(b))
        {
            if (IsIntLike(a) && IsIntLike(b)) return unchecked(AsLong(a) + AsLong(b));
            return Num(a) + Num(b);
        }
        Die("не можно сложити " + TypeName(a) + " и " + TypeName(b));
        return null;
    }
    public static object Sub(object a, object b)
    {
        if (IsIntLike(a) && IsIntLike(b)) return unchecked(AsLong(a) - AsLong(b));
        return Num(a) - Num(b);
    }
    public static object Mul(object a, object b)
    {
        string s = a as string;
        if (s != null && b is long)
        {
            StringBuilder sb = new StringBuilder();
            for (long i = 0; i < (long)b; i++) sb.Append(s);
            return sb.ToString();
        }
        if (IsIntLike(a) && IsIntLike(b)) return unchecked(AsLong(a) * AsLong(b));
        return Num(a) * Num(b);
    }
    static void CheckZero(object b)
    {
        if (Num(b) == 0) Die("дѣленіе на нуль, егоже несть въ кириллической цыфири");
    }
    public static object Div(object a, object b)
    {
        CheckZero(b);
        if (IsIntLike(a) && IsIntLike(b) && AsLong(a) % AsLong(b) == 0) return AsLong(a) / AsLong(b);
        return Num(a) / Num(b);
    }
    public static object IDiv(object a, object b)
    {
        CheckZero(b);
        if (IsIntLike(a) && IsIntLike(b))
        {
            long x = AsLong(a), y = AsLong(b), q = x / y;
            if (x % y != 0 && ((x < 0) != (y < 0))) q--;
            return q;
        }
        return Math.Floor(Num(a) / Num(b));
    }
    public static object Mod(object a, object b)
    {
        CheckZero(b);
        if (IsIntLike(a) && IsIntLike(b))
        {
            long x = AsLong(a), y = AsLong(b), m = x % y;
            if (m != 0 && ((m < 0) != (y < 0))) m += y;
            return m;
        }
        return Num(a) % Num(b);
    }
    public static object Neg(object a)
    {
        if (IsIntLike(a)) return unchecked(-AsLong(a));
        return -Num(a);
    }

    // ---------- указаніе (ѿ) ----------
    static long NeedInt(object k, string what)
    {
        if (IsIntLike(k)) return AsLong(k);
        if (k is double && (double)k == Math.Floor((double)k)) return (long)(double)k;
        Die(what + " долженъ быти цѣлымъ, а дано " + TypeName(k));
        return 0;
    }
    public static object Index(object a, object k)
    {
        AzDict d = a as AzDict;
        if (d != null) { int j = d.Find(k); return j < 0 ? null : d.Values[j]; }
        AzList l = a as AzList;
        if (l != null)
        {
            long i = NeedInt(k, "указатель");
            if (i < 1 || i > l.Items.Count)
                Die("указатель " + i + " внѣ свитка (длина " + l.Items.Count + "). Помни: счётъ ѿ единицы, ибо нуля несть");
            return l.Items[(int)(i - 1)];
        }
        string s = a as string;
        if (s != null)
        {
            int[] st = Letters(s);
            long i = NeedInt(k, "указатель");
            if (i < 1 || i > st.Length) Die("указатель " + i + " внѣ слова (длина " + st.Length + ")");
            int from = st[i - 1], to = i < st.Length ? st[i] : s.Length;
            return s.Substring(from, to - from);
        }
        Die("не можно взяти ѿ " + TypeName(a));
        return null;
    }
    public static void SetIndex(object a, object k, object v)
    {
        AzDict d = a as AzDict;
        if (d != null) { d.Set(k, v); return; }
        AzList l = a as AzList;
        if (l != null)
        {
            long i = NeedInt(k, "указатель");
            if (i == l.Items.Count + 1) { l.Items.Add(v); return; }
            if (i < 1 || i > l.Items.Count)
                Die("указатель " + i + " внѣ свитка (длина " + l.Items.Count + "). Помни: счётъ ѿ единицы, ибо нуля несть");
            l.Items[(int)(i - 1)] = v;
            return;
        }
        Die("не можно положити въ " + TypeName(a));
    }

    // ---------- обходъ ----------
    public static List<object> Iter(object a)
    {
        AzList l = a as AzList;
        if (l != null) return l.Items;
        AzDict d = a as AzDict;
        if (d != null) return new List<object>(d.Keys);
        string s = a as string;
        if (s != null)
        {
            int[] st = Letters(s);
            List<object> r = new List<object>(st.Length);
            for (int i = 0; i < st.Length; i++)
                r.Add(s.Substring(st[i], (i + 1 < st.Length ? st[i + 1] : s.Length) - st[i]));
            return r;
        }
        if (a is long)
        {
            List<object> r = new List<object>();
            for (long j = 1; j <= (long)a; j++) r.Add(j);
            return r;
        }
        Die("не можно обходити " + TypeName(a));
        return null;
    }

    // ---------- встроенныя дѣла ----------
    public static object Len(object x)
    {
        string s = x as string; if (s != null) return (long)Letters(s).Length;
        AzList l = x as AzList; if (l != null) return (long)l.Items.Count;
        AzDict d = x as AzDict; if (d != null) return (long)d.Keys.Count;
        Die("у " + TypeName(x) + " нѣсть длины");
        return null;
    }
    public static object Push(object l, object x)
    {
        AzList li = l as AzList;
        if (li == null) Die("приложити можно токмо ко свитку, а дано " + TypeName(l));
        li.Items.Add(x);
        return null;
    }
    public static object Pop(object l)
    {
        AzList li = l as AzList;
        if (li == null) Die("изъяти можно токмо изъ свитка");
        if (li.Items.Count == 0) Die("свитокъ пустъ");
        object v = li.Items[li.Items.Count - 1];
        li.Items.RemoveAt(li.Items.Count - 1);
        return v;
    }
    public static object Keys(object d)
    {
        AzDict di = d as AzDict;
        if (di == null) Die("ключи бываютъ токмо у словника");
        return new AzList(new List<object>(di.Keys));
    }
    public static object Has(object c, object x)
    {
        AzDict d = c as AzDict; if (d != null) return Bool(d.Find(x) >= 0);
        AzList l = c as AzList;
        if (l != null) { foreach (object o in l.Items) if (Eq(o, x)) return True; return False; }
        string s = c as string, xs = x as string;
        if (s != null && xs != null) return Bool(s.IndexOf(xs, StringComparison.Ordinal) >= 0);
        Die("не можно искати въ " + TypeName(c));
        return null;
    }
    public static object Range(object a, object b)
    {
        long s = 1, e;
        if (b == null) e = NeedInt(a, "конецъ");
        else { s = NeedInt(a, "начало"); e = NeedInt(b, "конецъ"); }
        List<object> r = new List<object>();
        if (s <= e) for (long j = s; j <= e; j++) r.Add(j);
        else for (long j = s; j >= e; j--) r.Add(j);
        return new AzList(r);
    }
    public static object ToInt(object x)
    {
        if (x is long) return x;
        if (x is bool) return AsLong(x);
        if (x is double) return (long)(double)x;
        string s = x as string;
        if (s != null)
        {
            long v;
            if (long.TryParse(s.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite, CultureInfo.InvariantCulture, out v)) return v;
            Die("«" + s + "» не есть число");
        }
        Die("не можно " + TypeName(x) + " обратити въ число");
        return null;
    }
    public static object ToFloat(object x)
    {
        string s = x as string;
        if (s != null)
        {
            double v;
            if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            Die("«" + s + "» не есть число");
        }
        return Num(x);
    }
    public static object Str(object x) { return ToStr(x); }
    public static object Type(object x) { return TypeName(x); }
    public static object Sqrt(object x) { return Math.Sqrt(Num(x)); }
    public static object Abs(object x) { if (x is long) return Math.Abs((long)x); return Math.Abs(Num(x)); }
    static readonly Random rng = new Random();
    public static object RandomInt(object a, object b)
    {
        long lo = NeedInt(a, "начало"), hi = NeedInt(b, "конецъ");
        if (hi < lo) { long t = lo; lo = hi; hi = t; }
        return lo + (long)(rng.NextDouble() * (hi - lo + 1));
    }
    public static object Sleep(object ms) { Flush(); System.Threading.Thread.Sleep((int)NeedInt(ms, "время")); return null; }
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    public static object Time() { return clock.ElapsedMilliseconds; }
    public static object Join(object l, object sep)
    {
        AzList li = l as AzList;
        if (li == null) Die("совокупити можно токмо свитокъ");
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < li.Items.Count; i++)
        {
            if (i > 0 && sep != null) sb.Append(ToStr(sep));
            sb.Append(ToStr(li.Items[i]));
        }
        return sb.ToString();
    }
    public static object Split(object str, object sep)
    {
        string s = str as string;
        if (s == null) Die("раздѣлити можно токмо слово");
        string sp = sep as string;
        List<object> r = new List<object>();
        if (sep == null || (sp != null && sp.Length == 0))
        {
            foreach (string p in s.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)) r.Add(p);
            return new AzList(r);
        }
        if (sp == null) Die("раздѣлитель долженъ быти словомъ");
        foreach (string p in s.Split(new string[] { sp }, StringSplitOptions.None)) r.Add(p);
        return new AzList(r);
    }

    // ---------- кириллическая цыфирь ----------
    static readonly string[] ones = { "", "а", "в", "г", "д", "є", "ѕ", "з", "и", "ѳ" };
    static readonly string[] tens = { "", "і", "к", "л", "м", "н", "ѯ", "ѻ", "п", "ч" };
    static readonly string[] hund = { "", "р", "с", "т", "ѵ", "ф", "х", "ѱ", "ѿ", "ц" };
    public static object Cyr(object n)
    {
        long x = NeedInt(n, "число");
        if (x <= 0) return ToStr(n);
        StringBuilder b = new StringBuilder();
        if (x >= 1000)
        {
            long th = x / 1000; x %= 1000;
            b.Append('҂');
            b.Append(th >= 10 ? (string)Cyr(th) : ones[th]);
        }
        List<string> parts = new List<string>();
        if (x / 100 > 0) parts.Add(hund[x / 100]);
        x %= 100;
        if (x > 10 && x < 20) { parts.Add(ones[x % 10]); parts.Add("і"); }
        else { if (x / 10 > 0) parts.Add(tens[x / 10]); if (x % 10 > 0) parts.Add(ones[x % 10]); }
        int last = parts.Count >= 2 ? parts.Count - 2 : parts.Count - 1;
        for (int i = 0; i < parts.Count; i++) { b.Append(parts[i]); if (i == last) b.Append('҃'); }
        if (parts.Count == 0) b.Append('҃');
        return b.ToString();
    }
}
