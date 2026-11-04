namespace Az.Compiler;

public abstract record Node(int Line);

// ---------- выраженія ----------
public abstract record Expr(int Line) : Node(Line);
public sealed record NumExpr(int Line, object Value) : Expr(Line);
public sealed record StrExpr(int Line, string Value) : Expr(Line);
public sealed record BoolExpr(int Line, bool Value) : Expr(Line);
public sealed record NoneExpr(int Line) : Expr(Line);
public sealed record NameExpr(int Line, string Name, string Raw, int Col) : Expr(Line);
public sealed record NegExpr(int Line, Expr A) : Expr(Line);
public sealed record NotExpr(int Line, Expr A) : Expr(Line);
public sealed record AndExpr(int Line, Expr A, Expr B) : Expr(Line);
public sealed record OrExpr(int Line, Expr A, Expr B) : Expr(Line);
public sealed record BinExpr(int Line, string Op, Expr A, Expr B) : Expr(Line);
public sealed record IndexExpr(int Line, Expr A, Expr Key) : Expr(Line);
public sealed record ListExpr(int Line, List<Expr> Items) : Expr(Line);
public sealed record DictExpr(int Line, List<(Expr Key, Expr Value)> Items) : Expr(Line);
public sealed record CallExpr(int Line, string Name, string Raw, List<Expr> Args, int Col) : Expr(Line);

// ---------- предложенія ----------
public abstract record Stmt(int Line) : Node(Line);
public sealed record ExprStmt(int Line, Expr Value) : Stmt(Line);
public sealed record PrintStmt(int Line, List<Expr> Args) : Stmt(Line);
public sealed record AssignStmt(int Line, Expr Target, Expr Value) : Stmt(Line);
public sealed record DeclStmt(int Line, List<string> Names, string Type) : Stmt(Line);
public sealed record IfStmt(int Line, List<(Expr Cond, List<Stmt> Body)> Branches, List<Stmt>? Else) : Stmt(Line);
public sealed record WhileStmt(int Line, Expr Cond, List<Stmt> Body) : Stmt(Line);
public sealed record ForStmt(int Line, string Var, Expr Iter, List<Stmt> Body) : Stmt(Line);
public sealed record BreakStmt(int Line) : Stmt(Line);
public sealed record ContinueStmt(int Line) : Stmt(Line);
public sealed record ReturnStmt(int Line, Expr? Value) : Stmt(Line);
public sealed record AnathemaStmt(int Line, Expr Value) : Stmt(Line);
public sealed record Param(string Name, Expr? Default);
public sealed record FuncDef(int Line, string Name, string Raw, List<Param> Params, List<Stmt> Body) : Stmt(Line);
