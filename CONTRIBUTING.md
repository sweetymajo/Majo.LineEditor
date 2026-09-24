# Contributing

Thank you for your interest in contributing to Majo.LineEditor.

Majo.LineEditor is a small cross-platform line-editing library with a managed Windows backend and a native POSIX backend. Contributions should keep the project focused and avoid adding unnecessary abstraction or platform complexity.

## Development Requirements

The project targets:

- .NET 8
- .NET 10

For normal managed development, no native toolchain is required.

Additional tools are only required when changing the POSIX native implementation:

- a Linux-compatible build environment;
- GCC;
- CMake 3.20 or newer.

On Windows, CLion with a WSL GCC toolchain can also be used for native development.

## Build

Restore and build the solution with:

```bash
dotnet restore Majo.LineEditor.slnx
dotnet build Majo.LineEditor.slnx
```

The POSIX native library is intentionally not rebuilt as part of the normal .NET build.

The repository already contains the runtime library used by normal builds:

```text
Majo.LineEditor/runtimes/linux-x64/native/libmajo_line_editor.so
```

Unless native source code has changed, no GCC or CMake invocation is required.

## Testing

`Majo.LineEditor.Test` is an interactive console test application rather than an automated unit test suite.

The behavior being tested depends heavily on a real terminal, including cursor movement, rendering, terminal resize behavior, `WriteAbove`, and platform-specific input handling.

Run a test mode with:

```bash
dotnet run --project Majo.LineEditor.Test/Majo.LineEditor.Test.csproj -- basic
```

Available test modes include:

```text
basic
write-above
cancellation
dispose
resize
resize-write-above
multiline
multiline-write-above
multiline-resize
multiline-resize-write-above
```

When changing terminal behavior, run the test modes relevant to the affected code.

Changes that affect platform-specific behavior should be tested on the affected platform whenever practical.

## Local NuGet Package Validation

`Majo.LineEditor.Test` can consume `Majo.LineEditor` in two different ways.

By default:

```xml
<UsePackageReference>false</UsePackageReference>
```

the test project uses a `ProjectReference`. This is the normal mode for day-to-day development because source changes are immediately visible to the test project.

When `UsePackageReference` is `true`, the test project instead consumes the locally packed NuGet package:

```text
ProjectReference
    → normal development

PackageReference
    → local NuGet package validation
```

The repository-level `NuGet.Config` maps `Majo.LineEditor` to the local package source under:

```text
artifacts/nuget
```

To validate the locally produced NuGet package without modifying the project file:

```bash
dotnet pack Majo.LineEditor/Majo.LineEditor.csproj -c Release

dotnet restore Majo.LineEditor.Test/Majo.LineEditor.Test.csproj \
    -p:UsePackageReference=true

dotnet build Majo.LineEditor.Test/Majo.LineEditor.Test.csproj \
    -c Release \
    --no-restore \
    -p:UsePackageReference=true
```

This validates the package as a real NuGet dependency rather than as a project reference.

If `UsePackageReference` is changed directly in the project file, restore the test project before building it. IDE builds may not automatically perform the required restore after this conditional dependency changes.

## POSIX Native Development

The POSIX backend uses a native shared library built from:

```text
Majo.LineEditor/Backends/Posix/Native/
```

The native project is written in C11 and built with CMake.

Only rebuild the native library when native source code changes.

From a Linux-compatible environment:

```bash
cd Majo.LineEditor/Backends/Posix/Native

cmake -S . -B cmake-build-release -DCMAKE_BUILD_TYPE=Release
cmake --build cmake-build-release
```

The CMake project automatically copies the resulting shared library to:

```text
Majo.LineEditor/runtimes/linux-x64/native/libmajo_line_editor.so
```

`libmajo_line_editor.so` is a committed runtime dependency, not a disposable build artifact.

If a contribution changes native behavior, the rebuilt `.so` should be committed together with the corresponding native source changes.

Normal managed-only changes should not rebuild or modify the native runtime asset.

## Vendored linenoise

The POSIX native backend contains a vendored copy of `linenoise`.

The upstream repository and exact pinned revision, together with all intentional local modifications, are documented in:

[LINENOISE_LOCAL_CHANGES.md](./Majo.LineEditor/Backends/Posix/Native/LINENOISE_LOCAL_CHANGES.md)

Project-specific behavior should normally be implemented in the managed POSIX backend or in `majo_line_editor.c`.

Direct changes to `linenoise.c` or `linenoise.h` should only be made when the required behavior cannot be implemented cleanly outside linenoise.

When a direct linenoise modification is necessary:

- keep the change as small and self-contained as practical;
- preserve the existing `local change N` source-marker convention;
- document the modification in `LINENOISE_LOCAL_CHANGES.md`;
- keep the vendored source as close to the pinned upstream revision as practical;
- review the upgrade and regression checklist in `LINENOISE_LOCAL_CHANGES.md`.

When updating the upstream linenoise revision, existing local changes should be re-evaluated rather than blindly reapplied.

## Pull Requests

Please keep pull requests focused on one logical change.

Before submitting a pull request:

- make sure the solution builds successfully;
- manually test behavior affected by the change;
- update documentation when public API or observable behavior changes;
- rebuild and commit the native runtime asset when native behavior changes;
- update `LINENOISE_LOCAL_CHANGES.md` when vendored linenoise is modified;
- explain the motivation and main changes in the pull request description.

The project intentionally prefers solving concrete terminal behavior over introducing abstractions for hypothetical future requirements.

## Code Style

Follow the existing code style and project structure where practical.

Prefer small, focused changes over unrelated cleanup or broad refactoring in the same pull request.
