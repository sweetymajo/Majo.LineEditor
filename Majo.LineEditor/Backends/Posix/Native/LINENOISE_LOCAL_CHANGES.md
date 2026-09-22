# Linenoise Local Changes

This project vendors **linenoise** as the POSIX line-editing backend used by `Majo.LineEditor`.

The purpose of this file is to record the exact upstream revision and every intentional local modification made to the vendored linenoise source. This should make future upstream upgrades easier to review, compare, and merge.

## Upstream Revision

- Repository: `https://github.com/antirez/linenoise`
- Commit: `a473823d74b93eab2ba83480df16ed37617493f2`
- Vendored files:
  - `linenoise.c`
  - `linenoise.h`

Except for the changes documented below, the vendored source should remain as close to this upstream revision as possible.

## Local Change Markers

Local modifications in the vendored source are marked with comments in the following form:

```c
// add start : local change N
...
// add end : local change N
```

The marker number corresponds to the numbered change in this document.

---

## Local Change 1: Support SS3 / Application Cursor Arrow Keys

### File

`linenoise.c`

### Function

```c
char *linenoiseEditFeed(struct linenoiseState *l)
```

### Background

The pinned upstream revision already handles cursor keys in the CSI form:

```text
ESC [ A    Up
ESC [ B    Down
ESC [ C    Right
ESC [ D    Left
```

It also handles Home and End in the SS3 / application-cursor form:

```text
ESC O H    Home
ESC O F    End
```

However, this upstream revision does not handle the corresponding SS3 arrow-key sequences:

```text
ESC O A    Up
ESC O B    Down
ESC O C    Right
ESC O D    Left
```

During testing with Windows Terminal connected to WSL, the terminal produced the following key sequences:

```text
Left     ESC O D
Right    ESC O C
Up       ESC O A
Down     ESC O B
Home     ESC O H
End      ESC O F
Delete   ESC [ 3 ~
Ctrl+D   0x04
Ctrl+C   0x03
```

Because `linenoiseEditFeed()` only recognized `ESC O H` and `ESC O F` in its upstream `ESC O` branch, Left, Right, Up, and Down were ignored in this environment.

### Modification

Extend the existing `ESC O` branch in `linenoiseEditFeed()` with these four cases:

```c
case 'A': /* Up */
    linenoiseEditHistoryNext(l, LINENOISE_HISTORY_PREV);
    break;

case 'B': /* Down */
    linenoiseEditHistoryNext(l, LINENOISE_HISTORY_NEXT);
    break;

case 'C': /* Right */
    linenoiseEditMoveRight(l);
    break;

case 'D': /* Left */
    linenoiseEditMoveLeft(l);
    break;
```

The existing `H` and `F` cases remain unchanged.

The resulting branch should conceptually contain:

```c
else if (seq[0] == 'O') {
    switch(seq[1]) {
    case 'A': /* Up */
        linenoiseEditHistoryNext(l, LINENOISE_HISTORY_PREV);
        break;
    case 'B': /* Down */
        linenoiseEditHistoryNext(l, LINENOISE_HISTORY_NEXT);
        break;
    case 'C': /* Right */
        linenoiseEditMoveRight(l);
        break;
    case 'D': /* Left */
        linenoiseEditMoveLeft(l);
        break;
    case 'H': /* Home */
        linenoiseEditMoveHome(l);
        break;
    case 'F': /* End */
        linenoiseEditMoveEnd(l);
        break;
    }
}
```

### Reason

This is a terminal-compatibility fix.

The affected terminals use SS3/application-cursor sequences for the arrow keys. The wrapper in `majo_line_editor.c` cannot cleanly implement this compatibility fix because the escape sequence is read and parsed internally by `linenoiseEditFeed()` itself.

Changing the wrapper would therefore require taking over stdin parsing or introducing an input-proxy mechanism, which would be substantially more invasive than this small parser extension.

### Scope

This patch intentionally changes only key-sequence recognition.

It does not change:

- line-editing semantics;
- history behavior;
- UTF-8 handling;
- Ctrl+C behavior;
- Ctrl+D behavior;
- Delete behavior;
- single-line rendering;
- multi-line rendering;
- resize behavior;
- the public linenoise API.

---

## Local Change 2: Dynamic Terminal Resize Recovery for the Multiplexed API

### Files

- `linenoise.c`
- `linenoise.h`

### Added API

The non-blocking / multiplexed API is extended with:

```c
int linenoiseEditResize(struct linenoiseState *l, int cols);
```

The declaration is added to `linenoise.h` next to `linenoiseEditStart()`, `linenoiseEditFeed()`, `linenoiseHide()`, and `linenoiseShow()`.

### Background

The pinned upstream revision determines terminal width when an edit session starts:

```c
l->cols = getColumns(stdin_fd, stdout_fd);
```

That value is then reused by the single-line and multi-line refresh code for the rest of the editing session. The multiplexed API does not provide a way to update the active `linenoiseState` after the terminal width changes.

This causes two different problems after a horizontal terminal resize:

- **single-line mode:** the terminal reflows only the viewport that is currently visible, while linenoise still owns the complete logical buffer and continues calculating horizontal trimming with the old `l->cols`;
- **multi-line mode:** `l->cols`, `l->oldrows`, and `l->oldrpos` continue to describe the previous physical layout, so the next refresh may clear or redraw from the wrong rows.

The resize support is intentionally implemented as a separate recovery path rather than modifying the normal `refreshSingleLine()`, `refreshMultiLine()`, or input fast paths.

### Modification Overview

`linenoise.c` adds three functions:

```c
static int resizeSingleLine(struct linenoiseState *l, size_t newcols);
static int resizeMultiLine(struct linenoiseState *l, size_t newcols);
int linenoiseEditResize(struct linenoiseState *l, int cols);
```

`linenoiseEditResize()` validates the supplied width, ignores unchanged widths, and dispatches according to the current `mlmode`:

```text
linenoiseEditResize
├─ single-line → resizeSingleLine
└─ multi-line  → resizeMultiLine
```

The caller supplies the currently observed terminal width. Resize detection and scheduling remain outside linenoise in the `Majo.LineEditor` POSIX wrapper.

### Single-Line Recovery

`resizeSingleLine()` reconstructs the viewport that linenoise last rendered using the **old** `l->cols`.

This is necessary because single-line mode may display only a horizontally trimmed portion of the complete logical buffer. For example, the logical buffer may be:

```text
0123456789
```

while the currently rendered viewport is only:

```text
12345678
```

A terminal resize can only reflow the characters that are physically present on screen; it cannot restore characters that linenoise previously trimmed out of the viewport.

The recovery therefore:

1. obtains the current rendered buffer through `linenoiseRenderBuffer()` so paste folding and logical cursor translation remain consistent with normal rendering;
2. reproduces the existing single-line horizontal trimming calculation using the old `l->cols`;
3. determines how the old viewport has been physically reflowed at `newcols`;
4. accounts for the terminal right-margin / pending-wrap boundary when locating the cursor row;
5. clears all physical rows occupied by the reflowed old viewport;
6. updates `l->cols` only after the old viewport has been removed;
7. calls `refreshSingleLine(l, REFRESH_WRITE)` to rebuild the viewport from the complete logical buffer at the new width.

The normal `refreshSingleLine()` implementation is deliberately left unchanged.

### Multi-Line Recovery

`resizeMultiLine()` operates on the complete rendered prompt and buffer rather than a horizontally trimmed viewport.

The recovery:

1. obtains the rendered buffer through `linenoiseRenderBuffer()`;
2. calculates prompt width, total rendered width, and logical cursor display offset;
3. calculates how many physical rows the old rendered block occupies after terminal reflow at `newcols`;
4. preserves the special right-margin behavior used by `refreshMultiLine()`, including the explicit trailing newline emitted when the cursor is at the end of the buffer exactly on the old right margin;
5. clears the complete reflowed old input block;
6. updates `l->cols`;
7. resets the stale multi-line geometry:

```c
l->oldrows = 0;
l->oldrpos = 1;
```

8. rebuilds the current edit area with:

```c
refreshMultiLine(l, REFRESH_WRITE);
```

The normal `refreshMultiLine()` implementation is deliberately left unchanged.

### Integration with `Majo.LineEditor`

Resize detection is intentionally kept outside linenoise.

The POSIX wrapper observes the current terminal width and records a pending resize. It does not continuously redraw while the user is dragging the terminal border.

The pending resize is applied through `linenoiseEditResize()` immediately before an operation that needs linenoise's terminal geometry to be valid, such as input processing or hiding the edit area for `WriteAbove`.

This separation is intentional:

```text
terminal resize detection
→ Majo.LineEditor wrapper

physical resize recovery / linenoise render-state repair
→ linenoiseEditResize()

normal input and refresh
→ existing linenoise paths
```

This keeps resize support removable without restructuring normal input handling.

### `WriteAbove` Interaction

`WriteAbove` can run independently of the input polling loop, so the wrapper must not assume that the polling loop has already detected the latest resize.

Before hiding the current edit area, the wrapper rechecks the terminal width and applies any pending resize. The hide operation is treated as resize-sensitive because a single-line viewport can be physically wrapped by the terminal if another resize occurs immediately before or during `linenoiseHide()`.

While the input area is hidden, there is no old edit region to recover. If the terminal width changes in that state, the wrapper synchronizes `state.cols` to the current width before `linenoiseShow()` rebuilds the edit area. Multi-line stale geometry is also reset before showing again when required.

These coordination rules live in `majo_line_editor.c`; they are not additional direct modifications to the vendored linenoise source.

### Performance and Accepted Resize Behavior

The resize patch does **not** modify the normal input rendering path. In particular:

- `linenoiseEditFeed()` remains on its existing path;
- `refreshSingleLine()` remains unchanged;
- `refreshMultiLine()` remains unchanged;
- the existing single-line direct-write fast path remains unchanged;
- resize recovery runs only when the wrapper has observed a pending terminal-width change.

Interactive POSIX testing after these changes shows no noticeable additional flicker in normal editing or `WriteAbove` operation.

Single-line resize recovery is intentionally not required to update continuously while the terminal border is being dragged. A width increase may therefore take a short time before the full logical viewport is restored. This is accepted behavior: occasional resize correctness is required, while immediate resize animation is not a performance goal.

### Scope

This change intentionally adds dynamic **horizontal terminal-width recovery** to active multiplexed editing sessions.

It does not attempt to:

- continuously animate redraws during terminal dragging;
- optimize resize latency beyond the current polling / pending-resize mechanism;
- change normal line-editing semantics;
- change history ownership or behavior;
- change Ctrl+C / Ctrl+D semantics;
- replace the existing UTF-8, grapheme-width, or paste-fold rendering logic;
- redesign `refreshSingleLine()` or `refreshMultiLine()`;
- provide a general terminal-resize event subsystem inside linenoise.

---

## Upgrade / Merge Checklist

When updating the vendored linenoise version:

1. Record the new upstream commit in this file.
2. Compare the new upstream `linenoise.c` and `linenoise.h` against the current vendored files.
3. Inspect every `local change N` marker and verify that the markers still surround code that is actually local to this project.

### Local Change 1

4. Inspect the `ESC O` / SS3 escape-sequence branch in `linenoiseEditFeed()`.
5. Check whether upstream now natively supports:
   - `ESC O A`
   - `ESC O B`
   - `ESC O C`
   - `ESC O D`
6. If upstream supports all four sequences, remove Local Change 1 instead of duplicating the behavior.
7. If upstream still does not support them, reapply the four SS3 cases.

### Local Change 2

8. Check whether upstream now provides dynamic resize support for an active multiplexed `linenoiseState`.
9. If upstream provides equivalent behavior, prefer the upstream implementation and remove Local Change 2 after verifying behavior.
10. Otherwise, reapply:
    - `linenoiseEditResize()` declaration in `linenoise.h`;
    - `resizeSingleLine()`;
    - `resizeMultiLine()`;
    - `linenoiseEditResize()` dispatch in `linenoise.c`.
11. Re-check any upstream changes to these internals before merging the resize patch:
    - `struct linenoiseState` fields related to columns and multi-line geometry;
    - `linenoiseRenderBuffer()`;
    - `refreshSingleLine()`;
    - `refreshMultiLine()`;
    - UTF-8 display-width helpers;
    - paste-fold rendering;
    - right-margin / trailing-newline behavior.
12. Re-check the POSIX wrapper integration around:
    - terminal-width detection;
    - pending resize application before input feed;
    - resize-aware hide/show around `WriteAbove`.

### Regression Tests

13. Re-run POSIX interactive tests for:
    - Left / Right;
    - Up / Down history navigation;
    - Home / End;
    - Delete;
    - Ctrl+C;
    - Ctrl+D;
    - UTF-8 / CJK / emoji editing;
    - paste folding;
    - single-line resize;
    - single-line resize + `WriteAbove`;
    - multi-line resize;
    - multi-line resize + `WriteAbove`.
14. Verify that normal POSIX editing still has no noticeable new flicker or performance regression.
15. Review this document and update it if any additional local linenoise changes are introduced.

---

## Policy for Future Local Changes

Prefer keeping `linenoise.c` and `linenoise.h` identical to upstream whenever the required behavior can be implemented cleanly in `Majo.LineEditor` itself.

Project-specific behavior should normally live in the POSIX wrapper or managed backend.

A direct linenoise modification should only be made when:

1. the required behavior cannot be implemented cleanly outside linenoise; and
2. the change can be kept small and well-defined.

When a direct modification is necessary, prefer a self-contained helper or API extension that leaves the normal input/render path untouched. This makes the patch easier to review, benchmark, disable, or remove during an upstream upgrade.

Every direct modification to the vendored linenoise source must be documented in this file and associated with a matching `local change N` source marker.
