# ProCad Controls

Reusable CAD viewing and editing controls for ProCad render scenes.

The control packages expose a small shared viewport layer and Skia-backed platform controls for Avalonia, Uno Platform, and .NET MAUI. They are intended for applications that already build or receive a `ProCad.Rendering.RenderScene` from DWG, DXF, or other document pipelines.

Packages:

- `ProCad.Controls`: platform-neutral viewport, render options, and editor selection contracts.
- `ProCad.Controls.Skia`: shared SkiaSharp renderer for `RenderScene` primitives.
- `ProCad.Controls.Avalonia`: Avalonia viewer and editor controls.
- `ProCad.Controls.Uno`: Uno Platform viewer and editor controls.
- `ProCad.Controls.Maui`: .NET MAUI viewer and editor controls.
