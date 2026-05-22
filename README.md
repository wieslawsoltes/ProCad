# ProCad

[![Build](https://github.com/wieslawsoltes/ProCad/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ProCad/actions/workflows/build.yml)
[![Docs](https://github.com/wieslawsoltes/ProCad/actions/workflows/docs.yml/badge.svg)](https://github.com/wieslawsoltes/ProCad/actions/workflows/docs.yml)
[![Release](https://github.com/wieslawsoltes/ProCad/actions/workflows/release.yml/badge.svg)](https://github.com/wieslawsoltes/ProCad/actions/workflows/release.yml)
[![Docs Site](https://img.shields.io/badge/docs-lunet-0b7285.svg)](https://wieslawsoltes.github.io/ProCad/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

ProCad is a .NET 10 CAD inspection, rendering, editing, scripting, and collaboration workspace for DWG and DXF documents. It combines an Avalonia desktop/browser application with reusable CAD viewer/editor controls, an ACadSharp-backed IO layer, a render-scene pipeline, command-based editing services, and a headless trace harness for performance work.

## Highlights

- Open, inspect, compare, edit, and save DWG/DXF documents.
- Render model space, paper space, layouts, viewports, plot styles, linetypes, hatches, raster images, underlays, dynamic blocks, XRefs, SHX text, ACIS SAT/SAB geometry, and common 2D/3D entity families.
- Use a Dock-based Avalonia workspace with document tree, property grid, layers, blocks, viewports, entity type filters, style editors, render options, DXF/DWG semantic views, raw DXF, batch tools, scripting, collaboration, diagnostics, and logs.
- Drive edits through command-line and interactive tools for draw, modify, annotation, clipboard, XRef, constraints, undo/redo, and script workflows.
- Embed reusable controls through `ProCad.Controls`, `ProCad.Controls.Skia`, `ProCad.Controls.Avalonia`, `ProCad.Controls.Uno`, and `ProCad.Controls.Maui`.
- Run render profiling and trace capture through `ProCad.TraceCli`.
- Build narrative and generated API documentation with Lunet.

## Repository Layout

| Path | Purpose |
| --- | --- |
| `ProCad` | Avalonia application shell, views, view models, services, docking layout, commands, diagnostics, and render backend integration. |
| `ProCad.Desktop` | Classic desktop host. |
| `ProCad.Browser` | Avalonia WebAssembly/browser host. |
| `ProCad.Core` | CAD domain contracts, metadata, diffing, batch query/search, IO options, and export models. |
| `ProCad.IO` | ACadSharp-backed DWG/DXF load/save services. |
| `ProCad.Rendering` | Document-to-render-scene pipeline, entity handlers, hit testing, caching, diagnostics, and performance budgets. |
| `ProCad.Editing` | CAD commands, sessions, operations, transactions, undo/redo, selection, clipboard, constraints, snaps, grips, and command runtime. |
| `ProCad.Collaboration` | Realtime operation sharing, presence, conflict handling, snapshot stores, and ProEdit transport adapters. |
| `ProCad.Scripting` | Roslyn-backed C# scripting and command script integration. |
| `ProCad.Controls*` | Platform-neutral, Skia, Avalonia, Uno, and MAUI reusable viewer/editor controls. |
| `ProCad.TraceCli` | Headless rendering and `dotnet-trace` harness. |
| `ProCad.*.Tests` | xUnit, rendering, editing, controls, collaboration, service, ViewModel, and Avalonia headless coverage. |
| `site` | Lunet documentation site and API docs configuration. |
| `.github/workflows` | Build, docs, and release automation. |

## Requirements

- .NET SDK 10.0.x
- Git submodules initialized recursively
- Optional workloads for full local coverage: `wasm-tools`, `android`, `ios`, `maccatalyst`, `maui-android`

```bash
git submodule update --init --recursive
dotnet workload restore
dotnet restore ProCad.slnx
```

## Build And Test

```bash
dotnet restore ProCad.slnx
dotnet build ProCad.slnx -c Release --no-restore
dotnet test ProCad.slnx -c Release --no-build
```

Focused test runs are useful during development:

```bash
dotnet test ProCad.Tests/ProCad.Tests.csproj -c Release
dotnet test ProCad.Editing.Tests/ProCad.Editing.Tests.csproj -c Release
dotnet test ProCad.Controls.Tests/ProCad.Controls.Tests.csproj -c Release
```

Run the desktop host:

```bash
dotnet run --project ProCad.Desktop/ProCad.Desktop.csproj -c Debug
```

Run the browser host:

```bash
dotnet run --project ProCad.Browser/ProCad.Browser.csproj -c Debug
```

## Packages

The reusable library and control packages are packable:

- `ProCad.Core`
- `ProCad.IO`
- `ProCad.Rendering`
- `ProCad.Editing`
- `ProCad.Scripting`
- `ProCad.Collaboration`
- `ProCad.Collaboration.ServerHost`
- `ProCad.Controls`
- `ProCad.Controls.Skia`
- `ProCad.Controls.Avalonia`
- `ProCad.Controls.Uno`
- `ProCad.Controls.Maui`

Create local packages:

```bash
dotnet pack ProCad.Core/ProCad.Core.csproj -c Release -o artifacts/packages
dotnet pack ProCad.IO/ProCad.IO.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Rendering/ProCad.Rendering.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Editing/ProCad.Editing.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Scripting/ProCad.Scripting.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Collaboration/ProCad.Collaboration.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Collaboration.ServerHost/ProCad.Collaboration.ServerHost.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Controls/ProCad.Controls.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Controls.Skia/ProCad.Controls.Skia.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Controls.Avalonia/ProCad.Controls.Avalonia.csproj -c Release -o artifacts/packages
dotnet pack ProCad.Controls.Uno/ProCad.Controls.Uno.csproj -c Release -o artifacts/packages
```

MAUI packages require platform workloads and are packed by the release workflow on macOS.

## Trace CLI

`ProCad.TraceCli` renders one or more files headlessly and can be used with `dotnet-trace`:

```bash
dotnet run --project ProCad.TraceCli -- --input /path/to/file.dxf --visual-style hiddenline
```

See [ProCad.TraceCli/README.md](ProCad.TraceCli/README.md) for trace collection and options.

## Documentation

The public documentation site is generated with Lunet from `site/`.

```bash
./build-docs.sh
./serve-docs.sh
./check-docs.sh
```

Generated output is written to `site/.lunet/build/www` and is published by the `Docs` workflow to GitHub Pages.

## Release

Tags named `v*` trigger the release workflow. The workflow validates the solution, packs the reusable controls, publishes NuGet packages when `NUGET_API_KEY` is configured, and creates a GitHub release with generated notes and package artifacts.

## License

This repository is licensed under the [MIT license](LICENSE).
