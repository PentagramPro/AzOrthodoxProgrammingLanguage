namespace Az.Compiler;

public sealed class Parser(List<Token> toks)
{
    int p;

    Token Cur => toks[p];
    Token At(int k) => toks[Math.Min(k, toks.Count - 1)];

    bool Is(T kind, string? val = null) => Is(Cur, kind, val);
    static bool Is(Token t, T kind, string? val = null) => t.Kind == kind && (val == null || t.S == val);

    static string Describe(Token t) => t.Raw.Length > 0 ? t.Raw : t.Kind switch
    {
        T.Newline => "конецъ строки",
        T.Eof => "конецъ файла",
        T.Indent => "отступъ",
        T.Dedent => "конецъ отступа",
        _ => t.Kind.ToString(),
    };

    Token Eat(T kind, string? val = null, string? what = null)
    {
        if (!Is(kind, val))
        {
            var want = what ?? (val != null ? $"«{val}»" : kind.ToString());
            throw new AzError($"ожидалось {want}, а обрѣтено «{Describe(Cur)}»", Cur.Line, Cur.Col);
        }
        return toks[p++];
    }

    Token? Accept(T kind, string? val = null) => Is(kind, val) ? Eat(kind, val) : null;

    public static List<Stmt> Parse(string src) => new Parser(Lexer.Lex(src)).Program();

    public List<Stmt> Program()
    {
        var body = new List<Stmt>();
        while (!Is(T.Eof)) body.Add(Statement());
        return body;
    }

    List<Stmt> Block()
    {
        Eat(T.Op, ":", "«:»");
        if (!Is(T.Newline))
        {
            var st = SimpleStatement();
            Eat(T.Newline, what: "конецъ строки");
            return [st];
        }
        Eat(T.Newline);
        if (!Is(T.Indent)) throw new AzError("ожидался отступъ (тѣло блока)", Cur.Line, Cur.Col);
        Eat(T.Indent);
        var body = new List<Stmt>();
        while (!Is(T.Dedent) && !Is(T.Eof)) body.Add(Statement());
        Accept(T.Dedent);
        return body;
    }

    Stmt Statement()
    {
        var c = Cur;
        if (Is(T.Kw, "def")) return FuncDef();
        if (Is(T.Kw, "if")) return IfStmt();
        if (Is(T.Kw, "while"))
        {
            Eat(T.Kw);
            var cond = Expr();
            return new WhileStmt(c.Line, cond, Block());
        }
        if (Is(T.Kw, "for"))
        {
            Eat(T.Kw);
            var v = Eat(T.Name, what: "имя");
            Eat(T.Kw, "in", "«во»");
            var it = Expr();
            return new ForStmt(c.Line, v.S, it, Block());
        }
        if (Is(T.Kw, "else")) throw new AzError("«иначе» безъ «аще»", c.Line, c.Col);
        var st = SimpleStatement();
        Eat(T.Newline, what: "конецъ строки");
        return st;
    }

    Stmt FuncDef()
    {
        var c = Eat(T.Kw, "def");
        var name = Eat(T.Name, what: "имя функціи");
        Eat(T.Op, "(", "«(»");
        var ps = new List<Param>();
        while (!Is(T.Op, ")"))
        {
            var pn = Eat(T.Name, what: "имя довода");
            Expr? def = null;
            if (Accept(T.Kw, "is") != null || Accept(T.Op, "=") != null)
            {
                def = Expr();
                bool simple = def is NumExpr or StrExpr or BoolExpr or NoneExpr || def is NegExpr { A: NumExpr };
                if (!simple) throw new AzError("по умолчанію допустимы токмо простыя значенія", pn.Line, pn.Col);
            }
            ps.Add(new Param(pn.S, def));
            if (Accept(T.Op, ",") == null) break;
        }
        Eat(T.Op, ")", "«)»");
        return new FuncDef(c.Line, name.S, name.Raw, ps, Block());
    }

    Stmt IfStmt()
    {
        var c = Eat(T.Kw, "if");
        var cond = Expr();
        var branches = new List<(Expr, List<Stmt>)> { (cond, Block()) };
        List<Stmt>? orelse = null;
        while (Is(T.Kw, "else"))
        {
            Eat(T.Kw);
            if (Accept(T.Kw, "if") != null)
            {
                var c2 = Expr();
                branches.Add((c2, Block()));
            }
            else { orelse = Block(); break; }
        }
        return new IfStmt(c.Line, branches, orelse);
    }

    Stmt SimpleStatement()
    {
        var c = Cur;
        if (Is(T.Kw, "return"))
        {
            Eat(T.Kw);
            return new ReturnStmt(c.Line, Is(T.Newline) ? null : Expr());
        }
        if (Is(T.Kw, "break")) { Eat(T.Kw); return new BreakStmt(c.Line); }
        if (Is(T.Kw, "continue")) { Eat(T.Kw); return new ContinueStmt(c.Line); }
        if (Is(T.Kw, "print"))
        {
            Eat(T.Kw);
            var args = new List<Expr>();
            if (!Is(T.Newline))
            {
                args.Add(Expr());
                while (Accept(T.Op, ",") != null) args.Add(Expr());
            }
            return new PrintStmt(c.Line, args);
        }
        if (Is(T.Kw, "anathema")) { Eat(T.Kw); return new AnathemaStmt(c.Line, Expr()); }

        // объявленіе:  а, б, в суть цѣлыя   /   славословъ же словникъ
        if (Is(T.Name))
        {
            int k = p;
            var names = new List<Token>();
            while (toks[k].Kind == T.Name)
            {
                names.Add(toks[k]);
                if (Is(At(k + 1), T.Op, ",")) k += 2;
                else { k++; break; }
            }
            if (toks[k].Kind == T.Kw && toks[k].S is "decl_many" or "decl_one")
            {
                p = k + 1;
                var tt = Eat(T.Name, what: "имя вида (цѣлое, слово, словникъ…)");
                if (!Lang.Types.TryGetValue(tt.S, out var typ))
                    throw new AzError($"невѣдомый видъ «{tt.Raw}»", tt.Line, tt.Col);
                return new DeclStmt(c.Line, names.Select(n => n.S).ToList(), typ);
            }
        }

        // присвоеніе:  цѣль есть выраженіе
        int save = p;
        Expr? target = null;
        try { target = Postfix(); } catch (AzError) { }
        if (target is NameExpr or IndexExpr &&
            (Is(T.Kw, "is") || Is(T.Kw, "assign") || Is(T.Op, "=") || Is(T.Op, "+=") || Is(T.Op, "-=")))
        {
            var op = toks[p++].S;
            var val = Expr();
            if (op is "+=" or "-=") val = new BinExpr(c.Line, op == "+=" ? "+" : "-", target!, val);
            return new AssignStmt(c.Line, target!, val);
        }
        p = save;
        return new ExprStmt(c.Line, Expr());
    }

    // ---------- выраженія ----------
    public Expr Expr()
    {
        var a = AndExpr();
        while (Is(T.Kw, "or")) { var t = Eat(T.Kw); a = new OrExpr(t.Line, a, AndExpr()); }
        return a;
    }

    Expr AndExpr()
    {
        var a = NotExpr();
        while (Is(T.Kw, "and")) { var t = Eat(T.Kw); a = new AndExpr(t.Line, a, NotExpr()); }
        return a;
    }

    Expr NotExpr()
    {
        if (Is(T.Kw, "not")) { var t = Eat(T.Kw); return new NotExpr(t.Line, NotExpr()); }
        return Comparison();
    }

    Expr Comparison()
    {
        var a = Arith();
        var t = Cur;
        // словесное сравненіе:  [не] [есть] [не] [вяще|менѣе]
        int nots = 0, k = p;
        bool hasIs = false;
        string? cmp = null;
        while (true)
        {
            var tk = toks[k];
            if (Is(tk, T.Kw, "not")) nots++;
            else if (Is(tk, T.Kw, "is") && !hasIs) hasIs = true;
            else if (Is(tk, T.Kw, "gt") || Is(tk, T.Kw, "lt")) { cmp = tk.S; k++; break; }
            else break;
            k++;
        }
        if (cmp != null || hasIs)
        {
            p = k;
            var b = Arith();
            bool neg = nots % 2 == 1;
            string op = cmp switch
            {
                "gt" => neg ? "<=" : ">",
                "lt" => neg ? ">=" : "<",
                _ => neg ? "!=" : "==",
            };
            return new BinExpr(t.Line, op, a, b);
        }
        if (nots > 0) throw new AzError("«не» не на мѣстѣ", t.Line, t.Col);
        if (Is(T.Op) && Cur.S is "==" or "!=" or "<" or ">" or "<=" or ">=")
        {
            var op = Eat(T.Op).S;
            return new BinExpr(t.Line, op, a, Arith());
        }
        return a;
    }

    Expr Arith()
    {
        var a = Term();
        while (Is(T.Op) && Cur.S is "+" or "-") { var t = Eat(T.Op); a = new BinExpr(t.Line, t.S, a, Term()); }
        return a;
    }

    Expr Term()
    {
        var a = Unary();
        while (Is(T.Op) && Cur.S is "*" or "/" or "//" or "%") { var t = Eat(T.Op); a = new BinExpr(t.Line, t.S, a, Unary()); }
        return a;
    }

    Expr Unary()
    {
        if (Is(T.Op, "-")) { var t = Eat(T.Op); return new NegExpr(t.Line, Unary()); }
        if (Is(T.Op, "+")) { Eat(T.Op); return Unary(); }
        return Postfix();
    }

    List<Expr> Args()
    {
        Eat(T.Op, "(");
        var args = new List<Expr>();
        while (!Is(T.Op, ")"))
        {
            args.Add(Expr());
            if (Accept(T.Op, ",") == null) break;
        }
        Eat(T.Op, ")", "«)»");
        return args;
    }

    Expr Postfix()
    {
        var a = Primary();
        while (true)
        {
            var t = Cur;
            if (Is(T.Op, "(") && a is NameExpr n)
                a = new CallExpr(t.Line, n.Name, n.Raw, Args(), n.Col);
            else if (Is(T.Op, "["))
            {
                Eat(T.Op);
                var k = Expr();
                Eat(T.Op, "]", "«]»");
                a = new IndexExpr(t.Line, a, k);
            }
            else if (Is(T.Kw, "index")) // ѿ
            {
                Eat(T.Kw);
                var k = Primary();
                if (Is(T.Op, "(") && k is NameExpr kn) k = new CallExpr(kn.Line, kn.Name, kn.Raw, Args(), kn.Col);
                a = new IndexExpr(t.Line, a, k);
            }
            else if (Is(T.Op, "."))
            {
                Eat(T.Op);
                var m = Eat(T.Name, what: "имя дѣйства");
                a = new CallExpr(t.Line, m.S, m.Raw, [a, .. Args()], m.Col);
            }
            else return a;
        }
    }

    Expr Primary()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case T.Num: p++; return new NumExpr(t.Line, t.Val!);
            case T.Str: p++; return new StrExpr(t.Line, t.S);
            case T.Kw when t.S is "true" or "false": p++; return new BoolExpr(t.Line, t.S == "true");
            case T.Kw when t.S == "none": p++; return new NoneExpr(t.Line);
            case T.Name: p++; return new NameExpr(t.Line, t.S, t.Raw, t.Col);
        }
        if (Is(T.Op, "("))
        {
            Eat(T.Op);
            var e = Expr();
            Eat(T.Op, ")", "«)»");
            return e;
        }
        if (Is(T.Op, "["))
        {
            Eat(T.Op);
            var items = new List<Expr>();
            while (!Is(T.Op, "]"))
            {
                items.Add(Expr());
                if (Accept(T.Op, ",") == null) break;
            }
            Eat(T.Op, "]", "«]»");
            return new ListExpr(t.Line, items);
        }
        if (Is(T.Op, "{"))
        {
            Eat(T.Op);
            var items = new List<(Expr, Expr)>();
            while (!Is(T.Op, "}"))
            {
                var k = Expr();
                Eat(T.Op, ":", "«:»");
                items.Add((k, Expr()));
                if (Accept(T.Op, ",") == null) break;
            }
            Eat(T.Op, "}", "«}»");
            return new DictExpr(t.Line, items);
        }
        throw new AzError($"ожидалось выраженіе, а обрѣтено «{Describe(t)}»", t.Line, t.Col);
    }
}
