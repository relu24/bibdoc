using System.Text;
using static BibDoc.Values;

namespace BibDoc;

/// <summary>
/// One widget as a frame drew it: its type, its props, and (for containers)
/// what it holds. <see cref="Key"/> identifies an input or a button across
/// frames: an input by its label (or id), a button by its label and how many
/// buttons with that label came before it.
/// </summary>
public sealed class FrameNode(string type)
{
    public string Type { get; } = type;
    public Dictionary<string, object?> Props { get; } = [];
    public List<FrameNode> Children { get; } = [];
    public string? Key { get; set; }

    public string? Text(string prop) => Props.TryGetValue(prop, out var v) && v is not null ? Str(v) : null;

    /// <summary>This node as JSON: the archived and compared form of a frame. Null props are left out.</summary>
    public void WriteJson(StringBuilder sb)
    {
        sb.Append("{\"type\":");
        Values.WriteJson(sb, Type);
        foreach (var (k, v) in Props)
        {
            if (v is null) continue;
            sb.Append(',');
            Values.WriteJson(sb, k);
            sb.Append(':');
            Values.WriteJson(sb, Clean(v));
        }
        if (Key is not null) { sb.Append(",\"_key\":"); Values.WriteJson(sb, Key); }
        sb.Append(",\"children\":[");
        for (int i = 0; i < Children.Count; i++) { if (i > 0) sb.Append(','); Children[i].WriteJson(sb); }
        sb.Append("]}");
    }

    public static object? Clean(object? v)
    {
        int left = Values.ReadLimit;
        return Clean(v, ref left);
    }

    // Past ReadLimit values, the rest is left as it is: a list that holds itself many times over is not copied out.
    private static object? Clean(object? v, ref int left)
    {
        if (--left < 0) return v;
        switch (v)
        {
            case Dictionary<string, object?> o:
            {
                var clean = new Dictionary<string, object?>();
                foreach (var (k, x) in o) if (x is not null) clean[k] = Clean(x, ref left);
                return clean;
            }
            case List<object?> xs:
            {
                var clean = new List<object?>(xs.Count);
                foreach (var x in xs) clean.Add(Clean(x, ref left));
                return clean;
            }
            default: return v;
        }
    }
}

/// <summary>What a click asked the world to do: call a tool, send a message, or respond with fields.</summary>
public sealed record Effect(string Kind, string? Tool, object? Payload)
{
    public void WriteJson(StringBuilder sb)
    {
        sb.Append("{\"kind\":"); Values.WriteJson(sb, Kind);
        if (Kind == "call") { sb.Append(",\"tool\":"); Values.WriteJson(sb, Tool); sb.Append(",\"args\":"); }
        else if (Kind == "send") sb.Append(",\"text\":");
        else sb.Append(",\"fields\":");
        Values.WriteJson(sb, FrameNode.Clean(Payload));
        sb.Append('}');
    }

    /// <summary>In words, for a person: what the document would have done.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        Values.WriteJson(sb, FrameNode.Clean(Payload));
        return Kind switch
        {
            "call" => $"call {Tool} {sb}",
            "send" => $"send “{Str(Payload)}”",
            _ => $"respond {sb}",
        };
    }
}

/// <summary>A frame: what each block drew, what a click did, and what went wrong where.</summary>
public sealed class FrameResult
{
    public required List<FrameNode>[] Blocks { get; init; }
    public required DocError?[] Errors { get; init; }
    public List<Effect> Effects { get; } = [];
    /// <summary>The states a click's run set, by name: the document changing itself, which the session keeps.</summary>
    public Dictionary<string, object?> Sets { get; } = [];
    public List<string> Warnings { get; } = [];
    /// <summary>The steps the run took, of <see cref="Budget.Limit"/>.</summary>
    public int Steps { get; set; }
    /// <summary>The values the run built and went through, of <see cref="Budget.SizeLimit"/>.</summary>
    public long Size { get; set; }
}

/// <summary>
/// Runs a document's blocks, in order, as one program over one scope: a frame
/// to draw, or (with a click) the effects that click causes.
/// </summary>
public sealed class Interpreter
{
    // name -> positional props, in order
    public static readonly Dictionary<string, string[]> Signatures = new()
    {
        ["text"] = ["text"], ["heading"] = ["text"], ["markdown"] = ["text"], ["badge"] = ["text"],
        ["card"] = ["title"], ["section"] = ["title"], ["row"] = [], ["column"] = [], ["divider"] = [],
        ["stat"] = ["label", "value"], ["progress"] = ["label", "value"], ["kv"] = ["items"],
        ["table"] = ["columns", "rows"], ["list"] = ["label"], ["item"] = ["id", "title"],
        ["button"] = ["label"], ["choice"] = ["label", "options"], ["slider"] = ["label", "min", "max"],
        ["input"] = ["label"], ["toggle"] = ["label"], ["checklist"] = ["label", "options"],
        ["grid"] = [], ["steps"] = [], ["step"] = ["n", "title"], ["chips"] = ["items"],
        ["tabs"] = ["label"], ["tab"] = ["title"],
        // charts (docs/charts.md)
        ["plot"] = [], ["series"] = ["label", "xs", "ys"], ["dots"] = ["label", "xs", "ys"], ["rule"] = [],
        ["point"] = ["x", "y"], ["bars"] = ["items"],
        // primitives (docs/two-layers.md)
        ["box"] = [], ["layer"] = [], ["draw"] = ["width", "height"],
        ["line"] = ["x1", "y1", "x2", "y2"], ["rect"] = ["x", "y", "w", "h"], ["circle"] = ["cx", "cy", "r"], ["label"] = ["x", "y", "text"],
    };

    public static readonly Dictionary<string, HashSet<string>> Props = new()
    {
        ["text"] = ["text", "tone", "size", "weight", "face", "color", "wrap", "align", "caps", "width"], ["heading"] = ["text"], ["markdown"] = ["text"], ["badge"] = ["text", "tone"],
        ["card"] = ["title", "subtitle", "icon", "tone", "badge", "badgeTone", "footer"], ["section"] = ["title"],
        ["row"] = ["gap", "fit"], ["column"] = ["gap"], ["divider"] = [],
        ["grid"] = ["columns", "gap"], ["steps"] = [], ["step"] = ["n", "title", "status", "tone", "who", "phase"],
        ["chips"] = ["items", "tone"],
        ["box"] = ["dir", "gap", "padding", "width", "height", "align", "justify", "wrap", "background", "border",
            "borderColor", "radius", "opacity", "shadow", "hover", "at", "scroll", "id", "component", "columns"],
        ["layer"] = [], ["draw"] = ["width", "height"],
        ["line"] = ["x1", "y1", "x2", "y2", "color", "width"],
        ["rect"] = ["x", "y", "w", "h", "fill", "color", "width", "radius"],
        ["circle"] = ["cx", "cy", "r", "fill", "color", "width"],
        ["label"] = ["x", "y", "text", "size", "color", "align", "weight"],
        ["stat"] = ["label", "value", "unit", "tone"], ["progress"] = ["label", "value", "max"],
        ["kv"] = ["items"], ["table"] = ["columns", "rows", "widths"],
        ["list"] = ["id", "label", "selectable"], ["item"] = ["id", "title", "subtitle", "badge", "tone"],
        ["button"] = ["label", "style", "disabled"],
        ["choice"] = ["id", "label", "options", "value", "style"],
        ["tabs"] = ["id", "label", "value"], ["tab"] = ["title"],
        ["plot"] = ["x", "y", "height", "xLabel", "yLabel", "log"],
        ["series"] = ["label", "xs", "ys", "tone", "fill", "width"], ["dots"] = ["label", "xs", "ys", "tone", "r"],
        ["rule"] = ["x", "y", "label", "tone"], ["point"] = ["x", "y", "label", "tone"],
        ["bars"] = ["items", "unit", "max", "dir", "height"], ["slider"] = ["id", "label", "min", "max", "value", "step"],
        ["input"] = ["id", "label", "value", "multiline", "placeholder"], ["toggle"] = ["id", "label", "value"],
        ["checklist"] = ["id", "label", "options", "value"],
    };

    public static readonly HashSet<string> Containers = ["card", "section", "row", "column", "list", "grid", "steps", "step", "box", "layer", "draw", "tabs", "tab", "plot"];
    public static readonly HashSet<string> Inputs = ["choice", "slider", "input", "toggle", "checklist"];
    // Names a document writes as values: tones and colour roles, and the style table's words.
    private static readonly string[] Names =
    [
        "neutral", "muted", "success", "warning", "danger", "accent", "primary", "ghost",
        "ink", "faint", "surface", "raised", "border", "start", "center", "end",
        "between", "fill", "fit", "body", "display", "mono", "bold", "italic", "regular",
        "low", "high", "sm", "md", "lg", "xl", "segmented",
    ];

    private readonly IReadOnlyList<IReadOnlyList<Stmt>?> _blocks;
    private readonly IReadOnlyList<string> _sources;
    private readonly DocError?[] _parseErrors;

    private readonly Budget _budget = new();
    private readonly Evaluator _ev;

    // per run
    private IReadOnlyDictionary<string, object?> _state = new Dictionary<string, object?>();
    private string? _event;
    private FrameResult _result = null!;
    private List<List<FrameNode>> _stack = [];
    private readonly List<Tabbed> _tabs = [];   // the tabs being drawn, innermost last
    private int _plots;                          // plots being drawn: what goes in one needs one around it
    public const int MaxPoints = 2000;           // a series' points, at most

    // A tabs container while its block runs: the title of the tab that shows.
    private sealed class Tabbed { public string? Active; }
    private readonly Dictionary<string, int> _occurrences = [];
    private readonly Dictionary<string, string> _inputs = [];   // this run's input keys, and the label each came from
    private readonly HashSet<string> _calling = [];
    // The block each running fn of the document's was called with (null for a plain call), and
    // the one about to be handed to the next: slot() draws the innermost.
    private readonly List<(IReadOnlyList<Stmt> Block, Scope Scope)?> _slots = [];
    private (IReadOnlyList<Stmt> Block, Scope Scope)? _given;
    private string _source = "";

    // A host that lets nothing out of a document: call, send and respond are errors.
    private readonly bool _contained;

    /// <summary>
    /// A document's blocks, parsed. A block that does not parse keeps its error and draws nothing.
    /// <paramref name="contained"/>: nothing the document does reaches outside it, so a click
    /// can only set() its own state.
    /// </summary>
    public Interpreter(IReadOnlyList<string> sources, bool contained = false)
    {
        _contained = contained;
        _ev = new Evaluator(_budget);
        _sources = sources;
        var blocks = new List<IReadOnlyList<Stmt>?>();
        _parseErrors = new DocError?[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            try { blocks.Add(Parser.ParseProgram(sources[i])); }
            catch (DocError e) { blocks.Add(null); _parseErrors[i] = e; }
        }
        _blocks = blocks;
    }

    public int BlockCount => _blocks.Count;

    /// <summary>The effects that reach outside a document.</summary>
    public static readonly string[] Outward = ["call", "send", "respond"];

    /// <summary>
    /// Where a document reaches outside itself, found without running it: each call of
    /// call(), send() or respond(), as "block N, line L: name()". Empty for a document a
    /// contained host can draw; an error in a block that does not run would otherwise
    /// only show once a reader clicked.
    /// </summary>
    public static List<string> ReachesOutside(IReadOnlyList<string> sources)
    {
        var found = new List<string>();
        for (int b = 0; b < sources.Count; b++)
        {
            List<Tok> toks;
            try { toks = Lexer.Tokenize(sources[b]); }
            catch (DocError) { continue; }
            for (int i = 0; i + 1 < toks.Count; i++)
            {
                if (toks[i].Kind != TokKind.Name || !Outward.Contains(toks[i].Value) || !toks[i + 1].Is("(")) continue;
                if (i > 0 && (toks[i - 1].Is(".") || toks[i - 1].Is("?."))) continue;   // a record's field, not the effect
                found.Add($"block {b + 1}, {Lexer.LineCol(sources[b], toks[i].Pos)}: {toks[i].Value}()");
            }
        }
        return found;
    }

    public static string Norm(object? s)
    {
        var sb = new StringBuilder();
        foreach (char c in Str(s).ToLowerInvariant()) if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Runs the whole document once. With <paramref name="clicked"/> (a button's
    /// key) this is the click's run: that button returns clicked, and the effects
    /// it reaches are recorded. Without, effects are an error.
    /// </summary>
    public FrameResult Run(object? data, IReadOnlyDictionary<string, object?> state, string? clicked = null)
    {
        _budget.Reset();
        _state = state;
        _event = clicked;
        _occurrences.Clear();
        _inputs.Clear();
        _tabs.Clear();
        _plots = 0;
        _calling.Clear();
        _slots.Clear();
        _given = null;
        _result = new FrameResult
        {
            Blocks = Enumerable.Range(0, _blocks.Count).Select(_ => new List<FrameNode>()).ToArray(),
            Errors = (DocError?[])_parseErrors.Clone(),
        };
        var root = new Scope(null, _ev.Builtins());
        var vars = new Dictionary<string, object?> { ["data"] = data };
        foreach (var (k, v) in Widgets()) vars[k] = v;
        var scope = root.Child(vars);
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i] is not { } stmts) continue;
            _stack = [_result.Blocks[i]];
            _source = _sources[i];
            // A block that fails gives back the steps and size it spent: the next one starts
            // with what this one started with, so a runaway block fails alone.
            var mark = _budget.Mark();
            try { Block(stmts, scope); }
            catch (DocError e) { _result.Errors[i] = e; _budget.Restore(mark); }
            catch (ReturnSignal) { _result.Errors[i] = new DocError("return outside fn"); _budget.Restore(mark); }
        }
        _result.Steps = _budget.Used;
        _result.Size = _budget.Sized;
        return _result;
    }

    // ── widgets, as functions the evaluator can call ──

    private Dictionary<string, object?> Widgets()
    {
        var env = new Dictionary<string, object?>();
        foreach (var t in Signatures.Keys)
        {
            string type = t;
            env[type] = new NamedBuiltin((args, named) => Draw(type, args, named, null));
        }
        env["call"] = new NamedBuiltin((args, named) =>
        {
            NeedEvent("call");
            Contained("call");
            object? payload = args.Count > 1 ? args[1] : named.ToDictionary(kv => kv.Key, kv => kv.Value);
            _result.Effects.Add(new Effect("call", Str(args.ElementAtOrDefault(0)), payload));
            return null;
        });
        env["send"] = new NamedBuiltin((args, _) =>
        {
            NeedEvent("send");
            Contained("send");
            _result.Effects.Add(new Effect("send", null, Str(args.ElementAtOrDefault(0))));
            return null;
        });
        env["respond"] = new NamedBuiltin((args, named) =>
        {
            NeedEvent("respond");
            Contained("respond");
            object? payload = args.Count > 0 ? args[0] : named.ToDictionary(kv => kv.Key, kv => kv.Value);
            _result.Effects.Add(new Effect("respond", null, payload));
            return null;
        });
        // State (docs/two-layers.md): named values the document keeps, set only by an event.
        // An input's value is the state named by its key, so value() reads one not drawn.
        env["state"] = new Builtin(args =>
            _state.TryGetValue(Norm(args.ElementAtOrDefault(0)), out var v) ? v : args.ElementAtOrDefault(1), 2);
        env["value"] = new Builtin(args => _state.GetValueOrDefault(Norm(args.ElementAtOrDefault(0))));
        env["set"] = new Builtin(args =>
        {
            NeedEvent("set");
            _result.Sets[Norm(args.ElementAtOrDefault(0))] = args.ElementAtOrDefault(1);
            return null;
        }, 2);
        foreach (var n in Names) env[n] = n;
        env["slot"] = new NamedBuiltin((args, _) =>
        {
            if (_slots.Count == 0 || _slots[^1] is not { } slot) return null;
            _slots.Add(null);   // the caller's block runs as the caller: no slot of its own
            try { Block(slot.Block, slot.Scope.Child(new() { ["it"] = args.ElementAtOrDefault(0) })); }
            finally { _slots.RemoveAt(_slots.Count - 1); }
            return null;
        });
        return env;
    }

    private void NeedEvent(string kind)
    {
        if (_event is null)
            throw new EffectOutsideEvent($"{kind}() ran while drawing; effects belong inside `if button(...).clicked {{ }}`");
    }

    private void Contained(string kind)
    {
        if (_contained)
            throw new DocError($"{kind}() reaches outside the document, and this one may not; a button here can set() the document's own state");
    }

    // A series' xs and ys as numbers, as many as both have (and MaxPoints at most). A point that
    // is not a number (NaN, infinite) is kept: the renderer breaks the line there.
    private void Points(FrameNode node)
    {
        if (node.Props.GetValueOrDefault("xs") is not List<object?> xs || node.Props.GetValueOrDefault("ys") is not List<object?> ys)
            throw new DocError($"{node.Type}() takes two lists of numbers, xs and ys");
        int n = Math.Min(xs.Count, ys.Count);
        if (xs.Count != ys.Count)
            _result.Warnings.Add($"{node.Type}: xs has {xs.Count} numbers and ys {ys.Count}: {n} points drawn");
        if (n > MaxPoints)
        {
            _result.Warnings.Add($"{node.Type}: {n} points, {MaxPoints} drawn");
            n = MaxPoints;
        }
        _budget.Size(2L * n);
        node.Props["xs"] = xs.Take(n).Select(x => (object?)Num(x)).ToList();
        node.Props["ys"] = ys.Take(n).Select(y => (object?)Num(y)).ToList();
    }

    private static object? InputDefault(string type, Dictionary<string, object?> props)
    {
        if (props.TryGetValue("value", out var v) && v is not null) return v;
        return type switch
        {
            "choice" => props.GetValueOrDefault("options") is List<object?> { Count: > 0 } opts ? opts[0] : null,
            "slider" => props.TryGetValue("min", out var min) ? min : 0d,
            "input" => "",
            "toggle" => false,
            "checklist" => new List<object?>(),
            _ => null,
        };
    }

    private object? Draw(string type, IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?> named, Action? block)
    {
        var names = Signatures[type];
        if (args.Count > names.Length)
            throw new DocError($"{type}() takes {names.Length} positional argument(s): {(names.Length == 0 ? "none" : string.Join(", ", names))}");
        var node = new FrameNode(type);
        for (int i = 0; i < args.Count; i++) node.Props[names[i]] = args[i];
        foreach (var (k, v) in named)
        {
            if (!Props[type].Contains(k)) _result.Warnings.Add($"{type}: unknown argument '{k}'");
            node.Props[k] = v;
        }

        object? ret = null;
        if (Inputs.Contains(type) || type is "list" or "tabs")
        {
            // Labels are told apart without case, spaces or punctuation: two that differ only so are one input.
            string called = Str(node.Props.GetValueOrDefault("id") ?? node.Props.GetValueOrDefault("label"));
            string key = Norm(called);
            if (_inputs.TryGetValue(key, out var first))
                _result.Warnings.Add($"{type}: '{called}' is the same input as '{first}' (labels are compared without case, spaces or punctuation): give one an id:");
            else _inputs[key] = called;
        }
        if (Inputs.Contains(type))
        {
            node.Key = Norm(node.Props.GetValueOrDefault("id") ?? node.Props.GetValueOrDefault("label"));
            var value = _state.TryGetValue(node.Key, out var st) ? st : InputDefault(type, node.Props);
            node.Props["value"] = value;
            ret = value;
        }
        else if (type == "list")
        {
            node.Key = Norm(node.Props.GetValueOrDefault("id") ?? node.Props.GetValueOrDefault("label"));
            var selected = _state.TryGetValue(node.Key, out var st) ? st : new List<object?>();
            node.Props["selected"] = selected;
            ret = new Dictionary<string, object?> { ["selected"] = selected };
        }
        else if (type == "button")
        {
            string k = Norm(node.Props.GetValueOrDefault("label"));
            int n = _occurrences.GetValueOrDefault(k);
            _occurrences[k] = n + 1;
            node.Key = $"{k}#{n}";
            ret = new Dictionary<string, object?>
            {
                ["clicked"] = _event == node.Key && !Truthy(node.Props.GetValueOrDefault("disabled")),
            };
        }

        // Charts (docs/charts.md): their marks go in a plot, a series' points are numbers.
        if (type is "series" or "dots" or "rule" or "point" && _plots == 0)
            throw new DocError($"{type}() goes inside plot {{ }}");
        if (type is "series" or "dots") Points(node);
        if (type == "rule" && node.Props.GetValueOrDefault("x") is null && node.Props.GetValueOrDefault("y") is null)
            throw new DocError("rule() takes x: or y:");
        if (type == "bars")
        {
            if (node.Props.GetValueOrDefault("items") is not List<object?> bars)
                throw new DocError("bars() takes a list of {label, value}");
            if (bars.FirstOrDefault(b => b is not Dictionary<string, object?> r || !r.ContainsKey("value")) is { } bad)
                throw new DocError($"bars(): each item is a {{label, value}} record, not {Str(bad)}");
        }

        // Tabs are an input whose value is the title of the tab that shows (the first, until one is
        // picked); only that tab's block runs.
        Tabbed? tabbed = null;
        bool runs = true;
        if (type == "tabs")
        {
            node.Key = Norm(node.Props.GetValueOrDefault("id") ?? node.Props.GetValueOrDefault("label"));
            tabbed = new Tabbed { Active = _state.TryGetValue(node.Key, out var st) ? Str(st) : node.Props.GetValueOrDefault("value") is { } v ? Str(v) : null };
        }
        else if (type == "tab")
        {
            if (_tabs.Count == 0) throw new DocError("tab() goes inside tabs { }");
            var within = _tabs[^1];
            string title = Str(node.Props.GetValueOrDefault("title"));
            within.Active ??= title;
            runs = title == within.Active;
            node.Props["active"] = runs;
        }

        _stack[^1].Add(node);
        if (block is not null)
        {
            if (!Containers.Contains(type)) throw new DocError($"{type}() does not take a {{ block }}");
            if (tabbed is not null) _tabs.Add(tabbed);
            if (type == "plot") _plots++;
            _stack.Add(node.Children);
            try { if (runs) block(); }
            finally
            {
                _stack.RemoveAt(_stack.Count - 1);
                if (tabbed is not null) _tabs.RemoveAt(_tabs.Count - 1);
                if (type == "plot") _plots--;
            }
        }
        if (tabbed is not null)
        {
            node.Props["value"] = tabbed.Active;
            ret = tabbed.Active;
        }
        if (type == "list" && ret is Dictionary<string, object?> r)
            r["count"] = (double)node.Children.Count(c => c.Type == "item");
        return ret;
    }

    // `card("x") { … }` : the call's arguments, then its block inside it. A fn of the
    // document's given a block draws it where it says slot().
    private object? WithBlock(Expr e, IReadOnlyList<Stmt> block, Scope s)
    {
        if (e is NameRef nr) e = new CallExpr(nr, [], []);
        if (e is CallExpr { Callee: NameRef { Name: var fnName } } fnCall && s.Has(fnName) && s.Lookup(fnName) is DocFn fn)
        {
            var fnArgs = fnCall.Args.Select(a => _ev.Eval(a, s)).ToList();
            var fnNamed = new Dictionary<string, object?>();
            foreach (var (k, v) in fnCall.Named) fnNamed[k] = _ev.Eval(v, s);
            _given = (block, s);
            try { return fn.Invoke(fnArgs, fnNamed); }
            finally { _given = null; }
        }
        if (e is not CallExpr { Callee: NameRef { Name: var name } } call || !Containers.Contains(name))
            throw new DocError("only a container or a fn of the document's takes a { block }");
        var args = call.Args.Select(a => _ev.Eval(a, s)).ToList();
        var named = new Dictionary<string, object?>();
        foreach (var (k, v) in call.Named) named[k] = _ev.Eval(v, s);
        // In a layer, w and h are the box's size, known when it is drawn.
        var inner = name == "layer" ? s.Child(new() { ["w"] = new Dim("w"), ["h"] = new Dim("h") }) : s.Child();
        return Draw(name, args, named, () => Block(block, inner));
    }

    /// <summary>A fn of the document's: positional and named arguments, defaults, and a slot.</summary>
    private sealed class DocFn(Interpreter it, FnStmt fn, Scope scope) : Callable
    {
        public override bool TakesNamed => true;

        public override object? Invoke(IReadOnlyList<object?> args, IReadOnlyDictionary<string, object?>? named)
        {
            if (!it._calling.Add(fn.Name)) throw new DocError($"{fn.Name}() calls itself; documents may not recurse");
            var vars = new Dictionary<string, object?>();
            for (int i = 0; i < fn.Params.Count; i++)
            {
                var (p, fallback) = fn.Params[i];
                vars[p] = i < args.Count ? args[i]
                    : named is not null && named.TryGetValue(p, out var v) ? v
                    : fallback is not null ? it._ev.Eval(fallback, scope) : null;
            }
            if (named is not null)
                foreach (var key in named.Keys)
                    if (!fn.Params.Any(p => p.Name == key)) { it._calling.Remove(fn.Name); throw new DocError($"{fn.Name}() has no parameter '{key}'"); }
            it._slots.Add(it._given);
            it._given = null;
            try
            {
                it.Block(fn.Body, scope.Child(vars));
                return null;
            }
            catch (ReturnSignal r) { return r.Value; }
            finally
            {
                it._slots.RemoveAt(it._slots.Count - 1);
                it._calling.Remove(fn.Name);
            }
        }
    }

    private sealed class ReturnSignal(object? value) : Exception
    {
        public object? Value { get; } = value;
    }

    private void Block(IReadOnlyList<Stmt> stmts, Scope s)
    {
        foreach (var st in stmts)
        {
            _budget.Spend();
            try
            {
                switch (st)
                {
                    case LetStmt l:
                        s.Vars[l.Name] = l.Block is { } lb ? WithBlock(l.Value, lb, s) : _ev.Eval(l.Value, s);
                        break;
                    case DoStmt d:
                        if (d.Block is { } db) WithBlock(d.Value, db, s);
                        else _ev.Eval(d.Value, s);
                        break;
                    case IfStmt i:
                        if (Truthy(_ev.Eval(i.Test, s))) Block(i.Then, s.Child());
                        else if (i.Else is { } other) Block(other, s.Child());
                        break;
                    case ForStmt f:
                    {
                        if (_ev.Eval(f.List, s) is not List<object?> xs)
                            throw new DocError($"for: '{Str(_ev.Eval(f.List, s))}' is not a list");
                        for (int k = 0; k < xs.Count; k++)
                        {
                            var cs = s.Child(new() { [f.Name] = xs[k], ["index"] = (double)k });
                            if (f.Where is { } w && !Truthy(_ev.Eval(w, cs))) continue;
                            Block(f.Body, cs);
                        }
                        break;
                    }
                    case ReturnStmt r:
                        throw new ReturnSignal(r.Value is null ? null : _ev.Eval(r.Value, s));
                    case SetStmt set:
                    {
                        var scope = s;
                        while (scope is not null && !scope.Vars.ContainsKey(set.Name)) scope = scope.Parent;
                        if (scope is null) throw new DocError($"{set.Name} = … needs a `let {set.Name}` first");
                        scope.Vars[set.Name] = _ev.Eval(set.Value, s);
                        break;
                    }
                    case FnStmt fn:
                        s.Vars[fn.Name] = new DocFn(this, fn, s);
                        break;
                }
            }
            catch (DocError e) when (!e.Message.Contains(" at line ", StringComparison.Ordinal))
            {
                string message = $"{e.Message} at {Lexer.LineCol(_source, st.Pos)}";
                throw e is EffectOutsideEvent ? new EffectOutsideEvent(message) : new DocError(message);
            }
        }
    }
}
