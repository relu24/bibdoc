namespace BibDoc;

public enum TokKind { Newline, Number, String, Template, Op, Name, End }

public readonly record struct Tok(TokKind Kind, string Value, int Pos)
{
    public bool Is(string value) => Kind is TokKind.Op or TokKind.Name && Value == value;
}

/// <summary>
/// Splits a block into tokens. Strings may hold quotes inside a {expr}; a
/// newline inside an expression (after an operator, or before a line that
/// starts with one) is not the end of a statement.
/// </summary>
public static class Lexer
{
    private static readonly string[] Ops =
        ["...", "===", "!==", "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?."];
    private const string SingleOps = "-+*/%<>!?:.,()[]{}=;";

    private static readonly HashSet<string> After =
        ["+", "-", "*", "/", "%", "==", "!=", "===", "!==", "<", "<=", ">", ">=", "&&", "||", "??", "?", ":", "=>", ",", "=", "(", "[", "."];
    private static readonly HashSet<string> Before =
        [".", "?.", "?", ":", "&&", "||", "??", "+", "*", "/", "%", "==", "!=", "===", "!==", "<=", ">=", ")", "]", "=>"];

    public static List<Tok> Tokenize(string src, bool keepNewlines = false)
    {
        var toks = new List<Tok>();
        int i = 0;
        while (i < src.Length)
        {
            char c = src[i];
            if (c == '\n')
            {
                if (keepNewlines) toks.Add(new(TokKind.Newline, "\n", i));
                i++;
                continue;
            }
            if (c is ' ' or '\t' or '\r') { i++; continue; }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new DocError($"unclosed comment at {LineCol(src, i)}");
                i = end + 2;
                continue;
            }
            if (char.IsAsciiDigit(c))
            {
                int j = i;
                while (j < src.Length && char.IsAsciiDigit(src[j])) j++;
                if (j + 1 < src.Length && src[j] == '.' && char.IsAsciiDigit(src[j + 1]))
                {
                    j++;
                    while (j < src.Length && char.IsAsciiDigit(src[j])) j++;
                }
                // An exponent: 3.156e7, 1e-9.
                int e = j < src.Length && src[j] is 'e' or 'E' ? j + 1 : -1;
                if (e > 0 && e < src.Length && src[e] is '+' or '-') e++;
                if (e > 0 && e < src.Length && char.IsAsciiDigit(src[e]))
                {
                    j = e;
                    while (j < src.Length && char.IsAsciiDigit(src[j])) j++;
                }
                toks.Add(new(TokKind.Number, src[i..j], i));
                i = j;
                continue;
            }
            if (c is '"' or '\'')
            {
                int end = ScanString(src, i);
                if (end < 0) throw new DocError($"unclosed string at {LineCol(src, i)}");
                toks.Add(new(TokKind.String, src[i..end], i));
                i = end;
                continue;
            }
            if (c == '`')
            {
                int j = i + 1;
                while (j < src.Length && src[j] != '`') j += src[j] == '\\' ? 2 : 1;
                if (j >= src.Length) throw new DocError($"unclosed string at {LineCol(src, i)}");
                toks.Add(new(TokKind.Template, src[i..(j + 1)], i));
                i = j + 1;
                continue;
            }
            if (char.IsAsciiLetter(c) || c is '_' or '$')
            {
                int j = i + 1;
                while (j < src.Length && (char.IsAsciiLetterOrDigit(src[j]) || src[j] is '_' or '$')) j++;
                toks.Add(new(TokKind.Name, src[i..j], i));
                i = j;
                continue;
            }
            string? op = null;
            foreach (var o in Ops)
                if (string.CompareOrdinal(src, i, o, 0, o.Length) == 0) { op = o; break; }
            if (op is null && SingleOps.Contains(c)) op = c.ToString();
            if (op is null) throw new DocError($"unexpected character '{c}' at {LineCol(src, i)}");
            toks.Add(new(TokKind.Op, op, i));
            i += op.Length;
        }
        toks.Add(new(TokKind.End, "", src.Length));
        return keepNewlines ? ContinueLines(toks) : toks;
    }

    /// <summary>
    /// The end of the string that starts at <paramref name="pos"/>, or -1. Quotes
    /// inside a {expr} do not end it, so "Export {s == "all" ? 1 : 2}" is one string.
    /// </summary>
    public static int ScanString(string src, int pos)
    {
        char q = src[pos];
        int i = pos + 1, depth = 0;
        while (i < src.Length)
        {
            char c = src[i];
            if (c == '\\') { i += 2; continue; }
            if (depth == 0 && c == q) return i + 1;
            if (c == '\n' && depth == 0) break;
            if (c == '{') depth++;
            else if (c == '}' && depth > 0) depth--;
            else if (depth > 0 && c is '"' or '\'')
            {
                int j = ScanString(src, i);
                if (j < 0) break;
                i = j;
                continue;
            }
            i++;
        }
        // The plain rule: up to the next quote on the line.
        int k = src.IndexOf(q, pos + 1);
        return k > 0 && src.IndexOf('\n', pos, k - pos) < 0 ? k + 1 : -1;
    }

    // A newline inside an expression is not the end of a statement.
    private static List<Tok> ContinueLines(List<Tok> toks)
    {
        var output = new List<Tok>(toks.Count);
        for (int i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            if (t.Kind == TokKind.Newline)
            {
                Tok? prev = null;
                for (int k = output.Count - 1; k >= 0; k--) if (output[k].Kind != TokKind.Newline) { prev = output[k]; break; }
                Tok? next = null;
                for (int k = i + 1; k < toks.Count; k++) if (toks[k].Kind != TokKind.Newline) { next = toks[k]; break; }
                if (prev is { Kind: TokKind.Op } p && After.Contains(p.Value)) continue;
                if (next is { Kind: TokKind.Op } n && Before.Contains(n.Value)) continue;
            }
            output.Add(t);
        }
        return output;
    }

    public static string LineCol(string src, int pos)
    {
        pos = Math.Clamp(pos, 0, src.Length);
        int line = 1, lineStart = 0;
        for (int i = 0; i < pos; i++) if (src[i] == '\n') { line++; lineStart = i + 1; }
        return $"line {line}:{pos - lineStart + 1}";
    }
}
