using System.Globalization;

namespace BibDoc;

/// <summary>
/// The YAML a document's front matter needs, and no more: block mappings and
/// sequences by indentation, flow <c>{a: 1}</c> and <c>[x, y]</c>, quoted and plain
/// scalars, numbers, booleans, null, and # comments. Values come out as the
/// language's values: doubles, strings, lists and records.
/// </summary>
public static class Yaml
{
    private readonly record struct Line(int Indent, string Text, int Number);

    public static object? Parse(string text)
    {
        var lines = new List<Line>();
        var raw = text.Replace("\r\n", "\n").Split('\n');
        for (int n = 0; n < raw.Length; n++)
        {
            string l = StripComment(raw[n]).TrimEnd();
            if (l.Trim() is "" or "---" or "...") continue;
            int indent = l.Length - l.TrimStart(' ').Length;
            lines.Add(new(indent, l.Trim(), n + 1));
        }
        int at = 0;
        return lines.Count == 0 ? null : Node(lines, ref at, lines[0].Indent);
    }

    private static object? Node(List<Line> lines, ref int at, int indent)
    {
        if (at >= lines.Count) return null;
        return lines[at].Text.StartsWith("- ", StringComparison.Ordinal) || lines[at].Text == "-"
            ? Sequence(lines, ref at, indent)
            : Mapping(lines, ref at, indent);
    }

    private static List<object?> Sequence(List<Line> lines, ref int at, int indent)
    {
        var list = new List<object?>();
        while (at < lines.Count && lines[at].Indent == indent && (lines[at].Text.StartsWith("- ", StringComparison.Ordinal) || lines[at].Text == "-"))
        {
            var line = lines[at];
            string rest = line.Text.Length > 1 ? line.Text[2..].TrimStart() : "";
            if (rest.Length == 0)
            {
                at++;
                list.Add(at < lines.Count && lines[at].Indent > indent ? Node(lines, ref at, lines[at].Indent) : null);
            }
            else if (KeyOf(rest) is not null && !rest.StartsWith('{') && !rest.StartsWith('"') && !rest.StartsWith('\''))
            {
                // "- key: value" starts a mapping whose keys sit at the column after "- ".
                int inner = line.Indent + (line.Text.Length - rest.Length);
                lines[at] = new(inner, rest, line.Number);
                list.Add(Mapping(lines, ref at, inner));
            }
            else
            {
                list.Add(Scalar(rest, line.Number));
                at++;
            }
        }
        return list;
    }

    private static Dictionary<string, object?> Mapping(List<Line> lines, ref int at, int indent)
    {
        var map = new Dictionary<string, object?>();
        while (at < lines.Count && lines[at].Indent == indent)
        {
            var line = lines[at];
            if (KeyOf(line.Text, out int valueAt) is not { } key) throw new DocError($"front matter line {line.Number}: expected 'key: value'");
            string rest = line.Text[valueAt..].Trim();
            at++;
            if (rest.Length > 0) map[key] = Scalar(rest, line.Number);
            else if (at < lines.Count && (lines[at].Indent > indent || lines[at].Indent == indent && lines[at].Text.StartsWith("- ", StringComparison.Ordinal)))
                map[key] = Node(lines, ref at, lines[at].Indent);
            else map[key] = null;
        }
        return map;
    }

    // "key: value" or "key:" gives key, and where its value starts; "\"a key\": value" too.
    // Anything else is not a mapping entry.
    private static string? KeyOf(string text) => KeyOf(text, out _);

    private static string? KeyOf(string text, out int valueAt)
    {
        valueAt = text.Length;
        if (text.Length > 0 && text[0] is '"' or '\'')
        {
            int close = text.IndexOf(text[0], 1);
            if (close <= 0 || close + 1 >= text.Length || text[close + 1] != ':') return null;
            valueAt = close + 2;
            return text[1..close];
        }
        if (text.StartsWith('{') || text.StartsWith('[')) return null;
        int colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0 && text.EndsWith(':')) colon = text.Length - 1;
        if (colon <= 0) return null;
        valueAt = colon + 1;
        return text[..colon].Trim();
    }

    private static object? Scalar(string text, int line)
    {
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            int i = 0;
            var v = Flow(text, ref i, line);
            return v;
        }
        return Plain(text);
    }

    private static object? Plain(string t)
    {
        t = t.Trim();
        if (t.Length >= 2 && (t[0] == '"' && t[^1] == '"' || t[0] == '\'' && t[^1] == '\'')) return Unquote(t);
        return t switch
        {
            "" or "~" or "null" or "Null" or "NULL" => null,
            "true" or "True" or "TRUE" => true,
            "false" or "False" or "FALSE" => false,
            _ => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !t.EndsWith('.') ? d : t,
        };
    }

    private static string Unquote(string t)
    {
        string body = t[1..^1];
        return t[0] == '\'' ? body.Replace("''", "'") : body.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
    }

    private static object? Flow(string s, ref int i, int line)
    {
        Skip(s, ref i);
        if (i >= s.Length) return null;
        if (s[i] == '{')
        {
            i++;
            var map = new Dictionary<string, object?>();
            while (true)
            {
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return map; }
                string key = Token(s, ref i, ":,}");
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new DocError($"front matter line {line}: expected ':' in {{…}}");
                i++;
                map[key.Trim().Trim('"', '\'')] = Flow(s, ref i, line);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') i++;
                else if (i < s.Length && s[i] == '}') { i++; return map; }
                else if (i >= s.Length) throw new DocError($"front matter line {line}: unclosed {{");
            }
        }
        if (s[i] == '[')
        {
            i++;
            var list = new List<object?>();
            while (true)
            {
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return list; }
                list.Add(Flow(s, ref i, line));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') i++;
                else if (i < s.Length && s[i] == ']') { i++; return list; }
                else if (i >= s.Length) throw new DocError($"front matter line {line}: unclosed [");
            }
        }
        return Plain(Token(s, ref i, ",}]"));
    }

    private static string Token(string s, ref int i, string stops)
    {
        Skip(s, ref i);
        int start = i;
        if (i < s.Length && s[i] is '"' or '\'')
        {
            char q = s[i++];
            while (i < s.Length && s[i] != q) i += s[i] == '\\' ? 2 : 1;
            i = Math.Min(i + 1, s.Length);
            return s[start..i];
        }
        while (i < s.Length && !stops.Contains(s[i])) i++;
        return s[start..i];
    }

    private static void Skip(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static string StripComment(string l)
    {
        bool single = false, dbl = false;
        for (int i = 0; i < l.Length; i++)
        {
            char c = l[i];
            if (c == '\'' && !dbl) single = !single;
            else if (c == '"' && !single) dbl = !dbl;
            else if (c == '#' && !single && !dbl && (i == 0 || char.IsWhiteSpace(l[i - 1]))) return l[..i];
        }
        return l;
    }
}
