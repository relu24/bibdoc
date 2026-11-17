using System.Globalization;
using static BibDoc.Values;

namespace BibDoc;

/// <summary>A chain of names: a block's lets over its parent's, down to the builtins.</summary>
public sealed class Scope(Scope? parent = null, Dictionary<string, object?>? vars = null)
{
    public Scope? Parent { get; } = parent;
    public Dictionary<string, object?> Vars { get; } = vars ?? [];

    public object? Lookup(string name)
    {
        for (var s = this; s is not null; s = s.Parent)
            if (s.Vars.TryGetValue(name, out var v)) return v;
        throw new DocError($"unknown name '{name}'");
    }

    public bool Has(string name)
    {
        for (var s = this; s is not null; s = s.Parent)
            if (s.Vars.ContainsKey(name)) return true;
        return false;
    }

    public Scope Child(Dictionary<string, object?>? vars = null) => new(this, vars);
}

/// <summary>
/// Every document must finish, and stay small: a fixed number of steps per run, and a fixed
/// size of what the run builds and goes through, counted in values: a list item, a record
/// field, a character of text.
/// </summary>
public sealed class Budget
{
    public const int Limit = 200_000;
    public const long SizeLimit = 4_000_000;
    private int _left = Limit;
    private long _room = SizeLimit;
    public void Reset() { _left = Limit; _room = SizeLimit; }
    /// <summary>The steps this run has taken so far.</summary>
    public int Used => Limit - _left;
    /// <summary>The size this run has built and gone through so far.</summary>
    public long Sized => SizeLimit - Math.Max(_room, 0);
    public void Spend()
    {
        if (--_left < 0) throw new DocError("step budget exhausted");
    }

    /// <summary>Before building or going through that many values.</summary>
    public void Size(long n)
    {
        if ((_room -= n) < 0)
            throw new DocError($"size budget exhausted: a run builds and goes through at most {SizeLimit:N0} values (list items, record fields, characters)");
    }

    /// <summary>Equality by value, the values it looks at counted.</summary>
    public bool Eq(object? x, object? y)
    {
        long left = Math.Max(_room, 0) + 1;
        bool eq = Values.Eq(x, y, ref left);
        Size(Math.Max(_room, 0) + 1 - left);
        return eq;
    }
}

/// <summary>A lambda the document wrote, closed over the scope it was written in.</summary>
public sealed class Closure(Lambda lambda, Scope scope, Evaluator ev) : Callable
{
    public override int Arity => lambda.Params.Count;

    /// <summary>Called, and not yet returned: a call now would be the lambda calling itself.</summary>
    public bool Running => ev.Running.Contains(lambda);

    public override object? Invoke(IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?>? named)
    {
        // As a fn may not: a lambda that called itself would go on until the stack, not the budget, ran out.
        if (!ev.Running.Add(lambda)) throw new DocError("a function calls itself; documents may not recurse");
        try
        {
            var vars = new Dictionary<string, object?>();
            for (int i = 0; i < lambda.Params.Count && i < args.Count; i++) vars[lambda.Params[i]] = args[i];
            var s = scope.Child(vars);
            if (lambda.Body is { } body) return ev.Eval(body, s);
            foreach (var st in lambda.Block!)
            {
                if (st.Name is { } name) s.Vars[name] = ev.Eval(st.Value, s);
                else return ev.Eval(st.Value, s);
            }
            return null;
        }
        finally { ev.Running.Remove(lambda); }
    }
}

/// <summary>Evaluates expressions: the pure, JavaScript-shaped part of the language.</summary>
public sealed class Evaluator(Budget budget)
{
    /// <summary>The lambdas called and not yet returned, each by where it is written.</summary>
    internal readonly HashSet<Lambda> Running = new(ReferenceEqualityComparer.Instance);

    public object? Eval(Expr e, Scope s)
    {
        budget.Spend();
        switch (e)
        {
            case Lit l: return l.Value;
            case Tmpl t:
            {
                if (t.Parts.Count == 0) return "";
                var sb = new System.Text.StringBuilder();
                foreach (var p in t.Parts)
                {
                    string part = p is string str ? str : Str(Eval((Expr)p, s));
                    budget.Size(part.Length);
                    sb.Append(part);
                }
                return sb.ToString();
            }
            case NameRef n: return s.Lookup(n.Name);
            case ArrayLit a:
            {
                var list = new List<object?>();
                foreach (var (spread, x) in a.Items)
                {
                    if (!spread) { budget.Size(1); list.Add(Eval(x, s)); continue; }
                    if (Eval(x, s) is List<object?> xs) { budget.Size(xs.Count); list.AddRange(xs); }
                }
                return list;
            }
            case ObjectLit o:
            {
                var rec = new Dictionary<string, object?>();
                foreach (var (key, x) in o.Pairs)
                {
                    if (key is not null) { budget.Size(1); rec[key] = Eval(x, s); continue; }
                    if (Eval(x, s) is Dictionary<string, object?> other)
                    {
                        budget.Size(other.Count);
                        foreach (var (k, v) in other) rec[k] = v;
                    }
                }
                return rec;
            }
            case Lambda f: return new Closure(f, s, this);
            case Not n: return !Truthy(Eval(n.Value, s));
            case Neg n: return -Num(Eval(n.Value, s));
            case Cond c: return Eval(Truthy(Eval(c.Test, s)) ? c.Then : c.Else, s);
            case Get g: return Member(Eval(g.Target, s), Eval(g.Key, s));
            case Bin b: return Binary(b, s);
            case CallExpr c: return Call(c, s);
        }
        throw new DocError("cannot evaluate " + e.GetType().Name);
    }

    // w and h take + - * / with numbers, and + - with themselves; they cannot be compared.
    private static Dim DimOp(string op, object? l, object? r)
    {
        if (l is Dim a && r is Dim b)
        {
            if (a.Axis != b.Axis || op is not ("+" or "-")) throw new DocError("w and h only add to and subtract from themselves");
            double sign = op == "+" ? 1 : -1;
            return new Dim(a.Axis, a.K + sign * b.K, a.C + sign * b.C);
        }
        if (op is not ("+" or "-" or "*" or "/"))
            throw new DocError("w and h are only known when the box is drawn: they cannot be compared");
        if (l is Dim x)
        {
            double n = Num(r);
            return op switch
            {
                "+" => x with { C = x.C + n },
                "-" => x with { C = x.C - n },
                "*" => new Dim(x.Axis, x.K * n, x.C * n),
                _ => new Dim(x.Axis, x.K / n, x.C / n),
            };
        }
        var y = (Dim)r!;
        double m = Num(l);
        return op switch
        {
            "+" => y with { C = y.C + m },
            "-" => new Dim(y.Axis, -y.K, m - y.C),
            "*" => new Dim(y.Axis, y.K * m, y.C * m),
            _ => throw new DocError("a number divided by w or h is not a length"),
        };
    }

    public static object? Member(object? target, object? key)
    {
        switch (target)
        {
            case null: return null;
            case Dictionary<string, object?> o:
                return o.TryGetValue(key as string ?? Str(key), out var v) ? v : null;
            case List<object?> xs:
                if (key is "length") return (double)xs.Count;
                if (key is double d) { int i = (int)d; return i >= 0 && i < xs.Count ? xs[i] : null; }
                return null;
            case string str:
                if (key is "length") return (double)str.Length;
                if (key is double ds) { int i = (int)ds; return i >= 0 && i < str.Length ? str[i].ToString() : null; }
                return null;
        }
        return null;
    }

    private object? Binary(Bin b, Scope s)
    {
        switch (b.Op)
        {
            case "&&": { var x = Eval(b.Left, s); return Truthy(x) ? Eval(b.Right, s) : x; }
            case "||": { var x = Eval(b.Left, s); return Truthy(x) ? x : Eval(b.Right, s); }
            case "??": { var x = Eval(b.Left, s); return x ?? Eval(b.Right, s); }
        }
        var l = Eval(b.Left, s);
        var r = Eval(b.Right, s);
        if (l is Dim || r is Dim) return DimOp(b.Op, l, r);
        switch (b.Op)
        {
            case "==": return budget.Eq(l, r);
            case "!=": return !budget.Eq(l, r);
            case "+":
                if (l is string || r is string)
                {
                    string sl = Str(l), sr = Str(r);
                    budget.Size(sl.Length + sr.Length);
                    return sl + sr;
                }
                if (l is List<object?> la && r is List<object?> lb)
                {
                    budget.Size(la.Count + lb.Count);
                    return la.Concat(lb).ToList();
                }
                return Num(l) + Num(r);
            case "<" or "<=" or ">" or ">=":
            {
                if (l is null || r is null) return false;
                int c;
                if (l is string sl && r is string sr) c = string.CompareOrdinal(sl, sr);
                else
                {
                    double x = Num(l), y = Num(r);
                    if (double.IsNaN(x) || double.IsNaN(y)) return false;
                    c = x.CompareTo(y);
                }
                return b.Op switch { "<" => c < 0, "<=" => c <= 0, ">" => c > 0, _ => c >= 0 };
            }
        }
        double a = Num(l), bb = Num(r);
        return b.Op switch
        {
            "-" => a - bb,
            "*" => a * bb,
            "/" => bb == 0 ? double.NaN : a / bb,
            "%" => bb == 0 ? double.NaN : a % bb,
            _ => throw new DocError("unknown operator " + b.Op),
        };
    }

    private object? Call(CallExpr c, Scope s)
    {
        if (c.Callee is Get { Key: Lit { Value: string method } } g)
        {
            var target = Eval(g.Target, s);
            var args = c.Args.Select(a => Eval(a, s)).ToList();
            switch (target)
            {
                case List<object?> xs: return ListMethod(xs, method, args);
                case string str: return StringMethod(str, method, args);
                case double d when method == "toFixed":
                    return d.ToString("F" + (args.Count > 0 ? Math.Clamp((int)Num(args[0]), 0, 100) : 0), CultureInfo.InvariantCulture);
                case Dictionary<string, object?> o when o.TryGetValue(method, out var f) && f is Callable fn:
                    return fn.Invoke(args, null);
            }
            throw new DocError($"'{Str(target)}' has no method '{method}'");
        }
        var callee = Eval(c.Callee, s);
        var positional = c.Args.Select(a => Eval(a, s)).ToList();
        Dictionary<string, object?>? named = null;
        if (c.Named.Count > 0)
        {
            named = [];
            foreach (var (k, v) in c.Named) named[k] = Eval(v, s);
        }
        if (callee is not Callable call) throw new DocError($"'{Str(callee)}' is not a function");
        if (call is Closure { Running: true } && c.Callee is NameRef self) throw new DocError($"{self.Name}() calls itself; documents may not recurse");
        if (named is not null && !call.TakesNamed) throw new DocError("named arguments only go to widgets");
        return call.Invoke(positional, named);
    }

    private static object? Invoke(object? f, params object?[] args)
    {
        if (f is not Callable c) throw new DocError($"'{Str(f)}' is not a function");
        return c.Invoke(args, null);
    }

    private static int ArityOf(object? f) => f is Callable c ? c.Arity : 1;

    // A callback gets (item, index, list), as many as it takes.
    private static object? Each(object? f, object? x, int i, List<object?> xs) =>
        Math.Max(1, ArityOf(f)) switch
        {
            1 => Invoke(f, x),
            2 => Invoke(f, x, (double)i),
            _ => Invoke(f, x, (double)i, xs),
        };

    // What a list's method builds or goes through is counted, item by item; what a callback
    // does is counted as it runs.
    private object? ListMethod(List<object?> xs, string name, List<object?> args)
    {
        var f = args.Count > 0 ? args[0] : null;
        switch (name)
        {
            case "map" or "filter" or "reverse" or "sort" or "sum" or "count":
                budget.Size(xs.Count);
                break;
            case "join":
            {
                string sep = args.Count > 0 ? Str(f) : ",";
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < xs.Count; i++)
                {
                    string item = Str(xs[i]);
                    budget.Size(item.Length + (i > 0 ? sep.Length : 0) + 1);
                    if (i > 0) sb.Append(sep);
                    sb.Append(item);
                }
                return sb.ToString();
            }
            case "slice":
            {
                var part = Slice(xs, args);
                budget.Size(part.Count);
                return part;
            }
            case "concat":
            {
                var list = new List<object?>(xs);
                budget.Size(xs.Count);
                foreach (var a in args)
                {
                    if (a is List<object?> more) { budget.Size(more.Count); list.AddRange(more); }
                    else { budget.Size(1); list.Add(a); }
                }
                return list;
            }
            case "flatMap":
            {
                var list = new List<object?>();
                for (int i = 0; i < xs.Count; i++)
                {
                    var r = Each(f, xs[i], i, xs);
                    if (r is List<object?> more) { budget.Size(more.Count); list.AddRange(more); }
                    else { budget.Size(1); list.Add(r); }
                }
                return list;
            }
            case "includes":
                foreach (var x in xs) if (budget.Eq(x, f)) return true;
                return false;
            case "indexOf":
                for (int i = 0; i < xs.Count; i++) if (budget.Eq(xs[i], f)) return (double)i;
                return -1d;
        }
        return Methods(xs, name, args);
    }

    private static object? Methods(List<object?> xs, string name, List<object?> args)
    {
        var f = args.Count > 0 ? args[0] : null;
        switch (name)
        {
            case "map": return xs.Select((x, i) => Each(f, x, i, xs)).ToList();
            case "filter": return xs.Where((x, i) => Truthy(Each(f, x, i, xs))).ToList();
            case "find":
                for (int i = 0; i < xs.Count; i++) if (Truthy(Each(f, xs[i], i, xs))) return xs[i];
                return null;
            case "findIndex":
                for (int i = 0; i < xs.Count; i++) if (Truthy(Each(f, xs[i], i, xs))) return (double)i;
                return -1d;
            case "some":
                for (int i = 0; i < xs.Count; i++) if (Truthy(Each(f, xs[i], i, xs))) return true;
                return false;
            case "every":
                for (int i = 0; i < xs.Count; i++) if (!Truthy(Each(f, xs[i], i, xs))) return false;
                return true;
            case "reverse": { var list = new List<object?>(xs); list.Reverse(); return list; }
            case "sort":
                return f is null
                    ? xs.OrderBy(Str, StringComparer.Ordinal).ToList()
                    : xs.OrderBy(x => x, Comparer<object?>.Create((a, b) => Num(Invoke(f, a, b)) is var c && double.IsNaN(c) ? 0 : Math.Sign(c))).ToList();
            case "reduce":
            {
                var acc = args.Count > 1 ? args[1] : null;
                int arity = Math.Max(2, ArityOf(f));
                for (int i = 0; i < xs.Count; i++)
                    acc = arity switch { 2 => Invoke(f, acc, xs[i]), 3 => Invoke(f, acc, xs[i], (double)i), _ => Invoke(f, acc, xs[i], (double)i, xs) };
                return acc;
            }
            case "push" or "pop" or "shift" or "unshift" or "splice":
                throw new DocError($"lists cannot be changed ({name}); build a new one: [...xs, x], xs.concat([x]) or unique(xs)");
            case "sum": return xs.Sum(x => Num(f is null ? x : Invoke(f, x)));
            case "count": return (double)(f is null ? xs.Count : xs.Count(x => Truthy(Invoke(f, x))));
        }
        throw new DocError($"lists have no method '{name}'");
    }

    // Python's slice rules: negative positions count from the end, and out of range is clipped.
    private static List<object?> Slice(List<object?> xs, List<object?> args)
    {
        int n = xs.Count;
        int Clip(object? a, int whenNone)
        {
            if (a is null) return whenNone;
            int i = (int)Num(a);
            if (i < 0) i += n;
            return Math.Clamp(i, 0, n);
        }
        int start = args.Count > 0 ? Clip(args[0], 0) : 0;
        int end = args.Count > 1 ? Clip(args[1], n) : n;
        return end > start ? xs.GetRange(start, end - start) : [];
    }

    // A string's method goes through the string, and builds no more than it plus what it is given.
    private object? StringMethod(string v, string name, List<object?> args)
    {
        budget.Size(v.Length + args.Sum(x => x is string sx ? sx.Length : 1));
        var a = args.Count > 0 ? args[0] : null;
        switch (name)
        {
            case "toUpperCase" or "upper": return v.ToUpperInvariant();
            case "toLowerCase" or "lower": return v.ToLowerInvariant();
            case "includes": return v.Contains(Str(a), StringComparison.Ordinal);
            case "startsWith": return v.StartsWith(Str(a), StringComparison.Ordinal);
            case "endsWith": return v.EndsWith(Str(a), StringComparison.Ordinal);
            case "trim": return v.Trim();
            case "split": return (Str(a) == "" ? v.Select(c => c.ToString()) : v.Split(Str(a))).Select(x => (object?)x).ToList();
            case "slice":
            {
                int n = v.Length;
                int Clip(object? x, int whenNone) { if (x is null) return whenNone; int i = (int)Num(x); if (i < 0) i += n; return Math.Clamp(i, 0, n); }
                int start = Clip(a, 0), end = args.Count > 1 ? Clip(args[1], n) : n;
                return end > start ? v[start..end] : "";
            }
            case "replace":
            {
                string from = Str(a);
                int at = v.IndexOf(from, StringComparison.Ordinal);
                return at < 0 ? v : v[..at] + Str(args.Count > 1 ? args[1] : null) + v[(at + from.Length)..];
            }
        }
        throw new DocError($"strings have no method '{name}'");
    }

    // ── builtins ──

    private static double Round(double x, double digits)
    {
        double p = Math.Pow(10, (int)digits);
        return Math.Floor(x * p + 0.5) / p;
    }

    // Numbers, text, true, false and null are told apart by a set; lists and records each
    // against the others kept so far, counted.
    private List<object?> Unique(object? v)
    {
        var output = new List<object?>();
        if (v is not List<object?> xs) return output;
        budget.Size(xs.Count);
        var seen = new HashSet<(int, object?)>();
        var kept = new List<object?>();
        foreach (var x in xs)
        {
            bool fresh = x switch
            {
                null => seen.Add((0, null)),
                bool b => seen.Add((1, b)),
                double d => double.IsNaN(d) || seen.Add((2, d)),
                string str => seen.Add((3, str)),
                _ => !kept.Any(y => budget.Eq(x, y)),
            };
            if (!fresh) continue;
            if (x is List<object?> or Dictionary<string, object?>) kept.Add(x);
            output.Add(x);
        }
        return output;
    }

    private Func<IReadOnlyList<object?>, object?> MinMax(bool max) => args =>
    {
        IEnumerable<object?> xs = args.Count == 1 && args[0] is List<object?> l ? l : args;
        budget.Size(args.Count == 1 && args[0] is List<object?> counted ? counted.Count : args.Count);
        var nums = xs.Select(Num).ToList();
        if (nums.Count == 0) return null;
        return max ? nums.Max() : nums.Min();
    };

    // A number as people read it: 1,234,567.5. With digits, that many decimals, kept; without, up to
    // two, trailing zeros dropped. compact: 1.2k, 3.4M, 5.6B (up to one decimal, unless digits says).
    // sign: a + before a positive number.
    public static string Fmt(double x, int? digits, bool compact, bool sign)
    {
        if (double.IsNaN(x)) return "NaN";
        if (double.IsInfinity(x)) return x > 0 ? "∞" : "-∞";
        string suffix = "";
        if (compact)
        {
            double ax = Math.Abs(x);
            (double div, suffix) = ax >= 1e12 ? (1e12, "T") : ax >= 1e9 ? (1e9, "B") : ax >= 1e6 ? (1e6, "M") : ax >= 1e3 ? (1e3, "k") : (1, "");
            x /= div;
        }
        int d = Math.Clamp(digits ?? (suffix == "" ? 2 : 1), 0, 15);
        string text = Math.Round(x, d, MidpointRounding.AwayFromZero).ToString("N" + d, CultureInfo.InvariantCulture);
        if (digits is null && text.Contains('.')) text = text.TrimEnd('0').TrimEnd('.');
        if (text is "-0" || text.StartsWith("-0") && text.Trim('-', '0', '.', ',').Length == 0) text = text[1..];
        return (sign && x > 0 && text.Trim('0', '.', ',').Length > 0 ? "+" : "") + text + suffix;
    }

    public Dictionary<string, object?> Builtins() => new()
    {
        ["count"] = new Builtin(a => a.Count > 0 && a[0] is List<object?> xs
            ? ListMethod(xs, "count", a.Count > 1 ? [a[1]] : [])
            : (double)(a.Count > 0 && a[0] is string s ? s.Length : 0)),
        ["sum"] = new Builtin(a => ListMethod(a.Count > 0 && a[0] is List<object?> xs ? xs : [], "sum", a.Count > 1 ? [a[1]] : [])),
        ["min"] = new Builtin(MinMax(false)),
        ["max"] = new Builtin(MinMax(true)),
        ["round"] = new Builtin(a => Round(Num(a.ElementAtOrDefault(0)), a.Count > 1 ? Num(a[1]) : 0)),
        ["abs"] = new Builtin(a => Math.Abs(Num(a.ElementAtOrDefault(0)))),
        ["floor"] = new Builtin(a => Math.Floor(Num(a.ElementAtOrDefault(0)))),
        ["ceil"] = new Builtin(a => Math.Ceiling(Num(a.ElementAtOrDefault(0)))),
        ["unique"] = new Builtin(a => Unique(a.ElementAtOrDefault(0))),
        ["str"] = new Builtin(a => Str(a.ElementAtOrDefault(0))),
        ["String"] = new Builtin(a => Str(a.ElementAtOrDefault(0))),
        ["num"] = new Builtin(a => Num(a.ElementAtOrDefault(0))),
        ["Number"] = new Builtin(a => Num(a.ElementAtOrDefault(0))),
        ["Infinity"] = double.PositiveInfinity,
        ["fmt"] = new NamedBuiltin((a, named) => Fmt(Num(a.ElementAtOrDefault(0)), a.Count > 1 ? (int?)Num(a[1]) : null,
            Truthy(named.GetValueOrDefault("compact")), Truthy(named.GetValueOrDefault("sign")))),
        ["fade"] = new Builtin(a => $"{Str(a.ElementAtOrDefault(0))}@{NumStr(a.Count > 1 ? Num(a[1]) : 0.15)}", 2),
        ["Math"] = new Dictionary<string, object?>
        {
            ["round"] = new Builtin(a => Round(Num(a.ElementAtOrDefault(0)), 0)),
            ["floor"] = new Builtin(a => Math.Floor(Num(a.ElementAtOrDefault(0)))),
            ["ceil"] = new Builtin(a => Math.Ceiling(Num(a.ElementAtOrDefault(0)))),
            ["abs"] = new Builtin(a => Math.Abs(Num(a.ElementAtOrDefault(0)))),
            ["min"] = new Builtin(MinMax(false)),
            ["max"] = new Builtin(MinMax(true)),
            ["sign"] = new Builtin(a => (double)Math.Sign(Num(a.ElementAtOrDefault(0)))),
            ["sqrt"] = new Builtin(a => Math.Sqrt(Num(a.ElementAtOrDefault(0)))),
            ["pow"] = new Builtin(a => Math.Pow(Num(a.ElementAtOrDefault(0)), Num(a.ElementAtOrDefault(1)))),
            ["exp"] = new Builtin(a => Math.Exp(Num(a.ElementAtOrDefault(0)))),
            ["log"] = new Builtin(a => Math.Log(Num(a.ElementAtOrDefault(0)))),
            ["log10"] = new Builtin(a => Math.Log10(Num(a.ElementAtOrDefault(0)))),
            ["log2"] = new Builtin(a => Math.Log2(Num(a.ElementAtOrDefault(0)))),
            ["sin"] = new Builtin(a => Math.Sin(Num(a.ElementAtOrDefault(0)))),
            ["cos"] = new Builtin(a => Math.Cos(Num(a.ElementAtOrDefault(0)))),
            ["tan"] = new Builtin(a => Math.Tan(Num(a.ElementAtOrDefault(0)))),
            ["asin"] = new Builtin(a => Math.Asin(Num(a.ElementAtOrDefault(0)))),
            ["acos"] = new Builtin(a => Math.Acos(Num(a.ElementAtOrDefault(0)))),
            ["atan"] = new Builtin(a => Math.Atan(Num(a.ElementAtOrDefault(0)))),
            ["atan2"] = new Builtin(a => Math.Atan2(Num(a.ElementAtOrDefault(0)), Num(a.ElementAtOrDefault(1)))),
            ["hypot"] = new Builtin(a => Math.Sqrt(a.Sum(x => Num(x) * Num(x)))),
            ["PI"] = Math.PI,
            ["E"] = Math.E,
        },
        ["Object"] = new Dictionary<string, object?>
        {
            ["keys"] = new Builtin(a => a.ElementAtOrDefault(0) is Dictionary<string, object?> o ? o.Keys.Select(k => (object?)k).ToList() : new List<object?>()),
            ["values"] = new Builtin(a => a.ElementAtOrDefault(0) is Dictionary<string, object?> o ? o.Values.ToList() : new List<object?>()),
            ["entries"] = new Builtin(a => a.ElementAtOrDefault(0) is Dictionary<string, object?> o
                ? o.Select(kv => (object?)new List<object?> { kv.Key, kv.Value }).ToList() : new List<object?>()),
        },
    };
}
