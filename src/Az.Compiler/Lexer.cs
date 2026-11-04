using System.Globalization;
using System.Text;

namespace Az.Compiler;

public enum T { Indent, Dedent, Newline, Eof, Num, Str, Name, Kw, Op }

public sealed record Token(T Kind, object? Val, string Raw, int Line, int Col)
{
    public string S => Val as string ?? "";
    public override string ToString() => $"{Kind}:{Val}@{Line}";
}

public static class Lexer
{
    static readonly string[] Ops =
        ["//", "==", "!=", "<=", ">=", "+=", "-=", "<", ">", "=", "+", "-", "*", "/", "%", "(", ")", "[", "]", "{", "}", ",", ":", "."];

    public static List<Token> Lex(string src)
    {
        src = src.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
        var toks = new List<Token>();
        var indents = new List<int> { 0 };
        int depth = 0;
        var lines = src.Split('\n');
        for (int ln = 1; ln <= lines.Length; ln++)
        {
            var line = lines[ln - 1];
            int i = 0, n = line.Length;
            if (depth == 0)
            {
                var stripped = line.Trim();
                if (stripped.Length == 0 || stripped.StartsWith('#')) continue;
                int ind = line.Length - line.TrimStart(' ').Length;
                if (ind > indents[^1])
                {
                    indents.Add(ind);
                    toks.Add(new Token(T.Indent, null, "", ln, 1));
                }
                while (ind < indents[^1])
                {
                    indents.RemoveAt(indents.Count - 1);
                    toks.Add(new Token(T.Dedent, null, "", ln, 1));
                }
                if (ind != indents[^1]) throw new AzError("отступъ не согласуется ни съ единымъ прежнимъ", ln, ind + 1);
                i = ind;
            }
            while (i < n)
            {
                char ch = line[i];
                int col = i + 1;
                if (ch == ' ') { i++; continue; }
                if (ch == '#') break;
                if (ch is '«' or '"')
                {
                    char close = ch == '«' ? '»' : '"';
                    int j = i + 1;
                    var buf = new StringBuilder();
                    while (j < n && line[j] != close)
                    {
                        if (line[j] == '\\' && j + 1 < n)
                        {
                            char e = line[j + 1];
                            buf.Append(e switch
                            {
                                'n' => "\n", 't' => "\t", '\\' => "\\", '"' => "\"", '»' => "»", '«' => "«",
                                _ => "\\" + e,
                            });
                            j += 2;
                            continue;
                        }
                        buf.Append(line[j]);
                        j++;
                    }
                    if (j >= n) throw new AzError("слово (строка) не закрыто", ln, col);
                    toks.Add(new Token(T.Str, buf.ToString(), line[i..(j + 1)], ln, col));
                    i = j + 1;
                    continue;
                }
                if (ch is >= '0' and <= '9')
                {
                    int j = i;
                    while (j < n && char.IsAsciiDigit(line[j])) j++;
                    bool isFloat = j + 1 < n && line[j] == '.' && char.IsAsciiDigit(line[j + 1]);
                    if (isFloat) { j++; while (j < n && char.IsAsciiDigit(line[j])) j++; }
                    var s = line[i..j];
                    object val = isFloat ? (object)double.Parse(s, CultureInfo.InvariantCulture) : long.Parse(s, CultureInfo.InvariantCulture);
                    toks.Add(new Token(T.Num, val, s, ln, col));
                    i = j;
                    continue;
                }
                if (Lang.IsWordChar(ch))
                {
                    int j = i;
                    while (j < n && Lang.IsWordChar(line[j])) j++;
                    var raw = line[i..j];
                    var num = Lang.CyrNumeral(raw);
                    if (num is long v) toks.Add(new Token(T.Num, v, raw, ln, col));
                    else
                    {
                        var w = Lang.Norm(raw);
                        toks.Add(Lang.Keywords.TryGetValue(w, out var kw)
                            ? new Token(T.Kw, kw, raw, ln, col)
                            : new Token(T.Name, w, raw, ln, col));
                    }
                    i = j;
                    continue;
                }
                var op = Ops.FirstOrDefault(o => string.CompareOrdinal(line, i, o, 0, o.Length) == 0);
                if (op == null) throw new AzError($"невѣдомый знакъ «{ch}»", ln, col);
                if (op is "(" or "[" or "{") depth++;
                else if (op is ")" or "]" or "}") depth = Math.Max(0, depth - 1);
                toks.Add(new Token(T.Op, op, op, ln, col));
                i += op.Length;
            }
            if (depth == 0 && toks.Count > 0 && toks[^1].Kind is not (T.Newline or T.Indent or T.Dedent))
                toks.Add(new Token(T.Newline, null, "", ln, n + 1));
        }
        int last = lines.Length;
        if (toks.Count == 0 || toks[^1].Kind != T.Newline) toks.Add(new Token(T.Newline, null, "", last, 1));
        while (indents.Count > 1)
        {
            indents.RemoveAt(indents.Count - 1);
            toks.Add(new Token(T.Dedent, null, "", last, 1));
        }
        toks.Add(new Token(T.Eof, null, "", last + 1, 1));
        return toks;
    }
}
