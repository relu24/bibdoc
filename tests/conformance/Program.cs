// The bib document language, headless. Every case in cases/ is a reference
// document from the language benchmark, driven by its task's checks through
// the Python reference interpreter: each frame, input change and click, with
// the effects it caused. Replaying the steps here must give the same frames
// and effects. The checks after that cover what the cases do not reach.
using System.Text;
using System.Text.Json.Nodes;
using BibDoc;

int failed = 0, passed = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { passed++; return; }
    failed++;
    Console.WriteLine($"FAIL {name}{(detail.Length > 0 ? ": " + detail : "")}");
}

string casesDir = Path.Combine(AppContext.BaseDirectory, "cases");
if (!Directory.Exists(casesDir)) casesDir = Path.Combine(Directory.GetCurrentDirectory(), "cases");
int caseCount = 0;
foreach (var file in Directory.GetFiles(casesDir, "*.json").Order(StringComparer.Ordinal))
{
    caseCount++;
    var c = JsonNode.Parse(File.ReadAllText(file))!;
    string name = c["name"]!.GetValue<string>();
    var session = new DocSession([c["source"]!.GetValue<string>()], FromJson(c["data"]));
    int n = 0;
    foreach (var step in c["steps"]!.AsArray())
    {
        n++;
        string op = step!["op"]!.GetValue<string>();
        string at = $"{name} step {n} ({op})";
        switch (op)
        {
            case "session":
                session = new DocSession([c["source"]!.GetValue<string>()], FromJson(step["data"]));
                break;
            case "frame":
            {
                var frame = session.Frame();
                if (frame.Errors[0] is { } e) { Check(at, false, e.Message); break; }
                var sb = new StringBuilder("{\"type\":\"root\",\"children\":[");
                for (int i = 0; i < frame.Blocks[0].Count; i++) { if (i > 0) sb.Append(','); frame.Blocks[0][i].WriteJson(sb); }
                sb.Append("]}");
                var actual = JsonNode.Parse(sb.ToString());
                Check(at, Same(step["frame"], actual, out var why), why);
                break;
            }
            case "set":
                session.Set(step["key"]!.GetValue<string>(), FromJson(step["value"]));
                break;
            case "click":
            {
                var result = session.Click(step["key"]!.GetValue<string>());
                if (result.Errors[0] is { } e) { Check(at, false, e.Message); break; }
                var sb = new StringBuilder("[");
                for (int i = 0; i < result.Effects.Count; i++) { if (i > 0) sb.Append(','); result.Effects[i].WriteJson(sb); }
                sb.Append(']');
                Check(at, Same(step["effects"], JsonNode.Parse(sb.ToString()), out var why), why);
                break;
            }
        }
    }
}
Check("cases found", caseCount > 0, casesDir);

// ── rules the cases do not reach ─────────────────────────────────────────────

string? ErrorOf(string src, object? data = null) => new DocSession([src], data).Frame().Errors[0]?.Message;
FrameNode? First(string src, object? data = null) => new DocSession([src], data).Frame().Blocks[0].FirstOrDefault();

Check("an effect while drawing is an error",
    ErrorOf("send(\"hi\")")?.Contains("ran while drawing") == true, ErrorOf("send(\"hi\")") ?? "no error");
Check("push names the alternative",
    ErrorOf("let xs = []\nxs.push(1)")?.Contains("[...xs, x]") == true, ErrorOf("let xs = []\nxs.push(1)") ?? "no error");
Check("recursion is refused",
    ErrorOf("fn f(n) { return f(n) }\ntext(f(1))")?.Contains("may not recurse") == true, ErrorOf("fn f(n) { return f(n) }\ntext(f(1))") ?? "no error");
Check("a lambda that calls itself is refused, not a crash",
    ErrorOf("let f = x => f(x + 1)\nstat(\"n\", f(1))")?.Contains("f() calls itself") == true, ErrorOf("let f = x => f(x + 1)\nstat(\"n\", f(1))") ?? "no error");
Check("a lambda called again after it returned is fine",
    First("let inc = x => x + 1\nstat(\"n\", inc(inc(inc(1))) + [1, 2].map(inc).length)")?.Props["value"] is 6d);
Check("a program that does not stop runs out of steps",
    ErrorOf("let xs = [1,2,3,4,5,6,7,8,9,10]\nfor a in xs { for b in xs { for c in xs { for d in xs { for e in xs { for f in xs { let x = a } } } } } }")?.Contains("budget") == true);
// A document small in steps can still build something huge: each of these would take
// gigabytes, or hours, without the size budget.
const string Doubled = "let d = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30]\n";
foreach (var (what, src) in new[]
{
    ("a string doubled", Doubled + "let s = d.reduce((acc, k) => acc + acc, \"xxxxxxxx\")\ntext(\"{s.length}\")"),
    ("a template doubled", Doubled + "let s = d.reduce((acc, k) => \"{acc}{acc}\", \"xxxxxxxx\")\ntext(\"{s.length}\")"),
    ("a list spread into itself", Doubled + "let s = d.reduce((acc, k) => [...acc, ...acc], [1, 2, 3, 4])\ntext(\"{s.length}\")"),
    ("a list concatenated to itself", Doubled + "let s = d.reduce((acc, k) => acc.concat(acc), [1, 2, 3, 4])\ntext(\"{s.length}\")"),
    ("a big list flatMapped", Doubled + "let big = d.reduce((acc, k) => acc.length > 100000 ? acc : [...acc, ...acc], [1])\nlet s = d.flatMap(_ => big).flatMap(_ => big)\ntext(\"{s.length}\")"),
    ("a big list searched in a loop", Doubled + "let big = d.reduce((acc, k) => acc.length > 100000 ? acc : [...acc, ...acc], [1])\nfor i in d { for j in d { let x = big.includes(2) } }"),
})
    Check($"{what} runs out of room, and does not take the machine with it",
        ErrorOf(src)?.Contains("size budget") == true, ErrorOf(src) ?? "no error");
// Shared lists: [n, n] again and again is 2^30 items as text, but only 60 in memory.
const string Nested = Doubled + "let n = d.reduce((acc, k) => [acc, acc], [1])\n";
Check("a list that holds itself many times over is written out cut short",
    First(Nested + "text(\"{n}\")")?.Text("text") is { } cut && cut.EndsWith('…') && cut.Length <= Values.ReadLimit + 1);
Check("comparing two such lists runs out of room",
    ErrorOf(Nested + "let m = d.reduce((acc, k) => [acc, acc], [1])\ntext(\"{n == m}\")")?.Contains("size budget") == true);
Check("toFixed takes at most 100 digits", First("text((1).toFixed(1000000000))")?.Text("text")?.Length == 102);
Check("unique keeps the first of each, lists and records too",
    First("text(\"{unique([1, \"1\", 1, [1], [1], {a: 1}, {a: 1}, null, null, true])}\")")?.Text("text") is "1,1,1,{'a': 1},null,true");
Check("errors name the line", ErrorOf("text(\"a\")\ntext(nope)")?.Contains("line 2") == true, ErrorOf("text(\"a\")\ntext(nope)") ?? "");
Check("Math has the functions of JavaScript's",
    First("stat(\"x\", round(Math.sin(Math.PI / 2) + Math.sqrt(16) + Math.pow(2, 3) + Math.log(Math.exp(1)) + Math.atan2(0, 1), 6))")?.Props["value"] is 14d);
Check("two inputs of one key are a warning",
    new DocSession(["slider(\"w(AA)\", 0, 2)\nslider(\"w(aa)\", 0, 2)\nslider(\"w(BB)\", 0, 2)"], null).Frame().Warnings
        is [var dup] && dup.Contains("'w(aa)' is the same input as 'w(AA)'"));
Check("numbers may have an exponent", First("stat(\"x\", 3.156e7 + 2E-1 + 1e+2)")?.Props["value"] is 31560100.2d);
Check("split on nothing gives the letters", First("chips(\"ABC\".split(\"\"))")?.Props["items"] is List<object?> { Count: 3 });
Check("flatMap gives the index too", First("chips([5, 6].flatMap((n, j) => [j, n]))")?.Props["items"] is List<object?> { Count: 4 } fm && fm[2] is 1d);
Check("w in min is an error, not a shape that is not drawn",
    ErrorOf("box(width: 100, height: 50) { layer { circle(w / 2, 20, min([w, 10])) } }")?.Contains("w and h are only known") == true);
string? TextOf(string src) => First(src)?.Props["text"] as string;
Check("fmt groups thousands and keeps two decimals at most", TextOf("text(fmt(1234567.5))") == "1,234,567.5" && TextOf("text(fmt(0.125))") == "0.13" && TextOf("text(fmt(-3))") == "-3", TextOf("text(fmt(1234567.5))") ?? "");
Check("fmt with digits keeps them", TextOf("text(fmt(2, 2))") == "2.00" && TextOf("text(fmt(1999.996, 2))") == "2,000.00");
Check("fmt compact and sign", TextOf("text(fmt(1000, compact: true))") == "1k" && TextOf("text(fmt(3456789, compact: true))") == "3.5M"
    && TextOf("text(fmt(5, sign: true))") == "+5" && TextOf("text(fmt(0, sign: true))") == "0", TextOf("text(fmt(3456789, compact: true))") ?? "");
Check("a choice takes style: segmented", First("choice(\"Shape\", [\"a\", \"b\"], style: segmented)")?.Props["style"] as string == "segmented");
{
    const string tabsSrc = "let v = tabs(\"View\") {\n  tab(\"Chart\") { text(\"chart\") }\n  tab(\"Table\") { text(\"table\") }\n}\ntext(\"showing {v}\")";
    var tabsDoc = new DocSession([tabsSrc], null);
    var f1 = tabsDoc.Frame();
    var t1 = f1.Blocks[0][0];
    Check("tabs show the first tab, and run only its block",
        f1.Errors[0] is null && t1.Props["value"] as string == "Chart" && t1.Children[0].Children.Count == 1 && t1.Children[1].Children.Count == 0
        && f1.Blocks[0][1].Props["text"] as string == "showing Chart", f1.Errors[0]?.Message ?? "");
    tabsDoc.Set(t1.Key!, "Table");
    var f2 = tabsDoc.Frame();
    Check("a picked tab shows instead", f2.Blocks[0][0].Children[1].Children.Count == 1 && f2.Blocks[0][0].Children[0].Children.Count == 0
        && f2.Blocks[0][1].Props["text"] as string == "showing Table");
    Check("a tab outside tabs is an error", ErrorOf("tab(\"x\") { text(\"y\") }")?.Contains("inside tabs") == true);
}
{
    var plotted = new DocSession(["plot(y: [0, 1], log: \"x\") {\n  series(\"S\", [1, 10, 100], [\"0.5\", 0.25, 0.125], tone: accent, fill: true)\n  rule(y: 0.5, label: \"half\")\n  point(10, 0.25, label: \"here\")\n}"], null).Frame();
    var plot = plotted.Blocks[0].FirstOrDefault();
    Check("a plot holds its series, rules and points, with numbers for points",
        plotted.Errors[0] is null && plot?.Type == "plot" && plot.Children.Select(c => c.Type).SequenceEqual(["series", "rule", "point"])
        && plot.Children[0].Props["ys"] is List<object?> { Count: 3 } ys && ys[0] is 0.5d, plotted.Errors[0]?.Message ?? "");
}
Check("a series outside a plot is an error", ErrorOf("series(\"a\", [1], [2])")?.Contains("inside plot") == true);
Check("a rule needs x or y", ErrorOf("plot { rule(label: \"x\") }")?.Contains("x: or y:") == true);
Check("uneven xs and ys draw the shorter, with a warning",
    new DocSession(["plot { series(\"a\", [1, 2, 3], [1, 2]) }"], null).Frame() is var uneven
    && uneven.Blocks[0][0].Children[0].Props["xs"] is List<object?> { Count: 2 } && uneven.Warnings.Any(w => w.Contains("2 points drawn")));
Check("bars take {label, value} records", First("bars([{label: \"a\", value: 3}])")?.Type == "bars" && ErrorOf("bars([1, 2])")?.Contains("{label, value}") == true);
Check("a frame counts its steps", new DocSession(["let xs = [1, 2, 3].map(x => x * 2)\ntext(str(xs.length))"], null).Frame().Steps is > 0 and < 1000);
Check("tone names are constants", First("badge(\"x\", tone: danger)")?.Props["tone"] as string == "danger");
Check("a line continues before ?",
    First("let n = 2\nlet t = n > 1\n  ? \"many\"\n  : \"few\"\ntext(t)")?.Props["text"] as string == "many");
Check("a lambda may have a body",
    First("let f = x => { let y = x * 2; return y + 1 }\ntext(\"{f(3)}\")")?.Props["text"] as string == "7");
Check("a local may be reassigned",
    First("let xs = []\nfor i in [1, 2, 3] { xs = [...xs, i * 10] }\ntext(xs.join(\"-\"))")?.Props["text"] as string == "10-20-30");
Check("quotes inside {} stay in the string",
    First("let s = \"all\"\ntext(\"Export {s == \"all\" ? 1 : 2}\")")?.Props["text"] as string == "Export 1");
Check("a list returns its count",
    First("let l = list(\"L\") { for x in [1,2,3] where x > 1 { item(x, \"x\") } }\ntext(\"{l.count}\")") is null
    || new DocSession(["let l = list(\"L\") { for x in [1,2,3] where x > 1 { item(x, \"x\") } }\ntext(\"{l.count}\")"], null)
        .Frame().Blocks[0][1].Props["text"] as string == "2");

// One program across blocks: a let in the first block is seen by the second.
var two = new DocSession(["let total = 40 + 2", "stat(\"Total\", total)"], null).Frame();
Check("blocks share one scope", two.Blocks[1].FirstOrDefault()?.Props["value"] is 42d, two.Errors[1]?.Message ?? "");
var broken = new DocSession(["text(", "text(\"still drawn\")"], null).Frame();
Check("a block that does not parse leaves the others drawing",
    broken.Errors[0] is not null && broken.Blocks[1].FirstOrDefault()?.Props["text"] as string == "still drawn");

// Front matter.
var meta = """
    ---
    title: Which supplier
    data:
      vendors:
        - { name: Northwind, price: 41000 }
        - name: "Contoso Ltd"
          price: 38500
          tags: [quality, "lead: long"]
      today: 2026-11-12
      open: true   # a comment
    ---
    """;
var data = DocSession.DataFrom(meta, out var yamlError) as Dictionary<string, object?>;
Check("front matter parses", data is not null, yamlError ?? "");
var vendors = data?["vendors"] as List<object?>;
Check("flow and block records", vendors is [Dictionary<string, object?> { } a, Dictionary<string, object?> b]
    && a["name"] as string == "Northwind" && a["price"] is 41000d && b["name"] as string == "Contoso Ltd" && b["price"] is 38500d);
Check("a flow list with a quoted colon",
    vendors?[1] is Dictionary<string, object?> v2 && v2["tags"] is List<object?> { Count: 2 } tags && tags[1] as string == "lead: long");
Check("scalars", data?["today"] as string == "2026-11-12" && data?["open"] is true);

// Data beside the section that uses it: `bib data` blocks add their keys to data.
var merged = DocSession.DataFrom("---\ndata:\n  asof: 12 Nov\n---", ["review:\n  asked: [a, b]", "models:\n  - title: x"], out var mergeError)
    as Dictionary<string, object?>;
Check("data blocks add their keys", mergeError is null && merged?["asof"] as string == "12 Nov"
    && merged?["review"] is Dictionary<string, object?> review && review["asked"] is List<object?> { Count: 2 }
    && merged?["models"] is List<object?> { Count: 1 }, mergeError ?? "");
var onlyBlocks = DocSession.DataFrom(null, ["steps: [1, 2]"], out var onlyError) as Dictionary<string, object?>;
Check("data blocks need no front matter", onlyError is null && onlyBlocks?["steps"] is List<object?> { Count: 2 });
DocSession.DataFrom("---\ndata:\n  review: 1\n---", ["review: 2"], out var twice);
Check("a key given twice is named", twice?.Contains("'review' is already in the data") == true, twice ?? "no error");
DocSession.DataFrom(null, ["- just a list"], out var notMapping);
Check("a data block must be a mapping", notMapping?.Contains("not a mapping") == true, notMapping ?? "no error");
var shared = new DocSession(["stat(\"Asked\", data.review.asked.length)"], merged).Frame();
Check("blocks read merged data", shared.Blocks[0].FirstOrDefault()?.Props["value"] is 2d, shared.Errors[0]?.Message ?? "");

// State: a click changes the document's own state, and the next frame draws it.
const string Wizard = "let step = state(\"wizard\", 1)\nif button(\"Next\").clicked { set(\"wizard\", step + 1) }\ntext(\"Step {step}\")";
var wizard = new DocSession([Wizard], null);
Check("state starts at its initial value", wizard.Frame().Blocks[0][1].Props["text"] as string == "Step 1");
wizard.Click("next#0");
Check("a click's set() is kept", wizard.Frame().Blocks[0][1].Props["text"] as string == "Step 2");
Check("set() while drawing is an error",
    ErrorOf("set(\"x\", 1)")?.Contains("ran while drawing") == true, ErrorOf("set(\"x\", 1)") ?? "no error");
var hidden = new DocSession(["if state(\"page\", 1) == 1 { input(\"Name\") }\ntext(\"Hello {value(\"Name\")}\")"], null);
hidden.Set("name", "Ada");
Check("value() reads an input by its key", hidden.Frame().Blocks[0][1].Props["text"] as string == "Hello Ada");

// A contained host: nothing a document does reaches outside it.
const string Sends = "if button(\"Go\").clicked { send(\"hi\") }";
var contained = new DocSession([Sends], null, contained: true).Click("go#0");
Check("a contained document cannot send",
    contained.Errors[0]?.Message.Contains("reaches outside") == true && contained.Effects.Count == 0, contained.Errors[0]?.Message ?? "no error");
var found = Interpreter.ReachesOutside(["text(\"ok\")", Sends, "let r = {call: 1}\ntext(\"{r.call}\")"]);
Check("reaching outside is found without running", found is ["block 2, line 1:27: send()"], string.Join("; ", found));

Console.WriteLine($"{caseCount} cases; {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static object? FromJson(JsonNode? n) => n switch
{
    null => null,
    JsonObject o => o.ToDictionary(kv => kv.Key, kv => FromJson(kv.Value)),
    JsonArray a => a.Select(FromJson).ToList(),
    JsonValue v when v.TryGetValue<bool>(out var b) => b,
    JsonValue v when v.TryGetValue<double>(out var d) => d,
    JsonValue v => v.GetValue<string>(),
    _ => null,
};

static bool Same(JsonNode? expected, JsonNode? actual, out string why)
{
    why = "";
    switch (expected)
    {
        case null:
            if (actual is null) return true;
            why = $"expected nothing, got {actual.ToJsonString()}";
            return false;
        case JsonObject eo:
            if (actual is not JsonObject ao) { why = $"expected {eo.ToJsonString()}, got {actual?.ToJsonString()}"; return false; }
            foreach (var (k, v) in eo)
                if (!Same(v, ao[k], out var inner)) { why = $"{k}: {inner}"; return false; }
            foreach (var (k, _) in ao)
                if (!eo.ContainsKey(k)) { why = $"unexpected {k}: {ao[k]?.ToJsonString()}"; return false; }
            return true;
        case JsonArray ea:
            if (actual is not JsonArray aa || aa.Count != ea.Count) { why = $"expected {ea.ToJsonString()}, got {actual?.ToJsonString()}"; return false; }
            for (int i = 0; i < ea.Count; i++)
                if (!Same(ea[i], aa[i], out var inner)) { why = $"[{i}] {inner}"; return false; }
            return true;
        default:
            var ev = (JsonValue)expected;
            if (actual is not JsonValue av) { why = $"expected {ev.ToJsonString()}, got {actual?.ToJsonString()}"; return false; }
            if (ev.TryGetValue<double>(out var ed) && av.TryGetValue<double>(out var ad)) { if (Math.Abs(ed - ad) < 1e-9) return true; }
            else if (ev.ToJsonString() == av.ToJsonString()) return true;
            why = $"expected {ev.ToJsonString()}, got {av.ToJsonString()}";
            return false;
    }
}
