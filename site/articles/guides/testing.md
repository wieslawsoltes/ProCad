---
title: "Testing"
---

# Testing

The repository uses xUnit across domain, rendering, editing, controls, collaboration, services, ViewModels, Trace CLI, and Avalonia headless smoke coverage.

## Main Test Projects

```bash
dotnet test ProCad.Tests/ProCad.Tests.csproj -c Debug -m:1
dotnet test ProCad.Editing.Tests/ProCad.Editing.Tests.csproj -c Debug -m:1
dotnet test ProCad.Controls.Tests/ProCad.Controls.Tests.csproj -c Debug -m:1
```

## Coverage Areas

- rendering entities, layouts, hatches, text, tables, dimensions, leaders, images, underlays, dynamic blocks, XRefs, plot styles, hidden lines, hit testing, caches, diagnostics, and snapshots
- editing commands, command runtime, command descriptors, prompt state, script execution, undo/redo, constraints, snaps, tracking, grips, clipboard, selection, and collaboration
- app services, command recording, selection synchronization, data format safety, document context, editor session/controller hosts, ViewModels, and UI smoke tests
- controls viewport math and Skia rendering
- Trace CLI option parsing and render harness behavior

## CI Notes

The CI workflow runs focused cross-platform tests and a macOS full solution build for workload-specific projects. Use recursive submodule checkout for all validation.
