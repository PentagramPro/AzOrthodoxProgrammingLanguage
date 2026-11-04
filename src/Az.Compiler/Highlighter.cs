namespace Az.Compiler;

public enum Hl { Text, Keyword, Def, FuncName, Operator, Const, Type, Builtin, String, Number, Comment }

public readonly record struct HlSpan(int Start, int Length, Hl Kind);

/// <summary>Терпимая построчная подсвѣтка (не бросаетъ ошибокъ) для редактора.</summary>
public static class Highlighter
{
    static Hl KwClass(string kind) => kind switch
    {
        "def" => Hl.Def,
        "is" or "assign" or "not" or "and" or "or" or "gt" or "lt" or "index" => Hl.Operator,
        "decl_many" or "decl_one" => Hl.Type,
        "true" or "false" or "none" => Hl.Const,
        _ => Hl.Keyword,
    };

    public static List<HlSpan> ClassifyLine(string line)
    {
        var res = new List<HlSpan>();
        int i = 0, n = line.Length;
        string? prev = null;
        while (i < n)
        {
            char ch = line[i];
            if (ch == '#') { res.Add(new(i, n - i, Hl.Comment)); break; }
            if (ch is '«' or '"')
            {
                char close = ch == '«' ? '»' : '"';
                int j = i + 1;
                while (j < n && line[j] != close) j += line[j] == '\\' && close == '"' ? 2 : 1;
                j = Math.Min(n, j + 1);
                res.Add(new(i, j - i, Hl.String));
                i = j; prev = null;
                continue;
            }
            if (Lang.IsWordChar(ch))
            {
                int j = i;
                while (j < n && Lang.IsWordChar(line[j])) j++;
                var raw = line[i..j];
                Hl k = Hl.Text;
                string? next = null;
                if (char.IsAsciiDigit(raw[0]) || Lang.CyrNumeral(raw) != null) k = Hl.Number;
                else
                {
                    var w = Lang.Norm(raw);
                    if (Lang.Keywords.TryGetValue(w, out var kind))
                    {
                        k = KwClass(kind);
                        next = kind == "def" ? "def" : kind is "decl_many" or "decl_one" ? "decl" : null;
                    }
                    else if (prev == "def") k = Hl.FuncName;
                    else if (prev == "decl" && Lang.Types.ContainsKey(w)) k = Hl.Type;
                    else if (Lang.Builtins.ContainsKey(w)) k = Hl.Builtin;
                }
                if (k != Hl.Text) res.Add(new(i, j - i, k));
                prev = next;
                i = j;
                continue;
            }
            if (ch != ' ') prev = null;
            i++;
        }
        return res;
    }
}
