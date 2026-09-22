<div align="center">

# Majo.LineEditor

**A small, cross-platform interactive line editor for .NET.**

Native console behavior on Windows, a linenoise-based POSIX backend on Linux, and a deliberately small managed API for command-line applications.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
![Windows](https://img.shields.io/badge/Windows-supported-0078D4?style=flat-square&logo=windows&logoColor=white)
![Linux x64](https://img.shields.io/badge/Linux-x64-FCC624?style=flat-square&logo=linux&logoColor=black)
![C11](https://img.shields.io/badge/native-C11-A8B9CC?style=flat-square&logo=c&logoColor=black)

**English** · [简体中文](./README.zh-CN.md)

</div>

---

## Overview

`Majo.LineEditor` provides interactive line editing for .NET console applications without forcing the rest of the application to depend on a platform-specific terminal stack.

The public API stays small while the implementation selects a native backend for the current platform:

- **Windows** uses a managed Win32 console backend.
- **Linux / POSIX** uses a native C wrapper around vendored `linenoise`.
- The Linux native library is checked into the repository as a runtime asset, so normal `.NET` builds and publishes do **not** require GCC or CMake.

This makes it possible, for example, to publish a `linux-x64` application directly from Windows and run the resulting output on Linux without rebuilding the native line-editor library.

## Highlights

- Single-line editing with a horizontal viewport
- Wrapped multi-line editing
- Command history with configurable capacity
- Unicode-aware cursor movement and display-width handling
- Home / End / Left / Right navigation
- Backspace and Delete editing
- Up / Down history navigation
- `Ctrl+C` reported as an interrupted read
- `Ctrl+D` end-of-input semantics
- Terminal resize handling
- Asynchronous reads with cancellation
- `WriteAbove(...)` for background output without losing the active input line
- Platform-specific rendering optimized independently for Windows and POSIX
- Prebuilt `linux-x64` native runtime asset
- No GCC/CMake invocation during normal `.NET` build or publish

## Quick Start

Reference `Majo.LineEditor` from your application and create one editor instance:

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

The editor exposes three meaningful read outcomes:

| Status | Meaning |
| --- | --- |
| `Accepted` | The user pressed Enter and submitted the current line. |
| `Interrupted` | The user pressed `Ctrl+C`. |
| `EndOfInput` | The user requested end-of-input, such as `Ctrl+D` on an empty line. |

When `Ctrl+D` is pressed while the line is not empty, it behaves as forward-delete rather than immediately ending input.

Only one read operation may be active for an editor at a time.

## `WriteAbove`

Interactive applications often need to print asynchronous status messages while the user is typing. Writing directly to `Console.Out` can damage the active prompt or leave stale input fragments on screen.

`WriteAbove(string content)` is designed for this case:

```text
[background] peer connected
> still typing here...
```

The implementation is backend-specific:

- **Windows** preserves the active input region and uses synchronized terminal output where available to avoid visible intermediate frames.
- **POSIX** temporarily hides and restores the linenoise edit state while preserving the logical line.

If `content` does not already end with a newline, `WriteAbove(...)` adds one.

## Platform Architecture

```text
Majo.LineEditor
│
├─ LineEditor
│  └─ small cross-platform public API
│
├─ Backends
│  ├─ Win32
│  │  ├─ managed line editor
│  │  └─ Win32 Console API interop
│  │
│  └─ Posix
│     ├─ managed backend
│     └─ Native
│        ├─ majo_line_editor.c
│        ├─ linenoise.c
│        ├─ linenoise.h
│        └─ CMakeLists.txt
│
└─ runtimes
   └─ linux-x64
      └─ native
         └─ libmajo_line_editor.so
```

### Windows backend

The Windows backend talks directly to the Win32 Console API. It owns editing, history navigation, cursor movement, rendering, display-width accounting, resize recovery, and background output behavior.

The managed Windows renderer uses the `Wcwidth` package for display-width handling.

### POSIX backend

The POSIX backend uses a small native wrapper around a vendored copy of `linenoise`.

The wrapper exposes only the operations required by the managed backend, including starting/stopping an edit session, polling input, feeding linenoise, history setup, resize synchronization, and `WriteAbove`.

The vendored linenoise revision and intentional local modifications are documented in:

```text
Backends/Posix/Native/LINENOISE_LOCAL_CHANGES.md
```

Keep direct changes to vendored linenoise small and documented.

## Native Library Workflow

The native POSIX library is intentionally **not** part of the normal `.NET` build pipeline.

The repository contains both:

```text
Backends/Posix/Native/
    native source and CMake project

runtimes/linux-x64/native/
    prebuilt libmajo_line_editor.so used by .NET builds
```

A normal build:

```bash
dotnet build
```

or a cross-platform publish from Windows:

```bash
dotnet publish -r linux-x64
```

does not invoke GCC, CMake, WSL, or a Linux VM. The checked-in native library is copied into the final application output.

### Rebuilding the POSIX native library

Rebuild the native library only when the native source changes.

Requirements:

- Linux-compatible environment
- GCC
- CMake 3.20 or newer

For example:

```bash
cd Majo.LineEditor/Backends/Posix/Native

cmake -S . -B cmake-build-release -DCMAKE_BUILD_TYPE=Release
cmake --build cmake-build-release
```

The CMake project builds `libmajo_line_editor.so` and copies the resulting library to:

```text
Majo.LineEditor/runtimes/linux-x64/native/libmajo_line_editor.so
```

Using CLion with a WSL GCC toolchain is also supported and is a convenient Windows development workflow.

> `Wcwidth` is part of the managed Windows backend. It is not compiled or linked into the POSIX native library.

## Runtime Asset Policy

`libmajo_line_editor.so` is a committed runtime dependency, not a transient build artifact.

That distinction is intentional:

```text
Native source
    ↓ explicitly rebuilt with GCC/CMake when changed
libmajo_line_editor.so
    ↓ committed under runtimes/linux-x64/native
normal .NET build / publish
    ↓
final application output
```

This keeps ordinary .NET development platform-independent while still allowing the POSIX implementation to use native code where it is useful.

## Interactive Console Requirement

`Majo.LineEditor` is intended for a real interactive console/TTY.

Redirected standard input or output is not a supported editing environment. The editor also enforces a single active line-editor instance for the process so the backends do not compete for ownership of the terminal state.

## Design Goals

The project deliberately favors a small surface area:

- keep the public API platform-neutral;
- keep platform-specific behavior inside its backend;
- use native APIs where they provide materially better terminal behavior;
- avoid forcing native toolchains into normal managed builds;
- keep vendored third-party modifications minimal and auditable;
- solve real terminal behavior first, and only add abstraction when it is needed.

---

<div align="center">

Built as a focused line-editing component for the Majo project family.

[简体中文](./README.zh-CN.md)

</div>
