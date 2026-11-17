# Proposal: an immediate-mode document language

**Status:** proposed; the interpreter (`src/BibDoc`) and a native renderer (bibmd) exist and pass the conformance suite. The language is still open to change: see *Open*.

This began as a proposal to the bibui repository, and moved here: the language, its interpreter, its specification and its conformance suite are their own project. bibui stays a UI library; nothing here needs a change in it. bibmd draws documents with bibui and uses this repository for everything else.

## Why

People increasingly share small interactive documents: a comparison that recomputes when you move a weight, a release board whose blockers depend on a toggle, a calculator. One person or an agent writes them once, and a few others read them and act on them. Today these get written in one of three ways, and each has a cost:

- **HTML and JavaScript.** Anything is possible, which is the problem: the document's script runs with the reader's session, so it needs its own origin and a sandboxed frame, and it can never be drawn natively.
- **Markdown.** It's safe and diffable, but it can't compute or respond to anything.
- **A JSON tree of widgets with expressions in strings** (`"value": "=data.total"`). It's safe and can be drawn natively, but the evidence below shows it's the hardest of the candidates for a model to write, and the most expensive.

bibui is immediate-mode: each frame calls the build code again, and a widget returns its result where it is called (`if (runtime.Button("add", "Add one").Clicked) count++`). A document language with the same shape fits bibui exactly. The interpreter calls the runtime. Control flow is plain `if` and `for`. An input's value is a variable, not an id quoted inside a string.

This proposal defines that language. It's meant to be written by people and by models, drawn by bibui natively, and drawn as ordinary HTML in a browser.

## A document

A document is a Markdown file (`.bib.md`) with front matter, prose, and fenced `bib` blocks:

````markdown
---
bib: 1
title: Which supplier for the pilot run
data:
  vendors:
    - { name: Northwind, price: 41000, quality: 8, service: 6 }
    - { name: Fabrikam, price: 45250, quality: 6, service: 9 }
effects: [respond]
---
# Which supplier for the pilot run

Prose is Markdown, and stays text: selectable, searchable, readable by a screen reader.

```bib
let wq = slider("Quality weight", 0, 10, value: 5)
let ws = slider("Service weight", 0, 10, value: 3)
let score = v => wq + ws == 0 ? 0 : round((v.quality * wq + v.service * ws) / (wq + ws), 1)
let best = max(data.vendors.map(score))

for v in data.vendors {
  card(v.name) {
    stat("Score", score(v))
    stat("Price", v.price, unit: "EUR")
    if score(v) == best { badge("best", tone: accent) }
  }
}

let pick = choice("Pick", data.vendors.map(v => v.name))
if button("Choose", style: primary).clicked { respond(vendor: pick) }
```
````

- **`bib`** is the language version. A reader that doesn't know the version shows the blocks as code.
- **`data`** is the document's input, readable in every block as `data`. It can also be kept beside the section that uses it, in fenced blocks whose info string is `bib data`: each holds YAML, a mapping, whose keys are added to `data`. All of them are read before any block runs, so where they stand does not matter; a key given twice is an error that names it. A renderer draws nothing for them.
- **`effects`** lists what the document may do: `respond`, `send`, and named tool calls (`call: [orders.receive]`). A host asks the reader before it allows any of them, and refuses an effect that isn't listed (see *Effects*).
- **The blocks of one document share one scope**, in order: a `let` in the first block is visible in the second. All blocks run as one program, top to bottom, every frame.

In a Markdown reader that knows nothing of `bib`, the file degrades to prose plus code blocks, which is the right failure.

## The language

### Lexical

- Comments are `// …` and `/* … */`.
- Strings are `"…"` or `'…'`. Inside a string, `{expr}` is replaced by the expression's value: `"Total: {total} EUR"`. Quotes inside a `{…}` don't end the string, so `"Export {s == "all" ? n : m}"` is one string. `{{` is a literal brace. Backtick strings use `${expr}`, as in JavaScript.
- Numbers are decimal: `12`, `0.5`.
- A statement ends at a newline or `;`, **except** where the line obviously continues: after a binary operator, `?`, `:`, `=>`, `,`, `=` or an open bracket, or before a line that starts with `.`, `?.`, `?`, `:`, `&&`, `||`, `??`, a binary operator, `)` or `]`. So this is one statement:

  ```
  let shown = hide
    ? changes.filter(c => c.kind != "chore")
    : changes
  ```

### Grammar

```ebnf
program    = { statement } ;
block      = "{" { statement } "}" ;
statement  = let | assign | fn | if | for | return | call-stmt ;

let        = ( "let" | "const" ) name "=" expr [ block ] ;
assign     = name "=" expr ;                    (* only a name a `let` declared *)
fn         = "fn" name "(" [ name { "," name } ] ")" block ;
if         = "if" expr block [ "else" ( if | block ) ] ;
for        = "for" name "in" expr [ "where" expr ] block ;
return     = "return" [ expr ] ;                (* only inside fn *)
call-stmt  = expr [ block ] ;                   (* a widget, or a container with its block *)

expr       = ternary ;
ternary    = nullish [ "?" expr ":" expr ] ;
nullish    = or { "??" or } ;
or         = and { "||" and } ;
and        = equality { "&&" equality } ;
equality   = compare { ( "==" | "!=" | "===" | "!==" ) compare } ;
compare    = sum { ( "<" | "<=" | ">" | ">=" ) sum } ;
sum        = product { ( "+" | "-" ) product } ;
product    = unary { ( "*" | "/" | "%" ) unary } ;
unary      = [ "!" | "-" ] postfix ;
postfix    = primary { "." name | "?." name | "[" expr "]" | args } ;
args       = "(" [ arg { "," arg } ] ")" ;
arg        = [ name ":" ] expr ;                (* named arguments come last *)
primary    = number | string | "true" | "false" | "null" | name
           | "[" [ item { "," item } ] "]" | "{" [ pair { "," pair } ] "}"
           | lambda | "(" expr ")" ;
item       = [ "..." ] expr ;
pair       = name ":" expr | string ":" expr | name | "..." expr ;
lambda     = ( name | "(" [ name { "," name } ] ")" ) "=>" ( expr | body ) ;
body       = "{" { ( "let" name "=" expr | "return" expr ) [ ";" ] } "}" ;
```

`===` means `==`. Both compare by value. A container's block follows its call on the same line: `card("Orders") {`. `row { … }` and `column { … }` may drop the parentheses.

### Values and expressions

Values are null, booleans, numbers, strings, lists, records, and functions. Expressions are a pure subset of JavaScript, deliberately: it's the expression syntax models write most reliably, and every rule below that differs from JavaScript produces an error rather than a different answer.

- Member access on null gives null (`a.b.c` doesn't throw). Truthiness is JavaScript's.
- **List methods:** `map`, `filter`, `find`, `findIndex`, `some`, `every`, `includes`, `indexOf`, `join`, `slice`, `concat`, `reverse`, `sort`, `reduce`, `flatMap`, `length`, plus `sum(fn?)` and `count(fn?)`. Callbacks receive `(item, index, list)`, as in JavaScript.
- **String methods:** `length`, `includes`, `startsWith`, `endsWith`, `toUpperCase`, `toLowerCase`, `trim`, `split`, `slice`, `replace`. Number: `toFixed`.
- **Globals:** `count`, `sum(list, fn?)`, `min`, `max`, `round(x, digits?)`, `abs`, `floor`, `ceil`, `unique`, `str`, `num`, `Math.*` (round, floor, ceil, abs, min, max), `Object.keys/values/entries`, `Infinity`.
- **Lists and records never change.** `push`, `pop`, `shift`, `unshift` and `splice` are errors that name the alternative: `[...xs, x]`, `xs.concat([x])` or `unique(xs)`.
- **Tone and style names are constants:** `neutral`, `muted`, `success`, `warning`, `danger`, `accent`, `primary`, `ghost`. So `tone: danger` and `tone: "danger"` mean the same thing.

### Widgets

A widget is a function call. It draws where it is called, and returns a value:

| Call | Draws | Returns |
|---|---|---|
| `text(text, tone:?)`, `heading(text)`, `markdown(text)`, `badge(text, tone:?)`, `divider()` | text and labels | null |
| `card(title, subtitle:?, icon:?, tone:?, badge:?, badgeTone:?, footer:?) { … }`, `section(title) { … }`, `row(fit:?) { … }`, `column { … }` | containers. A card's `badge` sits at the right of its title line, and `footer` (Markdown) under a line at its foot. A row shares its width equally among its children, or with `fit: true` gives each only the width it needs | null |
| `grid(columns:?) { … }` | its children in rows of `columns` (3 by default), equal widths, wrapping | null |
| `steps { step(n, title, status:?, tone:?, who:?, phase:?) { … } … }` | a numbered vertical timeline: each step's number in a circle of its `tone`, joined by a line; `status` is a pill on the title line, `who` a line under it, and a step's block holds its detail. A `phase` label is drawn above the first step of each phase | null |
| `chips(items, tone:?)` | small pills in the code face, wrapping: references, ids, `verify` | null |
| `stat(label, value, unit:?, tone:?)`, `progress(label, value, max:?)`, `kv(items)`, `table(columns, rows, widths:?)` | figures. A table's cells wrap; `widths` shares its width in proportion (`[1, 2, 2]`); a cell that is a record with a `badge` (`{badge: "open", tone: warning}`) is drawn as that badge | null |
| `list(label, selectable:?) { item(id, title, subtitle:?, badge:?, tone:?) … }` | rows | `{selected, count}`: the ticked item ids, and how many items it drew |
| `button(label, style:?, disabled:?)` | a button | `{clicked}` |
| `choice(label, options, value:?)` | pick one | the picked option (the first, by default) |
| `slider(label, min, max, value:?, step:?)` | a number | the number (`min`, by default). Without `step:` it moves in a hundredth of the range as a power of ten (0.1 on 0–10), and the value is kept to that many decimals; `step: 1` for whole numbers |
| `input(label, value:?, multiline:?, placeholder:?)` | a text field | the text |
| `toggle(label, value:?)` | on/off | `true` or `false` |
| `checklist(label, options, value:?)` | tick any | the ticked options |

Positional arguments come first, in the order shown, and named ones after. An unknown named argument is a warning, not an error, so a document written for a newer version still draws in an older one.

`list` returns a record, not a bare list of ids, on purpose. `let shown = list("Issues") { … }` followed by `shown.length` was the single most common semantic mistake in the evidence: the writer expected the items, and got the ticked ids, which are empty. With `{selected, count}`, the code says what it reads.

### Statements

- **`let x = expr`** binds a name in the current block. **`x = expr`** reassigns a name a `let` declared. A local may change while a frame runs (`owners = [...owners, o.owner]` in a loop), and nothing a local holds survives to the next frame. Only input values do.
- **`if` / `else if` / `else`** and **`for x in list where cond`** are the only control flow. Inside a `for`, `index` is the position.
- **`fn name(a, b) { … }`** defines a piece that may draw, may `return` a value, or both. Lambdas (`x => …`) compute and never draw. Their body is an expression, or `{ let …; return … }`.

### Effects

`call(tool, …args)`, `send(text)` and `respond(…fields)` are the only things a document can do to the world. They run **only while the host handles a click**, and only inside the branch that the click made true:

```
if button("Archive", disabled: picked.selected.length == 0).clicked {
  call("mail.archive", ids: picked.selected)
}
```

The host runs a frame for drawing, and when the reader clicks, runs one more frame in which that button returns `clicked: true`. An effect reached in a drawing frame is an error ("`call()` ran while drawing; effects belong inside `if button(…).clicked { }`"). That makes an effect at the top level, or one guarded by anything but a click, a mistake the writer sees the first time the document draws, not something that fires on every frame. A disabled button never clicks.

An effect the front matter doesn't list is refused when it runs. The host shows what was asked for, and nothing happens.

### Identity and state

- An input is identified by its label (or by `id:` when two inputs share one), so its value survives from frame to frame while the reader works with it.
- A button is identified by its label and by how many buttons with the same label came before it in this frame. So the third card's "Message" button is a different button from the first card's.
- An input that isn't drawn in a frame keeps its value, but its `let` isn't bound until it's drawn again (see *Open*).

### Totality

Every document finishes, and finishes fast:

- No `while`, no recursion (a `fn` that calls itself, directly or not, is an error), and `for` only over a list that already exists.
- A step budget per frame, 200,000 evaluation steps. Running out is an error that names the block.
- No clock, no randomness, no I/O. A frame is a function of `data`, the declared sources, and the input values. Drawing the same state twice gives the same frame.

### Errors

An error gives the line and column, and names the fix where there is one. Agents read the error and try again, so the message is part of the language. In the evidence, a single repair round with these messages fixed 3 of the 3 documents that still failed to run.

## Drawing it

The interpreter produces a **frame tree**: plain records (`{type: "stat", label: "Total", value: 82000}`), one per widget, in program order. Two renderers draw it:

- **Native.** A bibui app (bibmd today) runs the interpreter each frame and draws the frame tree with `BibuiRuntime`. Containers become `BeginParent`/`EndParent` around the block, and inputs become the existing components: `Slider`, `Switch` for `toggle`, `RadioGroup` or `Select` for `choice`, `TextField` for `input`, `Checkbox` rows for `checklist`, `Button`, `Progress`, `Badge`, `Section`, `Markdown`. `card`, `stat`, `kv`, `table` and `list`/`item` have no component today. They're composed from `Box` and `Text` by the app that draws the document; bibmd does.
- **Browser.** The same program runs in a small JavaScript interpreter. Each frame's tree is diffed onto real HTML elements, so text is selectable, Ctrl+F finds it, and screen readers read it. No canvas and no download of the .NET runtime is needed to read a document. The canvas (`Bibui.Platform.Browser`) remains the route for a document that embeds a compiled bibui app.

Two interpreters stay equal through a **conformance suite**: each case is a document, data, and a script of events (set an input, tick items, click a button), with the expected frame trees and effects as JSON. Both interpreters run every case, and the C# one is the reference. The frame tree is also the archived form of a document as a reader saw it.

While a document streams in (an agent writing it), the host runs the statements that are already complete and draws what they draw. A program drawn top to bottom draws its finished part.

## This repository

- **`src/BibDoc`** (namespace `BibDoc`): the parser, interpreter, frame tree, front matter and session. It depends on nothing, draws nothing, and publishes with Native AOT. A renderer reads the frame tree.
- **`tests/conformance`**: the shared cases, each a document, data and a script of events with the frames and effects expected, and the runner that replays them against `BibDoc`. A second interpreter passes when it gives the same JSON.
- **`docs/proposal.md`**: this document, the language's specification until it is split into a reference.
- **`docs/two-layers.md`**: the next design, not built yet. Primitives (`box`, `text`, inputs, drawing) with a fixed style vocabulary on bibui's box model, a theme, `state()` and events beyond clicks, and the widgets of this document rewritten as a standard library in bib. What artifacts get from CSS and JavaScript, without what makes them unsafe or unreproducible.
- **`docs/charts.md`**: built. `plot` (series, dots, rules, points, log axes) and `bars`, native so a curve costs one call, from what the showcase documents drew by hand.
- **`bibdoc.js`** (to come): the browser interpreter and HTML renderer, here so that it versions with the language.

The renderers live with their apps: bibmd draws documents with bibui, and references this repository as a sibling checkout (`../bibdoc`).

## Evidence

The choice of language was measured, not argued. A harness gave four candidate languages the same 22 tasks: static cards, data-bound tables, filters that combine, derived totals, actions guarded by a selection, per-row buttons, and three long documents (a pipeline, a release board, a weighted comparison). All four compiled to the same widget set and the same expression language, so only the shape of the language differed. Haiku, Sonnet and Opus wrote each document, two or three samples per cell. Every check drove the document (set inputs, tick items, click) and looked only at what it drew and what it did, never at its syntax. Every check was first proven against hand-written reference solutions. In all, about 2,100 model calls.

**Round 2: four languages, 19 tasks, 152 documents each.** The JSON tree is the existing ui-block format.

| | JSON tree | JSX subset | KDL style | Immediate mode |
|---|---|---|---|---|
| Pass | 90% | 98% | 96% | 96% |
| Hardest tasks (two) | 38%, 25% | 75%, 100% | 88%, 75% | 100%, 100% |
| Tokens per document | 197 | 160 | 123 | 122 |
| Thinking tokens | 988 | 930 | 895 | 663 |
| Seconds per document | 10.3 | 9.8 | 9.3 | 7.8 |

**Round 3: the two leaders, 22 tasks including the long ones, 176 documents each, with a repair round** (a document that fails to run gets its error back once).

| | JSX subset | Immediate mode |
|---|---|---|
| Pass | 97% | 98% |
| Pass after one repair | 99% | 99% |
| Haiku / Sonnet / Opus | 98 / 97 / 95 | 95 / 100 / 98 |
| Tokens per document | 232 | 170 (−27%) |
| Thinking tokens | 1,363 | 879 (−36%) |
| Seconds per document | 12.0 | 8.5 (−29%) |
| Cost per passing document | 0.014 USD | 0.011 USD (−21%) |

The two are tied on correctness, and immediate mode is cheaper on every cost measure. The tree languages also have a cost that doesn't show as a failure until it does: without `let`, a derived value is written out every time it's used. In the comparison task a JSX document repeats the score formula three times, and the copies drift apart (`count(…)) > 0`).

**Most of the rules above came from failures, not taste.** Written as first specified, immediate mode scored 85% (round 1), then 90% (round 3). Each fix below answers a failure that recurred across models:

| Failure, as written | Rule |
|---|---|
| `badge("failing", tone: danger)`: an unknown name | Tone and style names are constants |
| `fn score(v) { return … }`: no `return` | `fn` may return a value |
| `owners = [...owners, x]` in a loop | Locals may be reassigned within a frame |
| `assignees.push(x)` | Lists never change, and the error names the alternative |
| `let shown = list(…) { … }`, then `shown.length` (0) | `list` returns `{selected, count}` |
| `hideChores\n  ? a\n  : b` | A line continues after or before an operator |
| `v => { let s = …; return s }` | Lambdas may have a body of lets and a return |

Three tests never separated the languages, and the proposal leans on none of them: a fresh reader predicting what a document does (100% everywhere), editing a passing document (about 100%, with about a third of the lines changed), and how much of a document draws while it streams in (about half, in every language).

**Caveats.** The parsers were written for the benchmark, and changing how they behaved moved scores by several points. Immediate mode was tuned more than JSX. Five of the seven rules in the table above are its own; JSX got one (`<Else>` inside `<If>` as well as after it). The other two, the `push` error and lambda bodies, belong to the shared expression language, so both got them. More of immediate mode's failures were design gaps, so it got more fixes. The final round re-scored documents written to the earlier specification with the fixed parsers. The harness and its results aren't published; the conformance cases are what they left behind. The tasks are short: the long ones are about 25 lines, and documents of 200 lines are untested. None of this measures how readable the language is to a colleague who doesn't program.

## Steps

1. **The conformance suite** (done): 31 cases recorded from the benchmark's reference interpreter, driving the reference documents through their tasks' checks.
2. **The interpreter** (done): `src/BibDoc` passes the suite headless.
3. **Native drawing** (done, in bibmd): fenced `bib` blocks drawn live with bibui's components and the missing ones composed. Effects are shown, not run.
4. **`bibdoc.js`:** the browser interpreter and HTML renderer, passing the same suite.
5. **Streaming and editing:** drawing complete statements while a document arrives, and editing a block in place (as the Markdown editor does for prose).

1 and 2 go first, and nothing draws until the suite passes. 3 and 4 are independent of each other.

## Open

- **More freedom.** The closed vocabulary keeps documents short and reliable, and also limits what they can look like. `docs/two-layers.md` proposes the way out; it also answers *Undrawn inputs* below (`value(key)`).

- **Undrawn inputs.** An input inside an `if` that's false keeps its value, but its `let` isn't bound, so another branch can't read it. Should the language have `state("name", default)`, a value that exists whether or not anything draws it? The benchmark avoided the case, and real documents (a wizard with steps) will hit it.
- **Sources.** The front matter can declare live data (`sources: {orders: {call: orders.list}}`) that the host fetches as the reader. How often is it read again, how is a slow source drawn, and does a source failing fail the whole document or one block?
- **Number and date formatting.** `{amount}` prints `1200`. A `fmt(x, "currency")` family, or the reader's locale applied by the renderer?
- **Where `bibdoc.js` is built and published.** Here it versions with the language; a web app consumes it as a package.
- **The name.** "bib" is the working name for the format and its fence.
