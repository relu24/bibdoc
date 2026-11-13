# bibdoc

The bib document language: interactive documents written as small immediate-mode
programs inside Markdown. Each fenced `bib` block runs again every frame; a widget
draws where it is called and returns its value.

```bib
let units = slider("Units", 1, 10, value: 2, step: 1)
for v in data.vendors { stat(v.name, v.price * units, unit: "EUR") }
if button("Order").clicked { respond(units: units) }
```

- **`src/BibDoc`**: the interpreter. Parser, interpreter, frame tree, front matter
  (`data:`) and a session that keeps a reader's input values. It draws nothing: a
  frame is plain records (`FrameNode`) for a renderer to draw, and a click gives the
  effects it would cause (`call`, `send`, `respond`). No dependencies; Native AOT.
- **`tests/conformance`**: 31 cases, each a document, its data and a script of events
  with the frames and effects expected. `dotnet run -c Release` in that folder replays
  them, then checks the rules they do not reach.
- **`docs/reference.md`**: the language as the interpreter runs it, for writers and
  agents: every widget, the primitives that are built, and the mistakes that recur.
  `skills/bib-blocks` is a Claude Code skill built on it (link it into
  `~/.claude/skills/`); it checks documents with bibmd's `md FILE --check`.
- **`docs/proposal.md`**: the language (grammar, widgets, effects, rules that make
  every document finish) and the benchmark it was chosen by.

## Using it

```csharp
using BibDoc;

var session = new DocSession(blockSources, DocSession.DataFrom(frontMatter, out _));
FrameResult frame = session.Frame();          // what each block draws now
session.Set("units", 4d);                     // an input's new value, by its key
FrameResult click = session.Click("order#0"); // what that click would do: click.Effects
```

bibmd draws `bib` blocks with bibui and references this repository as a sibling
checkout (`../bibdoc`).
