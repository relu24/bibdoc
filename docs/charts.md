# Charts: `plot` and `bars`

**Status:** built: the interpreter and bibmd draw all six calls. The epidemic showcase is rewritten with them (below).

## Why

bib has no chart. A document that plots draws one from primitives: a `box` with a `layer`, a
`line` per segment of every curve, a `line` and a `label` per tick. The twelve showcase
documents on bibmd.app (about 3,000 lines of bib) are mostly that:

- 548 primitive calls (`line` 143, `box` 126, `label` 122, `circle` 79, `layer` 53, `rect` 25)
  against 105 `stat`s, the most used widget;
- 24 `fn`s of their own, most of them axes, ticks and curves written again in each document;
- a curve costs about 50 steps a segment, so plots ate the 200,000-step budget: the epidemic
  page kept 20% headroom only by sampling every fourth day, gradient descent ran out until
  its grid shrank, Fourier stopped at 20 terms where its artifact has 40, and the tuning page
  moved a curve into its front matter;
- `w` and `h` cannot be mixed or compared, so plots fixed their height in px and scaled y by
  hand; log axes were each worked out again.

What they drew was the same few things: lines through computed points, an area under one,
dots, a horizontal or vertical rule with a note (a hospital's capacity, the day of the peak),
a marked point ("you are here"), and bars. The diagrams (an orbit, a light clock, a tree)
are each their own picture and stay in layers.

## `plot`

```bib
plot(x: [0, 300], y: [0, 1], height: 220, xLabel: "day", yLabel: "share of the city") {
  series("Susceptible", days, s, tone: accent)
  series("Infectious", days, i, tone: danger, fill: true)
  series("Recovered", days, r, tone: success)
  rule(x: peakDay, label: "peak, day {peakDay}")
  rule(y: 1 / r0, label: "S = 1/R₀", tone: muted)
  point(today, i[today], label: "today")
}
```

- **`plot(x:, y:, height:, xLabel:, yLabel:, log:)`** is a container. `x` and `y` are the
  axes' ranges, `[min, max]`; left out, they fit what is drawn. `log: "x"`, `"y"` or `"xy"`.
  Ticks are chosen by the renderer (1, 2, 5 × 10ⁿ, about six to an axis) and written with
  `fmt`. `height` is px (220 by default); the width is the plot's room.
- **`series(label, xs, ys, tone:, fill:, width:)`** a line through the points (`xs[i]`,
  `ys[i]`); `fill: true` shades down to the x axis. Up to 2,000 points.
- **`dots(label, xs, ys, tone:, r:)`** points without a line (data, a sample).
- **`rule(x: | y:, label:, tone:)`** a line across the plot at that value, with a note.
- **`point(x, y, label:, tone:)`** one marked point with a note.

A plot with two or more labelled series draws its legend. A renderer may show the values
under the pointer; that is drawing, not state, so it stays out of the language.

**The budget.** Drawing costs no steps: a series is one call, however many points it has.
What the document computes for it still does, so a curve of 300 points costs its `map` and
nothing more, against some 15,000 steps drawn segment by segment today.

**The frame.** `{type: "plot", x, y, log, …, children: [{type: "series", label, xs, ys, …}]}`.
bibmd draws it on bibui's `DrawContext` (paths, text); the HTML renderer as SVG.

## `bars`

```bib
bars(data.sales, unit: "EUR")                           // [{label, value, tone?}]
bars(harmonics, dir: "column", max: 1, height: 160)     // a spectrum: bars standing up
```

**`bars(items, unit:, max:, dir:, height:)`** a bar per item of `{label, value, tone?}`,
the value written at its end with `fmt`. `dir: "row"` (the default) lays them across, as
the agent window's `bars` block does; `dir: "column"` stands them up under their labels.
`max` defaults to the largest value. It is not a container and returns nothing.

## What it replaces in the showcases

| Document | Today | With charts |
|---|---|---|
| Fourier | wave, overshoot curve (log x), spectrum | `plot` + `series`; `plot(log: "x")`; `bars(dir: "column")` |
| Epidemic | S/I/R, beds against capacity, final size | `plot` with three `series`, `fill`, `rule(y:)` |
| Base rates | positive predictive value against prevalence (log x) | `plot(log: "x")`, `point` |
| Relativity | γ against speed, survival against altitude | `plot`, `point` |
| Gradient descent | loss over steps (log y), the bumpy loss | `plot(log: "y")`; `plot` with two `point`s |
| Tuning | waveforms, roughness, harmonics | `plot` + `series`; `rule(x:)` per ratio |
| Voting | first choices, Borda points | `bars` |
| Orbits | speed over an orbit, phase angle over days | `plot`, `rule(y:)`, `point` |
| Evolution | genotype shares, selection, eight drifting populations, the race (log x y) | `plot` with `series` each |
| Sourdough | baker's percentages, the rise | `bars`; `plot` + `rule(y:)` + `point` |

The diagrams stay as layers: the orbits and the transfer ellipse, the light clock and
spacetime, the infection tree, the frequency tree, the circle of fifths, the voting graph,
the heat map, the crumb.

## The epidemic page, before and after

`bibmd/samples/explainers/03-epidemic.bib.md` drew three charts by hand; it now draws them
with `plot`. The infection tree stays a layer: it is a diagram.

| | By hand | With `plot` |
|---|---|---|
| Lines of bib | 241 | 178 |
| Lines in the three chart blocks | 155 | 108 |
| `fn`s of its own (frame, axes, curve) | 3 | 0 |
| Primitives in the chart blocks | 13 calls, most in loops over every point | 0 |
| Points a curve is drawn through | every 4th day (76) | every day (301) |
| Steps, as it opens | 140,855 | 125,705 |
| Steps, by hand at every day | 194,095 (97% of the budget) | |

The charts read the same, with their numbers inside the plot and a legend where the page
had a row of badges; the bed axis is written by `fmt` (12,000), where the page wrote its own
thousands. Two things the rewrite showed:

- **A band is missing.** The page shaded the days the measures last; with `plot` it marks
  where they start and end with `rule(x:)`. `band(x: [from, to], label:)` (and `y:`) is the
  next mark to add.
- **Drawing is no longer the cost; building lists is.** What is left of the 125,705 steps
  is almost all the simulation: `days.reduce((acc, d) => [...acc, next], …)` copies the list
  every day, some 45,000 elements copied for a 300-day run, three runs a frame. An append that does not
  copy, or a `scan(list, f, start)` that returns every step of a fold, would take most of it.

## Open

- **Heat maps.** Gradient descent drew a 28 × 16 grid of `rect`s. A `grid(xs, ys, values)`
  series would be the next chart; it is left out until a second document needs one.
- **Dates on an axis.** bib has no dates; a series over days uses day numbers. When dates
  arrive (`proposal.md`, number and date formatting), `x` takes them.
- **Clicking a point** to set state, as a slider does. Not until events (`two-layers.md`).
- **Colours past the six tones** for a ninth series. The tones, faded, have been enough.

## Steps

1. The interpreter: the five calls, their props and their frame, with conformance cases.
2. bibmd draws them; one showcase (the epidemic) is rewritten with them, and its step count
   and length are compared with today's.
3. The reference and the widgets page teach them; the remaining showcases follow.
