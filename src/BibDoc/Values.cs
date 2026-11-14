using System.Globalization;
using System.Text;

namespace BibDoc;

/// <summary>Anything that makes a document fail to parse or run. The message names the line and, where it can, the fix.</summary>
public class DocError(string message) : Exception(message);

/// <summary>An effect reached while drawing, not while handling a click.</summary>
public sealed class EffectOutsideEvent(string message) : DocError(message);

/// <summary>
/// A function a document can call: a lambda it wrote, a builtin, or a widget.
/// <see cref="Arity"/> is how many arguments a list method hands it
/// (item, index, list); <see cref="TakesNamed"/> marks the ones that take
/// named arguments (widgets and effects).
/// </summary>
public abstract class Callable
{
    public virtual int Arity => 1;
    public virtual bool TakesNamed => false;
    public abstract object? Invoke(IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?>? named);
}

public sealed class Builtin(Func<IReadOnlyList<object?>, object?> fn, int arity = 1) : Callable
{
    public override int Arity => arity;
    public override object? Invoke(IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?>? named) => fn(args);
}

/// <summary>
/// A length known only once a box is laid out: <see cref="K"/> × its width or height
/// (<see cref="Axis"/> "w" or "h"), plus <see cref="C"/>. Inside a layer, w and h are these;
/// the renderer works them out when it draws.
/// </summary>
public sealed record Dim(string Axis, double K = 1, double C = 0);

public sealed class NamedBuiltin(Func<IReadOnlyList<object?>, IReadOnlyDictionary<string, object?>, object?> fn) : Callable
{
    public override bool TakesNamed => true;
    public override object? Invoke(IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?>? named) =>
        fn(args, named ?? Values.NoNames);
}

/// <summary>
/// The value rules, as in JavaScript where the two agree with each other and with
/// the reference interpreter: numbers are doubles, null and missing are one thing,
/// lists are <see cref="List{T}"/> and records <see cref="Dictionary{TKey,TValue}"/>.
/// </summary>
public static class Values
{
    public static readonly IReadOnlyDictionary<string, object?> NoNames = new Dictionary<string, object?>();

    public static bool IsNumber(object? v) => v is double;

    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true,
    };

    public static double Num(object? v) => v switch
    {
        null => 0,
        bool b => b ? 1 : 0,
        double d => d,
        string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        Dim => throw new DocError("w and h are only known when the box is drawn: they take + - * / with numbers, not min, max, round or Math; give the box a size in px to compute with it"),
        _ => double.NaN,
    };

    public static string Str(object? v) => v switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        double d => NumStr(d),
        string s => s,
        List<object?> xs => string.Join(",", xs.Select(Str)),
        Dictionary<string, object?> o => "{" + string.Join(", ", o.Select(kv => $"'{kv.Key}': {Repr(kv.Value)}")) + "}",
        Callable => "<function>",
        Dim d => $"{d.Axis}*{NumStr(d.K)}+{NumStr(d.C)}",
        _ => v.ToString() ?? "",
    };

    private static string Repr(object? v) => v is string s ? $"'{s}'" : v is List<object?> xs ? "[" + string.Join(", ", xs.Select(Repr)) + "]" : Str(v);

    public static string NumStr(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e21) return d.ToString("F0", CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Equality by value: lists and records compare their contents.</summary>
    public static bool Eq(object? x, object? y)
    {
        if (x is bool || y is bool) return x is bool a && y is bool b && a == b;
        if (x is double dx && y is double dy) return dx == dy;
        if (x is null || y is null) return x is null && y is null;
        if (x is string sx && y is string sy) return sx == sy;
        if (x is List<object?> lx && y is List<object?> ly)
        {
            if (lx.Count != ly.Count) return false;
            for (int i = 0; i < lx.Count; i++) if (!Eq(lx[i], ly[i])) return false;
            return true;
        }
        if (x is Dictionary<string, object?> ox && y is Dictionary<string, object?> oy)
            return ox.Count == oy.Count && ox.All(kv => oy.TryGetValue(kv.Key, out var w) && Eq(kv.Value, w));
        return ReferenceEquals(x, y);
    }

    /// <summary>A value as JSON: what frames and effects are compared and archived as.</summary>
    public static void WriteJson(StringBuilder sb, object? v)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case double d: sb.Append(double.IsFinite(d) ? NumStr(d) : "null"); break;
            case Dim dim:
                sb.Append("{\"dim\":"); WriteString(sb, dim.Axis);
                sb.Append(",\"k\":").Append(NumStr(dim.K)).Append(",\"c\":").Append(NumStr(dim.C)).Append('}');
                break;
            case string s: WriteString(sb, s); break;
            case List<object?> xs:
                sb.Append('[');
                for (int i = 0; i < xs.Count; i++) { if (i > 0) sb.Append(','); WriteJson(sb, xs[i]); }
                sb.Append(']');
                break;
            case Dictionary<string, object?> o:
                sb.Append('{');
                bool first = true;
                foreach (var (k, x) in o)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, k); sb.Append(':'); WriteJson(sb, x);
                }
                sb.Append('}');
                break;
            default: WriteString(sb, Str(v)); break;
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
