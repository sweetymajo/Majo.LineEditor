# Majo.LineEditor

A small, cross-platform interactive line editor for .NET.

`Majo.LineEditor` provides interactive line editing for console applications with a managed Win32 backend on Windows and a native linenoise-based backend on Linux.

## Installation

```bash
dotnet add package Majo.LineEditor
```

The package targets:

- .NET 8
- .NET 10

Supported runtime environments currently include:

- Windows
- Linux x64

The Linux native runtime is included in the package, so normal .NET builds and publishes do not require GCC or CMake.

## Features

- Single-line editing with a horizontal viewport
- Wrapped multi-line editing
- Command history with configurable capacity
- Unicode-aware cursor movement and display-width handling
- Home / End / Left / Right navigation
- Backspace and Delete editing
- Up / Down history navigation
- `Ctrl+C` interruption handling
- `Ctrl+D` end-of-input semantics
- Terminal resize handling
- Asynchronous reads with cancellation
- `WriteAbove(...)` for background output without losing the active input line

## Quick Start

```csharp
using Majo.LineEditor;

using var editor = new LineEditor(new LineEditorOption
{
    Prompt = "> ",
    CommandBufferSize = 4096,
    HistoryCount = 100,
    PollInterval = 20,
    MultiLine = false
});

ReadResult result = await editor.ReadLineAsync();
```

`ReadLineAsync()` returns a `ReadResult` whose status distinguishes normal acceptance, interruption, and end-of-input.

Background output can be written while an edit is in progress:

```csharp
editor.WriteAbove("[background] Connection established.");
```

`WriteAbove(...)` preserves the current edit state and redraws or relocates the active input as required by the current backend.

## Configuration

`LineEditorOption` controls the editor behavior:

| Option | Description |
| --- | --- |
| `Prompt` | Prompt displayed before the editable line. |
| `CommandBufferSize` | Maximum command size in **UTF-8 bytes**. |
| `HistoryCount` | Number of accepted non-empty commands retained in memory. `0` disables history. |
| `PollInterval` | Polling interval used while waiting for console input and cancellation/resize work. |
| `MultiLine` | `false` uses a single-line horizontal viewport; `true` enables wrapped multi-row editing. |

History belongs to the `LineEditor` instance and is kept in memory for that instance's lifetime.

## Read Semantics

| Status | Meaning |
| --- | --- |
| `Accepted` | The user pressed Enter and submitted the current line. |
| `Interrupted` | The user pressed `Ctrl+C`. |
| `EndOfInput` | The user requested end-of-input, such as `Ctrl+D` on an empty line. |

When `Ctrl+D` is pressed while the line is not empty, it behaves as forward-delete rather than immediately ending input.

Only one read operation may be active for an editor at a time.

## Interactive Console Requirement

`Majo.LineEditor` is intended for a real interactive console/TTY.

Redirected standard input or output is not a supported editing environment. The editor also enforces a single active line-editor instance for the process so the backends do not compete for ownership of the terminal state.
