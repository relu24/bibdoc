---
bib: 1
title: bib widgets and components
width: wide
nav: true
data:
  tasks:
    - { id: t1, title: Draft the outline, owner: Ana, status: done }
    - { id: t2, title: Review the numbers, owner: Ben, status: open }
    - { id: t3, title: Send to the team, owner: Cleo, status: blocked }
  sales: [{ label: North, value: 42 }, { label: South, value: 31 }, { label: East, value: 18 }, { label: West, value: 9 }]
---
# bib widgets and components

Every widget a `bib` block can draw, live. Each widget is a call: it draws where it is called
and returns a value. The Markdown source (`Ctrl+E`) shows each block's code, and the language
reference lists every widget's arguments.

## Text and figures

`heading`, `text`, `markdown`, `badge` and `divider` draw text. They return nothing.

```bib
heading("A heading")
text("Plain text, in the body's colour.")
text("Muted text, with tone: muted.", tone: muted)
markdown("Markdown works too: **bold**, *italic*, `code` and [links](https://bibmd.app).")
row(fit: true) {
  badge("neutral")
  badge("accent", tone: accent)
  badge("success", tone: success)
  badge("warning", tone: warning)
  badge("danger", tone: danger)
  badge("muted", tone: muted)
}
divider()
text("Under a divider.")
```

`stat` is a big number, `progress` a bar towards a maximum, `chips` small pills.

```bib
row {
  stat("Revenue", 48200, unit: "EUR")
  stat("Growth", 12.4, unit: "%", tone: success)
  stat("Churn", 3.1, unit: "%", tone: danger)
}
progress("Done", 64)
progress("Tickets closed", 18, max: 25)
chips(["draft", "q4", "finance", "review"])
```

`kv` aligns labels and values. It takes a list of `{label, value}` records.

```bib
kv([
  {label: "Owner", value: "Ana"},
  {label: "Due", value: "Friday"},
  {label: "Budget", value: "12 000 EUR"},
])
```

`table` takes columns and rows. A cell written as `{badge, tone}` draws as a badge.

```bib
table(["Task", "Owner", "Status"],
  data.tasks.map(t => [t.title, t.owner,
    {badge: t.status, tone: t.status == "done" ? success : t.status == "blocked" ? danger : warning}]),
  widths: [3, 1, 1])
```

## Containers

`card` is a bordered panel with a title, subtitle, badge and a Markdown footer.

```bib
row {
  card("Plain card", subtitle: "A subtitle") { text("Anything goes inside.") }
  card("Accent card", tone: accent, badge: "new", badgeTone: accent, footer: "A *Markdown* footer") {
    stat("Score", 8.2)
  }
}
```

`section` puts a caption over a group. `row` shares its width equally, `row(fit: true)` gives
each child only what it needs, and `column` stacks.

```bib
section("A section") {
  row {
    column { text("Left, line one"); text("Left, line two", tone: muted) }
    column { text("Right, line one"); text("Right, line two", tone: muted) }
  }
}
```

`grid` makes equal cells and wraps.

```bib
grid(columns: 3) {
  for s in data.sales { card(s.label) { stat("Units", s.value) } }
}
```

`steps` is a numbered timeline. `phase` labels the first step of a phase.

```bib
steps {
  step(1, "Collect the data", status: "done", tone: success, who: "Ana", phase: "Prepare") { text("From the three regions.", tone: muted) }
  step(2, "Check the numbers", status: "in progress", tone: accent, who: "Ben") { text("Totals against last quarter.", tone: muted) }
  step(3, "Publish", status: "waiting", tone: muted, who: "Cleo", phase: "Ship") { text("To the whole team.", tone: muted) }
}
```

## Charts

`plot` draws its `series`, `dots`, `rule`s and `point`s on axes it works out; `bars` draws a
bar per item. A curve is one call however many points it has.

```bib
let ts = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40]
let k = slider("Growth rate", 0.1, 0.5, value: 0.25, step: 0.01)
let grown = ts.map(t => 100 / (1 + 99 * Math.exp(-k * t)))
let half = ts.find(t => grown[t] >= 50) ?? 40
plot(y: [0, 100], height: 200, xLabel: "week", yLabel: "% adopted") {
  series("adopted", ts, grown, fill: true)
  series("straight line", ts, ts.map(t => t * 2.5), tone: muted, width: 1.5)
  rule(y: 50, label: "half", tone: muted)
  point(half, grown[half], label: "week {half}")
}
bars(data.sales, unit: "units")
```

## Inputs

Inputs return their value, so what comes after them can use it.

```bib
let n = slider("Units", 1, 20, value: 5, step: 1)
let size = choice("Size", ["Small", "Medium", "Large"], value: "Medium")
let name = input("Name", placeholder: "Type a name")
let note = input("Note", multiline: true, placeholder: "A longer note")
let rush = toggle("Rush delivery", value: false)
let extras = checklist("Extras", ["Gift wrap", "Insurance", "Manual"], value: ["Insurance"])

card("What the inputs return", tone: accent) {
  kv([
    {label: "slider", value: str(n)},
    {label: "choice", value: size},
    {label: "input", value: name == "" ? "(empty)" : name},
    {label: "toggle", value: rush ? "true" : "false"},
    {label: "checklist", value: extras.length == 0 ? "(none)" : extras.join(", ")},
  ])
}
```

`list` draws rows; with `selectable: true` they get checkboxes. It returns `{selected, count}`.
`button` returns `{clicked}`, true only in the run of its click.

```bib
let picked = list("Tasks", selectable: true) {
  for t in data.tasks {
    item(t.id, t.title, subtitle: t.owner, badge: t.status,
      tone: t.status == "done" ? success : t.status == "blocked" ? danger : warning)
  }
}
text("{picked.selected.length} of {picked.count} picked", tone: muted)

let clicks = state("clicks", 0)
row(fit: true) {
  if button("Primary", style: primary).clicked { set("clicks", clicks + 1) }
  if button("Ghost", style: ghost).clicked { set("clicks", clicks + 1) }
  if button("Danger", style: danger).clicked { set("clicks", clicks + 1) }
  button("Disabled", disabled: true)
}
text("Buttons clicked: {clicks}", tone: muted)
```

A `choice` with `style: segmented` sets a few short options side by side. An input's value is
the state named by its key, so a button can `set` it: a preset.

```bib
let speed = choice("Speed", ["Slow", "Steady", "Fast"], style: segmented, value: "Steady")
let rate = slider("Rate", 0, 100, value: 40, step: 1)
row(fit: true) {
  if button("Preset: calm", style: ghost).clicked { set("Rate", 10) }
  if button("Preset: busy", style: ghost).clicked { set("Rate", 90) }
}
text("{speed} at {rate}", tone: muted)
```

`tabs` shows one panel at a time and returns the title of the tab that shows. Only that tab's
block runs.

```bib
let view = tabs("View") {
  tab("Summary") {
    grid(columns: 3) {
      stat("Total", fmt(1234567.5))
      stat("Compact", fmt(1234567.5, compact: true))
      stat("Change", fmt(4.25, sign: true), unit: "%")
    }
  }
  tab("By region") {
    table(["Region", "Sales"], data.sales.map(s => [s.label, fmt(s.value * 1000)]))
  }
}
text("Showing {view}. fmt() writes numbers as people read them.", tone: muted)
```

## Your own pieces

There is no chart widget. Charts and anything else are built from the primitives `box`,
styled `text` and `layer` (with `rect`, `circle`, `line` and `label`), wrapped in a `fn`.

```bib
fn bar(label, value, top, tone: accent) {
  box(dir: "row", gap: 12, align: center) {
    box(width: 80) { text(label, size: sm, color: muted) }
    box(width: fill, height: 12, radius: 6, background: fade(tone, 0.14)) {
      layer { rect(0, 0, w * value / top, h, fill: tone, radius: 6) }
    }
    box(width: 40) { text(str(value), size: sm, face: mono) }
  }
}

fn pill(label, tone: muted) {
  box(padding: [2, 8], radius: 9999, background: fade(tone, 0.16)) { text(label, size: sm, color: tone) }
}

let top = max(data.sales.map(s => s.value))
card("Units by region", subtitle: "A bar chart from box and layer") {
  for s in data.sales { bar(s.label, s.value, top, tone: s.value == top ? success : accent) }
}

box(dir: "row", gap: 8) { pill("custom"); pill("pill", tone: accent); pill("widget", tone: success) }

box(width: fill, height: 120, radius: 8, background: raised, border: 1, borderColor: border) {
  layer {
    line(0, h / 2, w, h / 2, color: faint, width: 1)
    circle(w / 4, h / 2, 30, fill: fade(accent, 0.3), color: accent, width: 2)
    rect(w / 2 - 30, h / 2 - 30, 60, 60, fill: fade(success, 0.3), radius: 8)
    circle(w * 3 / 4, h / 2, 30, color: danger, width: 3)
    label(w / 2, h - 12, "rect, circle, line and label in a layer", size: sm, color: muted, align: center)
  }
}
```
