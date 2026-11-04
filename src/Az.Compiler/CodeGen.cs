using System.Globalization;
using System.Text;

namespace Az.Compiler;

/// <summary>Порождаетъ C#-кодъ (подмножество C# 5) изъ дерева программы.</summary>
public sealed class CodeGen
{
    readonly Dictionary<string, FuncDef> funcs = [];
    HashSet<string> globals = [];
    HashSet<string>? pars, locals;
    readonly StringBuilder sb = new();
    int tmp, loop;
    bool inFunc;
    readonly string? linePath;

    CodeGen(string? linePath) { this.linePath = linePath; }

    /// <param name="linePath">путь къ .азъ для директивъ #line (для будущаго отладчика); null — безъ нихъ.</param>
    public static string Generate(List<Stmt> prog, string srcName, string? linePath = null) =>
        new CodeGen(linePath).Run(prog, srcName);

    // ---------- имена ----------
    static string Ident(string name)
    {
        var b = new StringBuilder();
        foreach (var ch in name)
        {
            var c = CharUnicodeInfo.GetUnicodeCategory(ch);
            bool ok = char.IsLetterOrDigit(ch) || c is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
            if (ok) b.Append(ch);
            else if (ch == '_') b.Append("__");
            else b.Append($"_{(int)ch:x}_");
        }
        return b.ToString();
    }
    static string FuncName(string n) => "Ф_" + Ident(n);
    static string GlobalName(string n) => "Г_" + Ident(n);
    static string LocalName(string n) => "@" + Ident(n);

    string Var(string name, Node node)
    {
        string raw = node switch { NameExpr ne => ne.Raw, _ => name };
        int col = node is NameExpr ne2 ? ne2.Col : 0;
        if (pars != null && (pars.Contains(name) || locals!.Contains(name))) return LocalName(name);
        if (globals.Contains(name)) return GlobalName(name);
        if (funcs.ContainsKey(name) || Lang.Builtins.ContainsKey(name))
            throw new AzError($"«{raw}» есть функція — призови её со скобками", node.Line, col);
        throw new AzError($"имя «{raw}» не вѣдомо (не объявлено и не присвоено)", node.Line, col);
    }

    // ---------- литералы ----------
    static string CsString(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (ch < 32 || char.IsSurrogate(ch)) b.Append($"\\u{(int)ch:x4}");
                    else b.Append(ch);
                    break;
            }
        }
        return b.Append('"').ToString();
    }

    static string NumLit(object v) => v switch
    {
        long l => $"(object){l.ToString(CultureInfo.InvariantCulture)}L",
        double d => $"(object){d.ToString("R", CultureInfo.InvariantCulture)}D",
        _ => throw new InvalidOperationException(),
    };

    static readonly Dictionary<string, string> BinOps = new()
    {
        ["+"] = "Add", ["-"] = "Sub", ["*"] = "Mul", ["/"] = "Div", ["//"] = "IDiv", ["%"] = "Mod",
        ["=="] = "OpEq", ["!="] = "OpNe", ["<"] = "OpLt", [">"] = "OpGt", ["<="] = "OpLe", [">="] = "OpGe",
    };

    string E(Expr e) => e switch
    {
        NumExpr n => NumLit(n.Value),
        StrExpr s => "(object)" + CsString(s.Value),
        BoolExpr b => b.Value ? "Az.True" : "Az.False",
        NoneExpr => "Az.Nil",
        NameExpr n => Var(n.Name, n),
        NegExpr n => $"Az.Neg({E(n.A)})",
        NotExpr n => $"Az.Not({E(n.A)})",
        AndExpr a => $"Az.Bool(Az.Truthy({E(a.A)}) && Az.Truthy({E(a.B)}))",
        OrExpr o => $"Az.Bool(Az.Truthy({E(o.A)}) || Az.Truthy({E(o.B)}))",
        BinExpr b => $"Az.{BinOps[b.Op]}({E(b.A)}, {E(b.B)})",
        IndexExpr i => $"Az.Index({E(i.A)}, {E(i.Key)})",
        ListExpr l => l.Items.Count == 0 ? "Az.List()" : $"Az.List({string.Join(", ", l.Items.Select(E))})",
        DictExpr d => d.Items.Count == 0 ? "Az.Dict()"
            : $"Az.Dict({string.Join(", ", d.Items.Select(kv => E(kv.Key) + ", " + E(kv.Value)))})",
        CallExpr c => Call(c),
        _ => throw new AzError("внутренняя ошибка: " + e.GetType().Name, e.Line),
    };

    string Call(CallExpr c)
    {
        if (funcs.TryGetValue(c.Name, out var fd))
        {
            if (c.Args.Count > fd.Params.Count)
                throw new AzError($"функція «{fd.Raw}» пріемлетъ не болѣе {fd.Params.Count} доводовъ", c.Line, c.Col);
            var args = c.Args.Select(E).ToList();
            foreach (var prm in fd.Params.Skip(c.Args.Count))
            {
                if (prm.Default == null) throw new AzError($"функціи «{fd.Raw}» недостаетъ довода «{prm.Name}»", c.Line, c.Col);
                args.Add(E(prm.Default));
            }
            return $"{FuncName(c.Name)}({string.Join(", ", args)})";
        }
        if (Lang.Builtins.TryGetValue(c.Name, out var bi))
        {
            if (c.Args.Count < bi.Min || c.Args.Count > bi.Max)
                throw new AzError($"«{c.Raw}» пріемлетъ ѿ {bi.Min} до {bi.Max} доводовъ, а дано {c.Args.Count}", c.Line, c.Col);
            var args = c.Args.Select(E).Concat(Enumerable.Repeat("Az.Nil", bi.Max - c.Args.Count));
            return $"Az.{bi.Method}({string.Join(", ", args)})";
        }
        throw new AzError($"функція «{c.Raw}» не вѣдома", c.Line, c.Col);
    }

    // ---------- предложенія ----------
    void Emit(string s, int ind) => sb.Append(' ', ind * 4).Append(s).Append('\n');

    void Stmts(List<Stmt> body, int ind)
    {
        foreach (var st in body) Stmt(st, ind);
    }

    void Stmt(Stmt st, int ind)
    {
        if (linePath != null) sb.Append($"#line {st.Line} {CsString(linePath)}\n");
        Emit($"Az.Line = {st.Line};", ind);
        switch (st)
        {
            case ExprStmt e:
                Emit($"Az.Discard({E(e.Value)});", ind);
                break;
            case PrintStmt pr:
                Emit($"Az.Print({string.Join(", ", pr.Args.Select(E))});", ind);
                break;
            case AssignStmt a when a.Target is NameExpr n:
                Emit($"{Var(n.Name, n)} = {E(a.Value)};", ind);
                break;
            case AssignStmt a when a.Target is IndexExpr ix:
                Emit($"Az.SetIndex({E(ix.A)}, {E(ix.Key)}, {E(a.Value)});", ind);
                break;
            case DeclStmt d:
                var init = d.Type switch
                {
                    "int" => "(object)0L", "flt" => "(object)0.0D", "str" => "(object)\"\"",
                    "dict" => "Az.Dict()", "list" => "Az.List()", _ => "Az.False",
                };
                foreach (var nm in d.Names) Emit($"{Var(nm, d)} = {init};", ind);
                break;
            case IfStmt i:
                for (int k = 0; k < i.Branches.Count; k++)
                {
                    var (cond, body) = i.Branches[k];
                    Emit($"{(k == 0 ? "if" : "else if")} (Az.Truthy({E(cond)})) {{", ind);
                    Stmts(body, ind + 1);
                    Emit("}", ind);
                }
                if (i.Else != null)
                {
                    Emit("else {", ind);
                    Stmts(i.Else, ind + 1);
                    Emit("}", ind);
                }
                break;
            case WhileStmt w:
                Emit($"while (Az.Truthy({E(w.Cond)})) {{", ind);
                loop++;
                Stmts(w.Body, ind + 1);
                loop--;
                Emit($"Az.Line = {w.Line};", ind + 1);
                Emit("}", ind);
                break;
            case ForStmt f:
            {
                int t = ++tmp;
                var target = Var(f.Var, f);
                Emit("{", ind);
                Emit($"System.Collections.Generic.List<object> __обх{t} = Az.Iter({E(f.Iter)});", ind + 1);
                Emit($"for (int __и{t} = 0; __и{t} < __обх{t}.Count; __и{t}++) {{", ind + 1);
                Emit($"{target} = __обх{t}[__и{t}];", ind + 2);
                loop++;
                Stmts(f.Body, ind + 2);
                loop--;
                Emit("}", ind + 1);
                Emit("}", ind);
                break;
            }
            case BreakStmt or ContinueStmt:
                if (loop == 0) throw new AzError($"«{(st is BreakStmt ? "прерви" : "продолжи")}» внѣ цикла", st.Line);
                Emit(st is BreakStmt ? "break;" : "continue;", ind);
                break;
            case ReturnStmt r:
                var v = r.Value != null ? E(r.Value) : "Az.Nil";
                Emit(inFunc ? $"return {v};" : $"Az.ExitWith({v});", ind);
                break;
            case AnathemaStmt an:
                Emit($"Az.Anathema({E(an.Value)});", ind);
                break;
            case FuncDef:
                throw new AzError("функцію подобаетъ опредѣлять токмо въ главномъ тѣлѣ", st.Line);
            default:
                throw new AzError("внутренняя ошибка: " + st.GetType().Name, st.Line);
        }
    }

    static HashSet<string> Assigned(List<Stmt> body, HashSet<string> acc)
    {
        foreach (var st in body)
        {
            switch (st)
            {
                case AssignStmt { Target: NameExpr n }: acc.Add(n.Name); break;
                case DeclStmt d: acc.UnionWith(d.Names); break;
                case ForStmt f: acc.Add(f.Var); Assigned(f.Body, acc); break;
                case WhileStmt w: Assigned(w.Body, acc); break;
                case IfStmt i:
                    foreach (var (_, b) in i.Branches) Assigned(b, acc);
                    if (i.Else != null) Assigned(i.Else, acc);
                    break;
            }
        }
        return acc;
    }

    string Run(List<Stmt> prog, string srcName)
    {
        foreach (var fd in prog.OfType<FuncDef>())
        {
            if (!funcs.TryAdd(fd.Name, fd)) throw new AzError($"функція «{fd.Raw}» уже опредѣлена", fd.Line);
        }
        var main = prog.Where(s => s is not FuncDef).ToList();
        globals = Assigned(main, []);

        var head = new StringBuilder();
        head.Append($"// Порождено трансляторомъ «Азъ» {Lang.Version} изъ {srcName}. Не правити рукою.\n");
        head.Append("// Рантаймъ (классы Az, AzList, AzDict) лежитъ въ сосѣднемъ файлѣ AzRuntime.cs.\n\n");
        head.Append("public static class Программа\n{\n");
        foreach (var g in globals.Order(StringComparer.Ordinal)) head.Append($"    static object {GlobalName(g)};\n");
        head.Append('\n');

        foreach (var (name, fd) in funcs)
        {
            pars = fd.Params.Select(p => p.Name).ToHashSet();
            locals = Assigned(fd.Body, []);
            locals.ExceptWith(pars);
            locals.ExceptWith(globals);
            inFunc = true;
            sb.Clear();
            Stmts(fd.Body, 2);
            var sig = string.Join(", ", fd.Params.Select(p => "object " + LocalName(p.Name)));
            head.Append($"    // {fd.Raw}\n");
            head.Append($"    static object {FuncName(name)}({sig})\n    {{\n");
            head.Append("        Az.Enter();\n");
            foreach (var l in locals.Order(StringComparer.Ordinal)) head.Append($"        object {LocalName(l)} = null;\n");
            head.Append(sb);
            if (linePath != null) head.Append("#line default\n");
            head.Append("        return null;\n    }\n\n");
        }

        pars = locals = null;
        inFunc = false;
        sb.Clear();
        Stmts(main, 2);
        head.Append("    static void Тѣло()\n    {\n").Append(sb);
        if (linePath != null) head.Append("#line default\n");
        head.Append("    }\n\n");
        head.Append("    public static int Main()\n    {\n        return Az.Run(Тѣло);\n    }\n}\n");
        return head.ToString();
    }
}
