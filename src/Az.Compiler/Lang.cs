using System.Globalization;
using System.Text;

namespace Az.Compiler;

/// <summary>Прегрѣшеніе: ошибка компиляции съ мѣстомъ въ исходникѣ.</summary>
public sealed class AzError(string msg, int line = 0, int col = 0) : Exception(msg)
{
    public int Line { get; } = line;
    public int Col { get; } = col;
}

/// <summary>Словарь языка: нормализація словъ, ключевыя слова, виды, встроенныя дѣла.</summary>
public static class Lang
{
    /// <summary>Версія изъ сборки (Directory.Build.props или -p:Version=… изъ тега релиза).</summary>
    public static readonly string Version =
        (typeof(Lang).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion.Split('+')[0] ?? "2.0";
    public const char Titlo = '҃';
    public const char Thousands = '҂';

    static readonly Dictionary<char, string> LetterMap = new()
    {
        ['ѣ'] = "е", ['є'] = "е", ['ѥ'] = "е", ['ё'] = "е", ['і'] = "и", ['ї'] = "и", ['ѵ'] = "и", ['ѷ'] = "и",
        ['ꙇ'] = "и", ['й'] = "и", ['ѳ'] = "ф", ['ѿ'] = "от", ['ѡ'] = "о", ['ѻ'] = "о", ['ꙍ'] = "о", ['ѽ'] = "о",
        ['ꚙ'] = "о", ['ꙩ'] = "о", ['ꙫ'] = "о", ['ꙭ'] = "о", ['ꙋ'] = "у", ['ѹ'] = "у", ['ѫ'] = "у", ['ѭ'] = "ю",
        ['ѕ'] = "з", ['ꙃ'] = "з", ['ꙁ'] = "з", ['ꙗ'] = "я", ['ѧ'] = "я", ['ѩ'] = "я", ['ѯ'] = "кс", ['ѱ'] = "пс",
        ['ꙑ'] = "ы", ['ꙟ'] = "я",
    };

    public static bool IsMark(char ch)
    {
        var c = CharUnicodeInfo.GetUnicodeCategory(ch);
        return c is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark;
    }

    static bool IsCyr(char ch) => ch is >= 'Ѐ' and <= 'ԯ' or >= 'Ꙁ' and <= 'ꚟ';

    /// <summary>Приводитъ слово къ простому виду: безъ ударѣній, титлъ и древнихъ буквъ.</summary>
    public static string Norm(string word)
    {
        var w = word.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var sb = new StringBuilder(w.Length);
        foreach (var ch in w)
            if (!IsMark(ch) && ch != Thousands) sb.Append(ch);
        w = sb.ToString();
        bool cyr = w.Any(IsCyr);
        sb.Clear();
        foreach (var ch0 in w)
        {
            var ch = cyr && ch0 == 'i' ? 'и' : ch0;
            if (LetterMap.TryGetValue(ch, out var rep)) sb.Append(rep); else sb.Append(ch);
        }
        w = sb.ToString().Replace("оу", "у");
        if (w.Length > 1 && w.EndsWith('ъ')) w = w[..^1];
        return w;
    }

    static readonly Dictionary<char, int> Numerals = new()
    {
        ['а'] = 1, ['в'] = 2, ['г'] = 3, ['д'] = 4, ['є'] = 5, ['е'] = 5, ['ѕ'] = 6, ['з'] = 7, ['ꙁ'] = 7, ['и'] = 8,
        ['ѳ'] = 9, ['і'] = 10, ['i'] = 10, ['к'] = 20, ['л'] = 30, ['м'] = 40, ['н'] = 50, ['ѯ'] = 60, ['ѻ'] = 70,
        ['о'] = 70, ['п'] = 80, ['ч'] = 90, ['ҁ'] = 90, ['р'] = 100, ['с'] = 200, ['т'] = 300, ['у'] = 400,
        ['ѵ'] = 400, ['ꙋ'] = 400, ['ф'] = 500, ['х'] = 600, ['ѱ'] = 700, ['ѿ'] = 800, ['ѡ'] = 800, ['ц'] = 900,
    };

    /// <summary>Кириллическое число подъ титломъ (а҃ = 1, ҂вк҃ѕ = 2026) или null.</summary>
    public static long? CyrNumeral(string word)
    {
        if (!word.Contains(Titlo)) return null;
        long total = 0, mult = 1;
        bool seen = false;
        foreach (var ch in word.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (ch == Thousands) { mult = 1000; continue; }
            if (IsMark(ch)) continue;
            if (!Numerals.TryGetValue(ch, out var v)) return null;
            total += v * mult;
            mult = 1;
            seen = true;
        }
        return seen ? total : null;
    }

    public static bool IsWordChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == Thousands || IsMark(ch);

    // ---------- ключевыя слова ----------
    public static readonly IReadOnlyDictionary<string, string> Keywords = Build(new()
    {
        ["def"] = "функция деяние дело",
        ["return"] = "возврати возвратити верни",
        ["if"] = "аще",
        ["else"] = "иначе инако",
        ["while"] = "покуда доколе",
        ["for"] = "для всяко всяк",
        ["in"] = "во",
        ["break"] = "прерви довольно",
        ["continue"] = "продолжи паки",
        ["print"] = "глаголи рцы рци",
        ["is"] = "есть",
        ["assign"] = "бысть буди",
        ["decl_many"] = "суть",
        ["decl_one"] = "же",
        ["not"] = "не",
        ["and"] = "и",
        ["or"] = "або или либо",
        ["gt"] = "вяще вящше больше более болше",
        ["lt"] = "меньше менее мнее менше",
        ["true"] = "истина правда аминь",
        ["false"] = "ложь кривда",
        ["none"] = "безвестно безвестна безвестный безвестен безвестное ничтоже",
        ["index"] = "от",
        ["anathema"] = "анафема",
    });

    public static readonly IReadOnlyDictionary<string, string> Types = Build(new()
    {
        ["int"] = "целое целыя целый целые целая целых цело",
        ["flt"] = "дробное дробныя дробный дробные дробь",
        ["str"] = "слово словеса строка строки глагол",
        ["dict"] = "словник словники словарь",
        ["list"] = "свиток свитки список списки",
        ["bool"] = "истинность",
    });

    public sealed record Builtin(string Method, int Min, int Max);

    public static readonly IReadOnlyDictionary<string, Builtin> Builtins = BuildBuiltins(
        ("длина мера сочти сочтати", "Len", 1, 1),
        ("приложи", "Push", 2, 2),
        ("изъми изыми", "Pop", 1, 1),
        ("ключи", "Keys", 1, 1),
        ("имать содержит", "Has", 2, 2),
        ("чреда", "Range", 1, 2),
        ("число", "ToInt", 1, 1),
        ("дробь", "ToFloat", 1, 1),
        ("слово", "Str", 1, 1),
        ("вид", "Type", 1, 1),
        ("корень", "Sqrt", 1, 1),
        ("мерило", "Abs", 1, 1),
        ("жребий", "RandomInt", 2, 2),
        ("почий", "Sleep", 1, 1),
        ("время", "Time", 0, 0),
        ("совокупи", "Join", 1, 2),
        ("раздели", "Split", 1, 2),
        ("внемли", "Input", 0, 1),
        ("кириллицею цифирью", "Cyr", 1, 1));

    static Dictionary<string, string> Build(Dictionary<string, string> src)
    {
        var d = new Dictionary<string, string>();
        foreach (var (kind, words) in src)
            foreach (var w in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                d[Norm(w)] = kind;
        return d;
    }

    static Dictionary<string, Builtin> BuildBuiltins(params (string names, string method, int min, int max)[] items)
    {
        var d = new Dictionary<string, Builtin>();
        foreach (var (names, method, min, max) in items)
            foreach (var n in names.Split(' '))
                d[Norm(n)] = new Builtin(method, min, max);
        return d;
    }
}
