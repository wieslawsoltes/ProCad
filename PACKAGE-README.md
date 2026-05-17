# ACadInspector Controls

Reusable CAD viewing and editing controls for ACadInspector render scenes.

The control packages expose a small shared viewport layer and Skia-backed platform controls for Avalonia, Uno Platform, and .NET MAUI. They are intended for applications that already build or receive an `ACadInspector.Rendering.RenderScene` from DWG, DXF, or other document pipelines.

Packages:

- `ACadInspector.Controls`: platform-neutral viewport, render options, and editor selection contracts.
- `ACadInspector.Controls.Skia`: shared SkiaSharp renderer for `RenderScene` primitives.
- `ACadInspector.Controls.Avalonia`: Avalonia viewer and editor controls.
- `ACadInspector.Controls.Uno`: Uno Platform viewer and editor controls.
- `ACadInspector.Controls.Maui`: .NET MAUI viewer and editor controls.
