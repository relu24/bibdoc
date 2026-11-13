---
name: bib-blocks
description: >
  Write and fix bib documents: Markdown files (.bib.md) with fenced `bib` blocks, small
  immediate-mode programs drawn live as sliders, choices, cards, stats, tables, lists,
  buttons, boxes and charts (bibmd draws them; the language is bibdoc). Trigger when:
  writing or editing a .bib.md file or a ```bib block, turning data or a plan into an
  interactive document or notebook, a bib block that fails or draws wrong, or questions
  about bib's widgets, primitives (box, text, layer), state, fn/slot or effects.
---

# Writing bib blocks

1. **Read `reference.md`** (beside this file) before writing: the language as the
   interpreter runs it, every widget's signature, and the mistakes that recur. Do not
   write from memory of JavaScript or of the `ui` JSON blocks: bib differs from both.
2. **Write the document.** Data goes in the front matter's `data:` (or `bib data`
   blocks); prose stays Markdown between the blocks; every effect a button can cause is
   listed in `effects:`.
3. **Check it:** `md FILE.bib.md --check`. It prints each error at its line in the file,
   warnings, and what each button's click would do. Fix and check again until it says
   `ok`. Read the click lines too: a button that "does nothing" or responds with the wrong
   fields is a bug the error count does not show.
4. **Look at it** when the layout matters: `md FILE.bib.md --png /tmp/out.png --size 1000x1400`
   draws it headless; read the PNG.

`md` is bibmd (0.1.1 or later; on Arch the `bibmd-bin` package from the `[bibmd]` repository).

## The rules that matter most

- All `bib` blocks of a file are one program, run every frame, top to bottom.
- A `let` inside `{ }` is local to it: declare before a container, assign inside.
- Lists never change: `xs = [...xs, x]`, never `push`.
- `list(…)` returns `{selected, count}`; `button(…)` returns `{clicked}`.
- `respond`, `send`, `call` and `set` only inside `if button("…").clicked { }`.
- Tones are bare names: `neutral muted success warning danger accent`. A widget's
  name is not a word: `dir: "row"`, `color: ink`, never `dir: row` or `color: text`.
- `kv` takes `[{label, value}]`. A literal brace in a string is `\{`.

## Examples

bibmd's `samples/vendor-choice.bib.md` (a decision with inputs and effects),
bibmd's `samples/sourdough.bib.md` (a long notebook with charts in layers),
bibdoc's `samples/steps-from-primitives.bib.md` (a widget rebuilt from primitives).
