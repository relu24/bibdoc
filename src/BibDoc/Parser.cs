using System.Globalization;
using System.Text;

namespace BibDoc;

// ── Expressions ──────────────────────────────────────────────────────────────

public abstract record Expr;
public sealed record Lit(object? Value) : Expr;
public sealed record Tmpl(IReadOnlyList<object> Parts) : Expr;            // string or Expr
public sealed record NameRef(string Name) : Expr;
public sealed record ArrayLit(IReadOnlyList<(bool Spread, Expr Value)> Items) : Expr;
public sealed record ObjectLit(IReadOnlyList<(string? Key, Expr Value)> Pairs) : Expr;   // null key: ...spread
public sealed record Lambda(IReadOnlyList<string> Params, Expr? Body, IReadOnlyList<BodyStmt>? Block) : Expr;
public sealed record Not(Expr Value) : Expr;
public sealed record Neg(Expr Value) : Expr;
public sealed record Cond(Expr Test, Expr Then, Expr Else) : Expr;
public sealed record Get(Expr Target, Expr Key) : Expr;
public sealed record Bin(string Op, Expr Left, Expr Right) : Expr;
public sealed record CallExpr(Expr Callee, IReadOnlyList<Expr> Args, IReadOnlyList<(string Name, Expr Value)> Named) : Expr;

/// <summary>A statement in a lambda's body: <c>let x = …</c> (Name set) or <c>return …</c>.</summary>
public sealed record BodyStmt(string? Name, Expr Value);

// ── Statements ───────────────────────────────────────────────────────────────

public abstract record Stmt(int Pos);
public sealed record LetStmt(string Name, Expr Value, IReadOnlyList<Stmt>? Block, int At) : Stmt(At);
public sealed record SetStmt(string Name, Expr Value, int At) : Stmt(At);
public sealed record FnStmt(string Name, IReadOnlyList<(string Name, Expr? Default)> Params, IReadOnlyList<Stmt> Body, int At) : Stmt(At);
public sealed record IfStmt(Expr Test, IReadOnlyList<Stmt> Then, IReadOnlyList<Stmt>? Else, int At) : Stmt(At);
public sealed record ForStmt(string Name, Expr List, Expr? Where, IReadOnlyList<Stmt> Body, int At) : Stmt(At);
public sealed record ReturnStmt(Expr? Value, int At) : Stmt(At);
public sealed record DoStmt(Expr Value, IReadOnlyList<Stmt>? Block, int At) : Stmt(At);

/// <summary>
/// A Pratt parser for expressions, and the statement grammar on top of it.
/// Inside brackets newlines are skipped; at statement level a newline ends
/// the statement (the lexer has already joined lines that continue).
/// </summary>
public sealed class Parser(List<Tok> toks, string src)
{
    private int _i, _depth;

    public static Expr ParseExpression(string src)
    {
        var p = new Parser(Lexer.Tokenize(src), src);
        var e = p.Expression();
        if (p.Peek().Kind != TokKind.End) p.Fail($"unexpected '{p.Peek().Value}' after the expression");
        return e;
    }

    public static IReadOnlyList<Stmt> ParseProgram(string src) =>
        new Parser(Lexer.Tokenize(src, keepNewlines: true), src).Program();

    // ── tokens ──

    private Tok Peek(int k = 0)
    {
        int j = _i, n = 0;
        while (true)
        {
            var t = toks[j];
            if (t.Kind == TokKind.Newline && _depth > 0) { j++; continue; }
            if (n == k) return t;
            if (t.Kind == TokKind.End) return t;
            n++; j++;
        }
    }

    private Tok Next()
    {
        while (toks[_i].Kind == TokKind.Newline && _depth > 0) _i++;
        var t = toks[_i];
        if (t.Kind != TokKind.End) _i++;
        return t;
    }

    private bool At(string value, int k = 0) => Peek(k).Is(value);

    private Tok Expect(string value)
    {
        var t = Next();
        if (t.Value != value || t.Kind is TokKind.String or TokKind.Template)
            throw new DocError($"expected '{value}', found {(t.Kind == TokKind.End ? "the end" : $"'{t.Value}'")} at {Lexer.LineCol(src, t.Pos)}");
        return t;
    }

    private DocError Fail(string message, Tok? at = null) =>
        throw new DocError($"{message} at {Lexer.LineCol(src, (at ?? Peek()).Pos)}");

    // ── expressions ──

    private static readonly string[][] Levels =
    [
        ["??"], ["||"], ["&&"], ["==", "!=", "===", "!=="], ["<", "<=", ">", ">="], ["+", "-"], ["*", "/", "%"],
    ];

    public Expr Expression()
    {
        var test = Binary(0);
        if (!At("?")) return test;
        Next();
        _depth++;
        var then = Expression();
        Expect(":");
        _depth--;
        return new Cond(test, then, Expression());
    }

    private Expr Binary(int level)
    {
        if (level == Levels.Length) return Unary();
        var left = Binary(level + 1);
        while (Peek() is { Kind: TokKind.Op } t && Array.IndexOf(Levels[level], t.Value) >= 0)
        {
            string op = Next().Value switch { "===" => "==", "!==" => "!=", var o => o };
            left = new Bin(op, left, Binary(level + 1));
        }
        return left;
    }

    private Expr Unary()
    {
        if (At("!")) { Next(); return new Not(Unary()); }
        if (At("-")) { Next(); return new Neg(Unary()); }
        return Postfix(Primary());
    }

    private Expr Postfix(Expr e)
    {
        while (true)
        {
            var t = Peek();
            if (t.Kind == TokKind.Op && t.Value is "." or "?.")
            {
                Next();
                var name = Next();
                if (name.Kind != TokKind.Name) Fail("expected a name after '.'", name);
                e = new Get(e, new Lit(name.Value));
            }
            else if (t.Is("["))
            {
                Next(); _depth++;
                var key = Expression();
                Expect("]"); _depth--;
                e = new Get(e, key);
            }
            else if (t.Is("("))
            {
                var (args, named) = Arguments();
                e = new CallExpr(e, args, named);
            }
            else return e;
        }
    }

    private (List<Expr>, List<(string, Expr)>) Arguments()
    {
        Expect("("); _depth++;
        var args = new List<Expr>();
        var named = new List<(string, Expr)>();
        while (!At(")"))
        {
            var t = Peek();
            var t2 = Peek(1);
            if (t.Kind == TokKind.Name && t2.Kind == TokKind.Op && t2.Value is ":" or "=")
            {
                Next(); Next();
                named.Add((t.Value, Expression()));
            }
            else
            {
                if (named.Count > 0) Fail("a positional argument after a named one");
                args.Add(Expression());
            }
            if (!At(")")) Expect(",");
        }
        Expect(")"); _depth--;
        return (args, named);
    }

    // At '(' : is this '(a, b) =>' ?
    private bool ArrowParams()
    {
        for (int j = 1; ; j++)
        {
            var t = Peek(j);
            if (t.Is(")")) return Peek(j + 1).Is("=>");
            if (t.Kind == TokKind.Name || t.Is(",")) continue;
            return false;
        }
    }

    // x => expr, or x => { let a = …; return … }
    private Lambda ArrowBody(List<string> parameters)
    {
        if (!At("{")) return new Lambda(parameters, Expression(), null);
        Next(); _depth++;
        var body = new List<BodyStmt>();
        while (!At("}"))
        {
            if (At(";")) { Next(); continue; }
            if (At("let") || At("const"))
            {
                Next();
                string name = Next().Value;
                Expect("=");
                body.Add(new BodyStmt(name, Expression()));
            }
            else if (At("return"))
            {
                Next();
                body.Add(new BodyStmt(null, Expression()));
            }
            else Fail("a function body holds only `let x = …` and `return …`");
        }
        Expect("}"); _depth--;
        return new Lambda(parameters, null, body);
    }

    private Expr Primary()
    {
        var t = Peek();
        switch (t.Kind)
        {
            case TokKind.Number:
                Next();
                return new Lit(double.Parse(t.Value, CultureInfo.InvariantCulture));
            case TokKind.String:
                Next();
                return Template(t.Value[1..^1], backtick: false);
            case TokKind.Template:
                Next();
                return Template(t.Value[1..^1], backtick: true);
            case TokKind.Name:
                if (At("=>", 1)) { Next(); Next(); return ArrowBody([t.Value]); }
                Next();
                return t.Value switch
                {
                    "true" => new Lit(true),
                    "false" => new Lit(false),
                    "null" or "undefined" => new Lit(null),
                    _ => new NameRef(t.Value),
                };
        }
        if (t.Is("("))
        {
            if (ArrowParams())
            {
                Next();
                var ps = new List<string>();
                while (!At(")"))
                {
                    ps.Add(Next().Value);
                    if (At(",")) Next();
                }
                Next();
                Expect("=>");
                return ArrowBody(ps);
            }
            Next(); _depth++;
            var e = Expression();
            Expect(")"); _depth--;
            return e;
        }
        if (t.Is("["))
        {
            Next(); _depth++;
            var items = new List<(bool, Expr)>();
            while (!At("]"))
            {
                if (At("...")) { Next(); items.Add((true, Expression())); }
                else items.Add((false, Expression()));
                if (!At("]")) Expect(",");
            }
            Expect("]"); _depth--;
            return new ArrayLit(items);
        }
        if (t.Is("{"))
        {
            Next(); _depth++;
            var pairs = new List<(string?, Expr)>();
            while (!At("}"))
            {
                if (At("..."))
                {
                    Next();
                    pairs.Add((null, Expression()));
                    if (!At("}")) Expect(",");
                    continue;
                }
                var k = Next();
                if (k.Kind is not (TokKind.Name or TokKind.String)) Fail("expected a key in an object", k);
                string key = k.Kind == TokKind.Name ? k.Value : Unescape(k.Value[1..^1]);
                if (At(":")) { Next(); pairs.Add((key, Expression())); }
                else pairs.Add((key, new NameRef(key)));
                if (!At("}")) Expect(",");
            }
            Expect("}"); _depth--;
            return new ObjectLit(pairs);
        }
        throw Fail(t.Kind == TokKind.End ? "unexpected end of input" : $"unexpected '{t.Value}'", t);
    }

    private static string Unescape(string body)
    {
        var sb = new StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length)
            {
                char c = body[++i];
                sb.Append(c switch { 'n' => '\n', 't' => '\t', _ => c });
            }
            else sb.Append(body[i]);
        }
        return sb.ToString();
    }

    /// <summary>'Hi {name}': the text, with each {expr} (or ${expr} in backticks) parsed.</summary>
    public static Tmpl Template(string text, bool backtick)
    {
        var parts = new List<object>();
        var buf = new StringBuilder();
        string opener = backtick ? "${" : "{";
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                char c = text[i + 1];
                buf.Append(c switch { 'n' => '\n', 't' => '\t', _ => c });
                i += 2;
                continue;
            }
            if (string.CompareOrdinal(text, i, opener, 0, opener.Length) == 0)
            {
                int j = i + opener.Length, depth = 1, k = j;
                while (k < text.Length && depth > 0)
                {
                    if (text[k] == '{') depth++;
                    else if (text[k] == '}') depth--;
                    k++;
                }
                if (depth > 0) throw new DocError($"unclosed {{ in string \"{text}\"");
                if (buf.Length > 0) { parts.Add(buf.ToString()); buf.Clear(); }
                parts.Add(ParseExpression(text[j..(k - 1)]));
                i = k;
                continue;
            }
            buf.Append(text[i]);
            i++;
        }
        if (buf.Length > 0) parts.Add(buf.ToString());
        return new Tmpl(parts);
    }

    // ── statements ──

    private void SkipNewlines()
    {
        while (toks[_i].Kind == TokKind.Newline || toks[_i].Is(";")) _i++;
    }

    private List<Stmt> Program()
    {
        var output = new List<Stmt>();
        while (true)
        {
            SkipNewlines();
            if (Peek().Kind == TokKind.End) return output;
            output.Add(Statement());
        }
    }

    private List<Stmt> Block()
    {
        Expect("{");
        var output = new List<Stmt>();
        while (true)
        {
            SkipNewlines();
            var t = Peek();
            if (t.Is("}")) { Next(); return output; }
            if (t.Kind == TokKind.End) Fail("missing }");
            output.Add(Statement());
        }
    }

    private void End()
    {
        var t = toks[_i];
        if (t.Kind is TokKind.Newline or TokKind.End || t.Is(";") || t.Is("}")) return;
        Fail($"unexpected '{t.Value}'; one statement per line", t);
    }

    private List<Stmt>? MaybeBlock() => At("{") ? Block() : null;

    private Stmt Statement()
    {
        var t = Peek();
        int pos = t.Pos;
        if (t.Kind == TokKind.Name && t.Value is "let" or "const" or "var")
        {
            Next();
            var name = Next();
            if (name.Kind != TokKind.Name) Fail("expected a name after let", name);
            Expect("=");
            var e = Expression();
            var b = MaybeBlock();
            End();
            return new LetStmt(name.Value, e, b, pos);
        }
        if (t.Kind == TokKind.Name && t.Value is "fn" or "function")
        {
            Next();
            string name = Next().Value;
            Expect("(");
            _depth++;
            var ps = new List<(string, Expr?)>();
            while (!At(")"))
            {
                string p = Next().Value;
                Expr? fallback = null;
                if (At(":") || At("=")) { Next(); fallback = Expression(); }
                ps.Add((p, fallback));
                if (At(",")) Next();
            }
            Expect(")");
            _depth--;
            return new FnStmt(name, ps, Block(), pos);
        }
        if (t.Kind == TokKind.Name && t.Value == "return")
        {
            Next();
            Expr? e = toks[_i].Kind is TokKind.Newline or TokKind.End || At("}") ? null : Expression();
            End();
            return new ReturnStmt(e, pos);
        }
        if (t.Kind == TokKind.Name && At("=", 1))
        {
            Next(); Next();
            var e = Expression();
            End();
            return new SetStmt(t.Value, e, pos);
        }
        if (t.Kind == TokKind.Name && t.Value == "if")
        {
            Next();
            var test = Expression();
            var then = Block();
            List<Stmt>? other = null;
            int j = _i;
            SkipNewlines();
            if (At("else"))
            {
                Next();
                other = At("if") ? [Statement()] : Block();
            }
            else _i = j;
            return new IfStmt(test, then, other, pos);
        }
        if (t.Kind == TokKind.Name && t.Value == "for")
        {
            Next();
            string name = Next().Value;
            var t2 = Next();
            if (t2.Value is not ("in" or "of")) Fail("expected 'in' after the loop variable", t2);
            var list = Expression();
            Expr? where = null;
            if (At("where")) { Next(); where = Expression(); }
            return new ForStmt(name, list, where, Block(), pos);
        }
        var expr = Expression();
        var block = MaybeBlock();
        End();
        return new DoStmt(expr, block, pos);
    }
}
