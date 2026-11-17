# bib: a reference for writing documents

What a writer, person or agent, needs to write a `.bib.md` that runs: the language as the
interpreter in `src/BibDoc` runs it today, the widgets, and the mistakes that recur.
`proposal.md` is the reasoning and the evidence; `two-layers.md` the design of the
primitives, of which part is built (marked below). Where they and this page differ, this
page says what runs.

**Check what you write:** `md FILE.bib.md --check` (bibmd) runs every block headless,
prints each error at its line in the file, and clicks each button once to say what it
would do. Exit 1 on an error. Write, check, fix.

## A document

````markdown
---
bib: 1
title: Which supplier for the pilot run
width: wide
data:
  vendors:
    - { name: Northwind, price: 41000, quality: 8 }
    - { name: Fabrikam, price: 45250, quality: 6 }
effects: [respond, send]
---
# Which supplier for the pilot run

Prose is ordinary Markdown.

```bib
let w = slider("Quality weight", 0, 10, value: 5, step: 1)
let score = v => round(v.quality * w / 10, 1)
let best = max(data.vendors.map(score))

row {
  for v in data.vendors {
    card(v.name, tone: score(v) == best ? accent : neutral) {
      stat("Score", score(v))
      stat("Price", v.price, unit: "EUR")
    }
  }
}

let pick = choice("Pick", data.vendors.map(v => v.name))
if button("Choose", style: primary).clicked { respond(vendor: pick) }
```
````

- **Every `bib` block of a file is one program**, run top to bottom, every frame. A `let`
  at the top level of one block is seen by the blocks after it.
- **`data:`** in the front matter is the input, read as `data`. A fenced block with the
  info string `bib data` holds YAML whose keys are added to `data` (a key given twice is
  an error); it draws nothing.
- **`effects:`** lists what a click may do: `respond`, `send`, `call` or a tool's name.
  A host refuses an effect the list doesn't name.
- bibmd also reads `width: wide | full | <px>`, `nav: true`, `locked: true`.

## Statements

```
let x = expr                 a name, in the current block
x = expr                     reassign a name a let declared (locals may change within a frame)
if cond { … } else if c { … } else { … }
for v in list where cond { … }          inside, `index` is v's position
fn name(a, b, tone: muted) { … }        may draw, may `return` a value; params can have defaults
widget(args) { … }                       a container and the block inside it
// comment   /* comment */
```

- **A `let` inside `{ }` (a card, row, if, for) is local to it.** To use a value after the
  container, declare it before and assign it inside:

  ```
  let units = 0
  card("Batch") { units = slider("Units", 1, 4, step: 1) }
  stat("Total", units * 900, unit: "g")
  ```

- No `while`, no recursion, no clock, no randomness, no I/O. A frame has a budget of
  200,000 steps for the whole document (every block together; one `line(…)` costs some 50). `for`
  runs over a list that exists; for a range, write the list (`[0, 1, 2, 3]`). It also has a
  budget of 4,000,000 values built and gone through (list items, record fields, characters of
  text): `[...acc, x]` in a `reduce` over 300 items takes some 45,000.
- A statement ends at a newline or `;`, except where the line obviously continues: after an
  operator, `?`, `:`, `=>`, `,` or an open bracket, or before a line starting with `.`, `?`,
  `:`, `&&`, `||`, `??`, an operator, `)` or `]`.

## Expressions

A pure subset of JavaScript.

- Values: numbers, strings, `true`/`false`, `null`, lists `[a, ...xs]`, records
  `{ a: 1, b, ...other }`, lambdas `v => v.price`, `(a, b) => { let s = a + b; return s }`: a lambda's body holds only `let` and `return` (no `if`,
  no reassigning: use `? :`, or a `fn`). Numbers may have an exponent: `3.156e7`.
- Operators: `+ - * / %`, `== != === !==` (all compare by value), `< <= > >=`, `&& || !`,
  `??`, `a ? b : c`, `a.b`, `a?.b`, `a[i]`. Member access on null gives null.
- **Strings:** `"…"` or `'…'`, with `{expr}` interpolated: `"Total: {total} EUR"`.
  Quotes inside `{…}` are fine: `"{n == 1 ? "item" : "items"}"`. A literal brace is `\{`
  (not `{{`). Backtick strings interpolate `${expr}`. `\n` is a newline.
- **List methods:** `map filter find findIndex some every includes indexOf join slice
  concat reverse sort reduce flatMap length`, and `sum(fn?)`, `count(fn?)`. Callbacks get
  `(item, index, list)`.
- **String methods:** `length includes startsWith endsWith toUpperCase toLowerCase trim
  split slice replace`; numbers: `toFixed`.
- **Globals:** `count(list)`, `sum(list, fn?)`, `min(list)`, `max(list)`, `round(x, digits?)`,
  `abs floor ceil`, `unique(list)`, `str(x)`, `num(s)`, `Math.round/floor/ceil/abs/min/max`,
  `Math.sqrt/pow/exp/log/log10/log2/sin/cos/tan/asin/acos/atan/atan2/hypot/sign`, `Math.PI`, `Math.E`
  (there is no `**`: write `Math.pow`),
  `Object.keys/values/entries`, `Infinity`, `fade(colour, alpha)`.
- **`fmt(x, digits?, compact:, sign:)`** writes a number as people read it: `fmt(1234567.5)` is
  `1,234,567.5` (up to two decimals, trailing zeros dropped), `fmt(2, 2)` is `2.00`,
  `fmt(8123456, compact: true)` is `8.1M` (k, M, B, T), `fmt(4.25, sign: true)` is `+4.25`. It
  returns text: compute with the number, `fmt` it where it is shown.
- **Lists and records never change.** `push`, `pop`, `shift`, `unshift`, `splice` are
  errors: build a new one, `xs = [...xs, x]`.
- **Tone and style names are constants**, written bare: `neutral muted success warning
  danger accent primary ghost`, and the colour and style words below. `tone: danger` and
  `tone: "danger"` are the same.

## Widgets

A widget is a call: it draws where it is called and returns a value. Positional arguments
first, in the order shown, named after. An unknown named argument is a warning.
`widgets.bib.md` draws every one of them, live.

**Text and figures** (return null)

| Call | Notes |
|---|---|
| `heading(text)`, `text(text, tone:)`, `markdown(text)`, `badge(text, tone:)`, `divider()` | `text` takes the style props too (below) |
| `stat(label, value, unit:, tone:)` | a big number |
| `progress(label, value, max:)` | `max` defaults to 100 |
| `kv(items)` | **items is a list of `{label, value}` records** |
| `table(columns, rows, widths:)` | `rows` is a list of lists of cells; a cell `{badge: "open", tone: warning}` draws as a badge; `widths: [1, 2, 2]` |
| `chips(items, tone:)` | small mono pills, wrapping |

**Containers** (take a `{ block }`, return null)

| Call | Notes |
|---|---|
| `card(title, subtitle:, icon:, tone:, badge:, badgeTone:, footer:)` | `footer` is Markdown |
| `section(title)` | a captioned group |
| `row { }`, `row(fit: true) { }`, `column { }` | a row shares its width equally; `fit` gives each child only what it needs |
| `grid(columns: 3) { }` | equal cells, wrapping |
| `steps { step(n, title, status:, tone:, who:, phase:) { … } }` | a numbered timeline; `phase` labels the first step of each phase |

**Charts** (return null; drawing them costs no steps, only computing their numbers does)

```
plot(x: [0, 300], y: [0, 100], height: 300, xLabel: "day", yLabel: "% of the city", log: "x") {
  series("Infectious", days, infected, tone: danger, fill: true, width: 3)
  dots("Measured", xs, ys, tone: warning, r: 3)
  rule(y: 40, label: "S = 1/R₀")        // or rule(x: 45, …): a line across, with a note
  point(45, 23, label: "peak")          // one marked point
}
bars([{label: "North", value: 42}, {label: "South", value: 31, tone: warning}], unit: "EUR")
bars(harmonics, dir: "column", max: 1, height: 160)
```

| Call | Notes |
|---|---|
| `plot(x:, y:, height:, xLabel:, yLabel:, log:) { … }` | `x`, `y`: `[min, max]`, else they fit what is drawn; ticks are chosen and written with `fmt`. `log: "x" \| "y" \| "xy"`. `height` px, 220 by default. Two or more labelled series draw a legend |
| `series(label, xs, ys, tone:, fill:, width:)` | a line through (`xs[i]`, `ys[i]`), 2,000 points at most; `fill: true` shades it down to the axis. Without a tone the series take accent, warning, success, danger, muted in turn. A point that is not a number breaks the line |
| `dots(label, xs, ys, tone:, r:)` | points without a line |
| `rule(x: \| y:, label:, tone:)` | a line across the plot at that value |
| `point(x, y, label:, tone:)` | a marked point |
| `bars(items, unit:, max:, dir:, height:)` | a bar per `{label, value, tone?}`, its value written with `fmt`; `dir: "column"` stands them up |

`series`, `dots`, `rule` and `point` go only inside a `plot`. Drawing points is free: plot
every one you compute rather than sampling to save steps.

**Inputs** (return their value; identified by label, or `id:` when two share a label)

| Call | Returns |
|---|---|
| `slider(label, min, max, value:, step:)` | the number (`min` by default). Use `step: 1` for whole numbers |
| `choice(label, options, value:, style:)` | the picked option (the first by default). `style: segmented` draws the options side by side in one outline, for a few short ones |
| `input(label, value:, multiline:, placeholder:)` | the text |
| `toggle(label, value:)` | `true` / `false` |
| `checklist(label, options, value:)` | the ticked options (a list) |
| `list(label, selectable:) { item(id, title, subtitle:, badge:, tone:) … }` | **`{selected, count}`**: the ticked item ids and how many items were drawn. Not a list |
| `button(label, style: primary \| ghost \| danger, disabled:)` | **`{clicked}`**: true only in the run of its click |
| `tabs(label, value:) { tab(title) { … } … }` | the title of the tab that shows (the first until one is picked). **Only that tab's block runs**, so what the others compute is not there: compute it before `tabs` |

## Effects

`respond(field: value, …)`, `send(text)` and `call("server.tool", arg: value, …)` are the
only things a document does to the world. They run **only inside the branch a click made
true**:

```
let picked = list("Inbox", selectable: true) { for m in data.mail { item(m.id, m.subject) } }
if button("Archive", disabled: picked.selected.length == 0).clicked {
  call("mail.archive", ids: picked.selected)
}
```

An effect anywhere else is an error the first time the document draws. A disabled button
never clicks. A button is told apart by its label and how many buttons of that label came
before it, so a `button("Open")` in each card of a loop is a separate button.

## State

```
let step = state("wizard", 1)               // a named value: 1 until set
if button("Next", disabled: step == 3).clicked { set("wizard", step + 1) }
if step == 1 { input("Name") }
if step == 3 { text("Thanks, {value("Name")}") }   // an input's value, drawn or not
```

`set` runs only in a click's branch, as the effects do. State lasts while the document is open.

**An input's value is the state named by its key**, so `set` moves an input and `value` reads
one: a preset is a button that sets a slider.

```
let terms = slider("Terms", 1, 40, value: 5, step: 1)
if button("Just the first").clicked { set("Terms", 1) }
```

## Your own pieces: `fn` and `slot`

```
fn pill(label, tone: muted) {
  box(padding: [2, 8], radius: 9999, background: fade(tone, 0.16)) { text(label, size: sm, color: tone) }
}

fn rows(items) {
  for x in items { box(dir: "row", gap: 8) { text(x.name, width: fill); slot(x) } }
}
rows(data.people) { pill(it.role, tone: accent) }   // the caller's block, drawn at slot(x), with `it` = x
```

A `fn` may draw, `return` a value, or both. Lambdas compute and never draw. A `fn` may not
call itself.

## Primitives: `box`, styled `text`, `layer` (built)

```
box(dir: "row", gap: 8, padding: [6, 10], radius: 8, background: raised, border: 1, borderColor: border) {
  text("Hello", size: lg, weight: bold)
}
```

- **`box`** (all props named): `dir: "row"` or `"column"` (the default), **quoted**: bare
  `row` and `column` are the widgets of those names, `gap`, `padding` (px, `[v, h]`,
  `[top, right, bottom, left]`), `width`/`height` (px, `fill`, `fit`, `"50%"`),
  `align: start | center | end` (across), `justify: start | center | end | between`
  (along), `wrap: true`, `background`, `border`, `borderColor`, `radius`, `opacity`,
  `shadow: low | high`, `hover: {background, borderColor}`, `at: [x, y]`, `scroll: true`.
  A box with no block is a spacer. There is no `margin`: use the parent's `gap` and `padding`.
- **`text(s, …)`**: `size: sm | md | lg | xl | display | px`, `weight: regular | bold |
  italic`, `face: body | display | mono`, `color`, `wrap: false`, `align`, `caps: true`,
  `width: fill` (only `fill`: for a fixed column, put the text in `box(width: 120) { … }`).
- **`layer { … }`** inside a box draws under its children. **In a layer, `w` and `h` are
  the box's laid-out size**, so a shape can follow the box: give the box a size (or
  `width: fill`) and draw in proportions of `w` and `h`, not in fixed pixels. Shapes:
  `rect(x, y, w, h, fill:, color:, width:, radius:)`, `circle(cx, cy, r, fill:, color:, width:)`,
  `line(x1, y1, x2, y2, color:, width:)`, `label(x, y, text, size:, color:, align:)`;
  positions and sizes are positional, `width:` is the stroke's. A label's `y` is the middle of its
  line, and `align: start | center | end` puts its start, middle or end at `x`. Everything is
  clipped to the box. `w` and `h` take `+ - * /` with numbers (and `+ -` with themselves),
  nothing else: not `min`, `max`, `round`, `Math` or a comparison. To compute with the size,
  give the box one in px.

  ```
  fn bar(label, value, top, tone: accent) {
    box(dir: "row", gap: 12, align: center) {
      box(width: 100) { text(label, size: sm, color: muted) }
      box(width: fill, height: 12, radius: 6, background: fade(tone, 0.14)) {
        layer { rect(0, 0, w * value / top, h, fill: tone, radius: 6) }
      }
      box(width: 48) { text(str(value), size: sm, face: mono) }
    }
  }
  ```

- **Colours:** a role, `ink muted faint surface raised border accent success warning
  danger`, bare or quoted (but not `text`, which is the widget: write `ink`); a faded role, `fade(accent, 0.15)`; or a hex string `"#D9A441"` (the same in
  light and dark: prefer roles).
- **Not built yet:** `draw(w, h)` (bibmd does not draw it: use a box with a `layer`),
  `path`, `class:` and `styles:`, `theme:`, `borderLeft`, events on a box with `id`
  (`clicked`, `hovered`), `key()`, `tick`, `page.width`.
- **Accepted but not drawn by bibmd yet** (no error, no effect): `shadow`, `hover`, `at`,
  `scroll`; `align: end` and `justify: end`; a `width` of `fit` or `"50%"` (it fills); a
  `height` of `fill`, `fit` or `"50%"` (it fits its children). Give sizes in px.

## Mistakes that recur

| Written | Instead |
|---|---|
| `let shown = list(…) { … }` then `shown.length` | `shown.selected.length`, or `shown.count` |
| `xs.push(x)` | `xs = [...xs, x]` (with `let xs = []` before) |
| `let v = slider(…)` inside `card { }`, used after it | `let v = 0` before the card, `v = slider(…)` inside |
| `respond(…)` at the top level, or after `if picked` | inside `if button("…").clicked { }` |
| `box(dir: row)`, `color: text` | `dir: "row"`, `color: ink`: a widget's name is a function, not a word (`--check` warns) |
| `badge("x", tone: "red")`, `tone: error` | a tone name: `danger`, `warning`, `success`, `accent`, `muted` |
| `kv({Owner: "Ana"})` or `kv([["Owner", "Ana"]])` | `kv([{label: "Owner", value: "Ana"}])` |
| `"{{literal}}"` | `"\{literal}"` |
| `for i in range(5)` | `for i in [0, 1, 2, 3, 4]` |
| `fn` calling itself | a loop over the data |
| a slider without `step:` meant for counts | `step: 1` |
| `max(...xs)` | `max(xs)` |
| `draw(240, 120) { … }` | `box(width: 240, height: 120) { layer { … } }` |
| a row of buttons and `state` to pick one of a few | `choice(…, style: segmented)`; for panels of content, `tabs { tab(…) { … } }` |
| a lambda that puts commas in thousands | `fmt(x)` |
| a `box` with a `layer` of `line`s, `label`s and ticks for a curve | `plot { series(…) }`: a curve is one call, and its axes are drawn |
| `slider("w(AA)", …)` and `slider("w(aa)", …)` | one input: labels and ids are compared without case, spaces or punctuation; give each an `id:` that differs in letters (`--check` warns) |
| `min([w, 100])`, `w < 200`, `Math.sqrt(w)` in a layer | a box with a size in px, and that number |

## Examples

- `bibmd/samples/vendor-choice.bib.md`: inputs, cards, a checklist that gates a button, `respond` and `send`.
- `bibmd/samples/sourdough.bib.md`: a long notebook; sliders in cards, `grid`, `steps`, charts drawn in layers.
- `bibdoc/samples/steps-from-primitives.bib.md`: a `steps` widget rewritten from `box`, `text`, `layer` and `slot`.
- `bibdoc/tests/conformance/cases/*.json`: short documents (`source`) with what they must do.
