---
title: "Desktop And Browser"
---

# Desktop And Browser

## Desktop Host

`ProCad.Desktop` is the classic Avalonia desktop entry point. It wires `ProCad` into a window lifetime and uses the same DI composition root, ReactiveUI routing, Dock layout, ProDataGrid models, AvaloniaEdit behavior, and Skia render backend as the shared app project.

```bash
dotnet run --project ProCad.Desktop/ProCad.Desktop.csproj -c Debug
```

The main workspace starts with:

- a document dock for open drawings
- a document tree
- inspector tools for properties, render options, layers, blocks, viewports, entity types, text styles, line types, and dimension styles
- semantic tools for command line, collaboration, logs, DXF, raw DXF, DWG, batch, scripting, and IO options

## Browser Host

`ProCad.Browser` targets `net10.0-browser` with Avalonia Browser support.

```bash
dotnet run --project ProCad.Browser/ProCad.Browser.csproj -c Debug
```

The browser host is useful for validating WebAssembly rendering and browser-friendly collaboration snapshot persistence. Browser security and storage constraints still apply.

## Shared App Model

Both hosts use `ProCad.App` to configure services. Keep UI logic in ViewModels, services, commands, and behaviors; the views remain passive XAML compositions.
