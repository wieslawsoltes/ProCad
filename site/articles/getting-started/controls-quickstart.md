---
title: "Controls Quickstart"
---

# Controls Quickstart

The controls packages are for applications that already have or can build a `ProCad.Rendering.RenderScene`.

## Package Roles

- `ProCad.Controls`: platform-neutral viewport state, render options, point/size types, selection event contracts, and viewport math.
- `ProCad.Controls.Skia`: shared SkiaSharp renderer for `RenderScene` primitives.
- `ProCad.Controls.Avalonia`: Avalonia viewer/editor controls.
- `ProCad.Controls.Uno`: Uno Platform viewer/editor controls.
- `ProCad.Controls.Maui`: MAUI viewer/editor controls.

## Build A Render Scene

```csharp
using ProCad.Core;
using ProCad.IO;
using ProCad.Rendering;

ICadDocumentService documents = new AcAdSharpDocumentService();
CadReadOptions readOptions = CadReadOptions.Default;
CadRenderSceneSettings settings = new();
ICadRenderSceneBuilder sceneBuilder = new CadRenderSceneBuilder(
    settings,
    new RenderEntityDispatcher(/* registered handlers */));
```

In the full application, these services are registered in the DI composition root. Integrators should reuse the same pattern: keep IO, scene creation, cache invalidation, and ViewModel state outside the control.

## Avalonia Control

`CadViewerControl` and `CadEditorControl` display a render scene with viewport behavior. The control package is intentionally small; complex document orchestration belongs in your ViewModel or service layer.

Use the app project as the reference integration when you need:

- file dialogs and document lifetime
- render scene settings
- selection synchronization
- hit testing
- command and editor session routing
- batch/export/diagnostics integration

## Render Options

Common options include background, grid/axes visibility, fit behavior, pan/zoom, stroke scaling, and selection metadata. Keep render options in your ViewModel and bind them into the control.
