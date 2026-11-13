# Two layers: primitives, and a standard library written in bib

**Status:** designed, not built. Nothing here changes a document that works today: the
widgets in `proposal.md` keep their meaning, and become the standard library's.

## Why

A claude.ai artifact is HTML, CSS and JavaScript. bibui already covers what its HTML does:
boxes, text, inputs, rows and columns. bib's expressions, `let`, `fn`, `if` and `for` cover
most of what its JavaScript does. Two things are missing:

- **What CSS does.** A bib document cannot say how anything looks: spacing, size,
  alignment, borders, colours, type. Each renderer decides, widget by widget, so a document
  that needs a look the vocabulary lacks waits for a new widget.
- **Part of what JavaScript does.** State of its own (a counter, a wizard's step, a list
  that grows), events beyond a click (hover, press, keys, drag), and drawing.

Adding widgets one at a time never closes that gap. This chapter splits the language in
two instead:

1. **Primitives:** `box`, `text`, the inputs, `markdown` and drawing, with a fixed style
   vocabulary that maps onto bibui's box model, so the HTML renderer can map it onto CSS
   almost one to one. This is the layer renderers implement, and the only one.
2. **A standard library written in bib:** `card`, `steps`, `chips`, `grid` and the rest,
   as `fn`s in a prelude built from the primitives. Documents stay as short as today; a
   document that wants a different card writes its own.

The test of the design is the standard library itself: if `steps`, with its circles, the
lines between them and its phase labels, can be written in bib, the primitives are enough.

## Layer 1: primitives

### `box`

```bib
box(dir: row, gap: 8, padding: [6, 10], radius: 8, background: raised, border: 1) {
  text("Hello")
}
```

A box lays out its children along one axis and draws itself behind them. Every prop is
named; none is positional. A box without a block is an empty spacer of its size.

| Prop | Values | bibui | CSS |
|---|---|---|---|
| `dir` | `column` (default), `row` | `ChildLayoutAxis` Y / X | `flex-direction` |
| `gap` | px | `Gap` between children | `gap` |
| `padding` | px, `[v, h]` or `[top, right, bottom, left]` | `Style.Padding` (`EdgeInsets`) | `padding` |
| `width`, `height` | px; `fill`; `fit`; `text`; a share `"50%"` | `SizeX`/`SizeY`: `Pixels`, `ParentPct(1)`, `ChildrenSum`, `Text`, `ParentPct(f)` | `width`/`height`, `flex` |
| `align` | `start`, `center`, `end`: across the axis | `BeginCenterAcross`, spacers | `align-items` |
| `justify` | `start`, `center`, `end`, `between`: along the axis | `Spacer(Fill)` | `justify-content` |
| `wrap` | `true`: a row starts a new line when the next child does not fit | measured by the renderer | `flex-wrap` |
| `background` | a colour (below) | `Style.BackgroundColor` | `background` |
| `border` | px | `Style.BorderWidth` | `border-width` |
| `borderColor` | a colour | `Style.BorderColor` | `border-color` |
| `borderLeft` (and the other edges) | `[px, colour]` | `Style.BorderLeft` (`EdgeBorder`) | `border-left` |
| `radius` | px | `Style.CornerRadius` | `border-radius` |
| `opacity` | 0–1 | `Style.Opacity` | `opacity` |
| `shadow` | `none`, `low`, `high` (the theme's) | `Style.Shadow` | `box-shadow` |
| `hover` | a record of `background`, `borderColor` | `Style.HoverBackgroundColor`, `HoverBorderColor` | `:hover` |
| `at` | `[x, y]`: floats at that place in its parent, out of the flow | `FloatingX`/`FloatingY` | `position: absolute` |
| `scroll` | `true`: clips its content and scrolls along its axis | `AllowOverflow`, `Clip`, `ViewScroll` | `overflow: auto` |
| `class` | a named style (below) | — | — |
| `id` | a name: makes the box a thing that receives events (see *Events*) | `BoxFromId` | — |
| `component` | the standard library's name for what this box is (`"card"`) | — | `role`, a class name |

Order is paint order: a later sibling is drawn over an earlier one. There is no z-index.
`at` places a box inside its parent and nowhere else: it cannot leave the parent's
rectangle, and what is drawn outside a box's rectangle is clipped (see *Drawing*).

### `text`

```bib
text("From plan to first release", size: display, weight: bold)
```

| Prop | Values | bibui | CSS |
|---|---|---|---|
| `size` | `sm`, `md`, `lg`, `xl`, `2xl`, `display`, or px | `ComponentSize`, `FontSize.Px` | `font-size` |
| `weight` | `regular`, `bold`, `italic`, `boldItalic` | `FontWeight` | `font-weight`, `font-style` |
| `face` | `body`, `heading`, `display`, `mono` | `FontSlot` | `font-family` (the theme's) |
| `color` | a colour | text colour | `color` |
| `wrap` | `true` (default): breaks at the box's width; `false`: one line | the text layer's lines | `white-space` |
| `align` | `start`, `center`, `end` | — | `text-align` |
| `caps` | `true`: in capitals, spaced (an eyebrow) | — | `text-transform`, `letter-spacing` |
| `width` | as for `box`: `fill` takes the rest of a row, so a badge beside it stays at the end | the text's box `SizeX` | `flex: 1` |

`markdown(source, size:)` stays a primitive: prose is the one thing renderers draw
better than a document could describe.

### Inputs

`button`, `choice`, `slider`, `input`, `toggle`, `checklist` and selectable lists stay
primitives: they own focus, the keyboard and their own drawing, and they behave the same in
every renderer. They take `width` and, for buttons, `style: primary | ghost | danger`. A
document that wants a button that looks like nothing else draws a `box` with an `id`
and reads its events.

### Drawing

Two forms, both in logical pixels, both pure, and both inside the step budget.

```bib
// A picture of its own size:
draw(240, 120) {
  rect(0, 0, 240, 120, fill: raised, radius: 8)
  line(10, 100, 230, 20, color: accent, width: 2)
  circle(120, 60, 6, fill: warning)
  label(120, 82, "peak", size: sm, color: muted, align: center)
}

// A layer under a box's children, sized by the box: w and h are the box's laid-out size.
box(dir: row, gap: 10) {
  layer { line(12, 28, 12, h, color: border, width: 1.5) }
  …
}
```

Shapes: `rect(x, y, w, h)`, `circle(cx, cy, r)`, `line(x1, y1, x2, y2)`,
`path(points, closed:)`, `label(x, y, text, size:, color:, align:)`; each takes `fill`,
`color` (the stroke), `width` (the stroke's) and, for `rect`, `radius`.

**Everything drawn is clipped to its box.** A renderer repaints by boxes, and a pixel
drawn outside the box it belongs to is never repainted: it stays on the screen when the
page scrolls. bibmd drew a step's connector past its own row, and left exactly such
lines behind. In bib that cannot be written.

bibmd draws a `draw` or a `layer` with bibui's paths and paints (`DrawContext`,
`Paint`); the HTML renderer draws it as inline SVG.

## Colours and the theme

A colour is one of:

- a **role**: `text`, `muted`, `faint`, `surface`, `raised`, `border`, `accent`,
  `success`, `warning`, `danger`, resolved by the theme for light and dark alike;
- a role **faded**: `fade(accent, 0.15)`;
- a **hex** `"#D9A441"`, the same in light and dark. Allowed, and the renderer warns once
  per document, because it is what goes wrong first when the reader switches theme.

The front matter's `theme:` sets the roles and the scale for the whole document:

```yaml
theme:
  accent: "#5BB0C8"
  warning: "#D9A441"
  radius: 10          # the default corner, for the standard library
  density: compact    # roomy | normal | compact: scales gaps and padding
  fonts: { display: "Inter Display", mono: "iA Writer Mono S" }   # among the renderer's fonts
```

A theme can only name fonts the renderer has. It does not load them. A document reads its
theme as `theme` (`theme.radius`, `theme.accent`), as the standard library does.

### Named styles

```yaml
styles:
  pill: { dir: row, padding: [2, 7], radius: 5, background: raised }
```

`box(class: pill, background: fade(accent, 0.14))`: the class's props first, then the
box's own, which win. A class is a named set of props and nothing else: no selectors, no
cascade, no inheritance by where a box stands in the tree, and no class applies to a box
that does not name it.

## State

```bib
let step = state("wizard", 1)

if button("Next", disabled: step == 3).clicked { set("wizard", step + 1) }
if step == 1 { input("Name") }
if step == 2 { checklist("Consents", data.consents) }
if step == 3 { text("Thanks, {value("Name")}") }
```

- `state(name, initial)` is the value of a named state: `initial` until it is first set.
  Names are document-wide.
- `set(name, value)` changes it, and like the effects it runs **only in an event run**
  (below). In a drawing run it is an error.
- Inputs are state: an input's value is the state named by its key. `value(key)` reads it
  even when the input is not drawn in this frame. That settles the open question about
  inputs a wizard hides.
- State lives as long as the reader has the document open. A host may keep it per reader
  (a hosted reader would); bibmd forgets it on close.
- A host may be **contained**: nothing a document does reaches outside it. There, `call`,
  `send` and `respond` are errors, and a button's whole effect is the state it sets. A
  hosted reader that runs other people's documents is such a host. `Interpreter.ReachesOutside` finds those calls without running the
  document, so a host can refuse one when it is saved rather than when a reader clicks.

A frame is still a function of the data, the sources, the state and the pointer: drawing
the same of each twice gives the same frame.

## Events

A run is either a **drawing run** (what the frame shows) or an **event run**: one event,
the program run once more with that event visible, in which `set` and the effects may run.
Today the only event is a button's click. This adds:

```bib
let tile = box(id: "tile-{i}", padding: 12, radius: 8, background: raised) { text(v.name) }
if tile.clicked { set("picked", v.name) }
if tile.hovered { text("Click to pick {v.name}", size: sm, color: muted) }
```

A box with an `id` returns `{hovered, held, clicked, pressed, released, x, y}`:

- `hovered` and `held`, and the pointer's `x`, `y` in the box, are **readable in drawing
  runs**: they are input, like an input's value, and change nothing. They let a box look
  different under the pointer, a drag show where it is going.
- `pressed`, `clicked` and `released` are true only in the event run of that event.
- `key(name)` is true in the event run of that key (`key("ArrowRight")`, `key("Escape")`,
  `key("Ctrl+K")`), when the document has the keyboard and no input does. A renderer gives a
  document the keyboard while it is read, never while it is being edited.
- A drag is `held` with `x`, `y`, and `released` at the end: state changes on release,
  or in each event run of a move when the document says `drag: true` on the box.

## Time

A frame does not depend on the clock. There is no `setTimeout`, no `Date.now`, and no
`random`. What a document may have instead:

- **An explicit tick.** `tick: 1s` in the front matter gives a `tick` number that counts up
  once a second while the document is open. A frame is a function of `tick`, so a test can
  set it, and a reader who pauses the document stops it. Nothing else gives a document time.
- **Motion that is only presentation.** `box(animate: appear | pulse, spring: snappy)`
  is drawn with bibui's `Motion` springs; it changes how a frame arrives, never what it holds.
- **The page's width.** `page.width` is readable, so a layout can change with the window
  (what media queries do), and it is as much input as the pointer.

## Layer 2: the standard library

The prelude is a bib file every renderer runs before a document's own blocks. Its `fn`s are
ordinary: a document may call them, and define its own beside them. Two small additions to
`fn` make it possible:

- **Named parameters with defaults**: `fn card(title, subtitle: null, tone: neutral) { … }`.
- **A block for the caller.** `slot()` draws the block the caller passed, in the caller's
  scope; `slot(x)` draws it with `it` set to `x`, so a container can draw a block per item:

```bib
card("Orders") { text("drawn where the card says slot()") }
steps(items) { chips(it.refs) }      // it is the step being drawn
```

The standard library as it would be written:

```bib
fn card(title, subtitle: null, tone: neutral, badge: null, badgeTone: null, footer: null) {
  box(component: "card", padding: 14, radius: theme.radius, border: 1,
      borderColor: tone == neutral ? border : tone, background: fade(raised, 0.35), gap: 6) {
    box(dir: row, align: start, gap: 8) {
      text(title, weight: bold, width: fill)
      if badge { pill(badge, tone: badgeTone ?? tone) }
    }
    if subtitle { text(subtitle, size: sm, weight: italic, color: muted) }
    slot()
    if footer {
      box(height: 1, background: border)
      markdown(footer, size: sm)
    }
  }
}

fn pill(label, tone: neutral) {
  box(component: "pill", padding: [4, 12], radius: 9999,
      background: tone == neutral ? raised : fade(tone, 0.16)) {
    text(label, size: sm, color: tone == neutral ? text : tone)
  }
}

fn chips(items, tone: null) {
  box(component: "chips", dir: row, wrap: true, gap: 6) {
    for c in items {
      box(padding: [2, 7], radius: 5, background: fade(tone ?? "#B8A4F0", 0.14)) {
        text(c, size: sm, face: mono, color: tone ?? "#B8A4F0")
      }
    }
  }
}

fn grid(columns: 3, gap: 12) {
  // the caller's block is drawn once; the renderer lays its children out in rows of `columns`
  box(component: "grid", dir: row, wrap: true, gap: gap, columns: columns) { slot() }
}

fn steps(items) {
  let phase = null
  box(component: "steps") {
    for s in items {
      let i = index
      if s.phase && s.phase != phase {
        text(s.phase, size: sm, face: mono, caps: true, color: accent)
        phase = s.phase
      }
      let last = i == items.length - 1 || items[i + 1].phase != s.phase
      let ring = s.tone ?? muted
      box(dir: row, gap: 10, padding: [0, 0, last ? 0 : 12, 0]) {
        // the line to the next step: under the circle, inside this row, to its bottom edge
        if !last { layer { line(12, 28, 12, h, color: border, width: 1.5) } }
        box(width: 24, height: 24, radius: 12, border: 1.5, borderColor: ring,
            background: fade(ring, 0.12), align: center, justify: center) {
          text(str(s.n), size: sm, weight: bold, color: ring)
        }
        box(width: fill, gap: 4) {
          box(dir: row, align: start, gap: 8) {
            text(s.title, weight: bold, width: fill)
            if s.status { pill(s.status, tone: ring) }
          }
          if s.who { text(s.who, size: sm, color: muted) }
          slot(s)
        }
      }
    }
  }
}
```

`grid` needs one prop beyond the table above, `columns:`, which makes a wrapping row give
each child an equal share of the line. That is the only layout bibmd's grid does that the
primitives would otherwise not express.

A launch checklist's workflow would then read:

```bib
steps(ph.steps.map(s => ({...s, tone: tone(s.kind), phase: ph.name}))) {
  if it.refs { chips(it.refs) }
  if it.note { text(it.note) }
}
```

### What a renderer still knows

A renderer implements the primitives, and nothing of the standard library. The
frame tree a prelude component produces is boxes and text, with `component:` on the root
box, so a renderer can still give it its meaning (a role for a screen reader, a class name
in HTML) without drawing it.

## What stays out

Kept out on purpose, each because it breaks one of the language's promises: every
document safe to open, every frame reproducible, both renderers in step, and short
documents a model writes reliably.

- **Selectors and the cascade.** No styles that apply by where a box stands, no inheritance
  of looks down the tree, no `!important`. A box looks as its own props and its named
  class say, and nothing else.
- **Arbitrary CSS.** No style strings, no `calc()`, no `url()`, no web fonts, no CSS
  variables beyond the theme's roles.
- **Timers without an explicit tick.** No `setTimeout`, `setInterval`, `requestAnimationFrame`
  or clock reads. Time is `tick`, declared in the front matter, or nothing.
- **Randomness without a seed.** No `random()`. A document that needs variety takes it
  from `data`.
- **Unbounded work.** No `while`, no recursion, and the step budget per run stays.
- **Reaching out.** No `fetch`, no URLs, no images from the network. Live data comes only
  through the front matter's declared `sources`, fetched by the host as the reader.
- **The host's internals.** No DOM, no window, no event listeners of its own, no reading
  pixels back, no `eval`, no other scripts.
- **Drawing outside one's box.** Clipped, always (see *Drawing*).
- **z-index.** Paint order is build order.

## Steps

1. **`box`, `text` and the style table in bibdoc**: the interpreter emits them as frame
   nodes like any widget; conformance cases recorded for them, as for the rest.
2. **`fn` with named defaults and `slot()`**: interpreter only; conformance cases.
3. **bibmd draws `box` and `text`** from the style table (bibui's box model); the HTML
   renderer, when it exists, maps the same table onto CSS.
4. **`layer` and `draw`**, clipped to the box.
5. **The prelude**: `card`, `pill`, `chips`, `grid`, `steps` written in bib. Its first test:
   a launch checklist, drawn through the prelude, matches the one drawn by today's
   built-in widgets.
6. **State and events**: `state`, `set`, `value` (built, with contained hosts), box events,
   `key`, event runs beyond clicks.
7. **Theme and named styles**, then `tick` and motion.
8. **Retire the built-ins** that the prelude now writes, once every renderer draws the
   prelude's frames. Until then both exist, and the built-in wins where both are defined.
9. **Measure**: the benchmark's tasks rerun with the prelude in place of the built-ins,
   and a new set that needs the primitives (a tile picker, a wizard, a small chart), for how
   reliably models write documents with more freedom.

1 and 2 first; nothing draws until the cases pass. Step 5 is the proof of the design.

## Open

- **How much freedom models handle.** The benchmark rewarded a closed vocabulary. The
  prelude keeps the common case closed, and step 9 measures the rest. If free styling makes
  documents less reliable, the answer may be fewer props, not more.
- **Hex colours.** Allowed, with a warning, or not at all?
- **Where the prelude lives.** In bibdoc, versioned with the language, and loaded by every
  renderer; or per document, by an include.
- **Keyboard ownership** in a host that is also an editor (bibmd): which keys a locked
  document gets, and how a reader takes them back.
