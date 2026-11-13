---
width: wide
locked: true
data:
  steps:
    - n: 1
      title: "Write the plan"
      status: "v2 · 3 Mar"
      kind: "done"
      who: "Ana, with the team"
      phase: "Prepare"
      note: "One version, frozen before the review."
    - n: 2
      title: "Check the rules that apply"
      status: "Yes · 10 Mar"
      kind: "done"
      who: "The advisory office"
      phase: "Prepare"
      refs:
        - "Rule 4.2"
    - n: 3
      title: "Committee question answered"
      status: "Q-12"
      kind: "open"
      who: "The committee"
      phase: "Approve"
      note: "Confirm it is closed."
    - n: 4
      title: "Equipment approved"
      status: "open · EQ-77"
      kind: "watch"
      who: "Facilities"
      phase: "Approve"
    - n: 5
      title: "Collect, check, hand over"
      kind: "plan"
      who: "Team, partner"
      phase: "Run"
---
# Steps from primitives

The workflow below is drawn by `flow`, a `fn` written in bib from `box`, `text` and a `layer`. No steps widget. Under it, the same steps from the built-in `steps` widget, for comparison.

```bib
// The workflow's steps, written from primitives: no steps or step widget, only box, text and a layer.
let tone = k => k == "done" ? success : k == "watch" ? danger : k == "open" ? warning : muted

fn pill(label, tone: muted) {
  box(padding: [4, 12], radius: 9999, background: fade(tone, 0.16)) {
    text(label, size: sm, color: tone)
  }
}

fn chips(items, tone: null) {
  box(dir: "row", wrap: true, gap: 6) {
    for c in items {
      box(padding: [2, 7], radius: 5, background: fade(tone ?? accent, 0.14)) {
        text(c, size: sm, face: mono, color: tone ?? accent)
      }
    }
  }
}

fn flow(items) {
  let phase = null
  box(component: "steps") {
    for s in items {
      let i = index
      if s.phase && s.phase != phase {
        if i > 0 { box(height: 12) }
        text(s.phase, size: sm, face: mono, caps: true, color: accent)
        phase = s.phase
      }
      let last = i == items.length - 1 || items[i + 1].phase != s.phase
      let ring = tone(s.kind)
      box(dir: "row", gap: 10, padding: [0, 0, last ? 0 : 12, 0]) {
        if !last { layer { line(12, 28, 12, h, color: border, width: 1.5) } }
        box(width: 24, height: 24, radius: 12, border: 1.5, borderColor: ring,
            background: fade(ring, 0.12), align: center, justify: center) {
          text(str(s.n), size: sm, weight: bold, color: ring)
        }
        box(width: fill, gap: 4) {
          box(dir: "row", align: start, gap: 8) {
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

```bib
flow(data.steps) {
  if it.refs { chips(it.refs) }
  if it.note { text(it.note) }
}
```

## The same steps, from the built-in widget

```bib
steps {
  for s in data.steps {
    step(s.n, s.title, status: s.status, tone: tone(s.kind), who: s.who, phase: s.phase) {
      if s.refs { chips(s.refs) }
      if s.note { text(s.note) }
    }
  }
}
```
