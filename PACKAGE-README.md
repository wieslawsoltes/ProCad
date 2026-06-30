# ProCad Controls

Reusable CAD viewing and editing controls for ProCad render scenes.

The control packages expose a small shared viewport layer and Skia-backed platform controls for Avalonia, Uno Platform, and .NET MAUI. They are intended for applications that already build or receive a `ProCad.Rendering.RenderScene` from DWG, DXF, or other document pipelines.

Packages:

- `ProCadSharp.Controls`: platform-neutral viewport, render options, and editor selection contracts.
- `ProCadSharp.Controls.Skia`: shared SkiaSharp renderer for `RenderScene` primitives.
- `ProCadSharp.Controls.Avalonia`: Avalonia viewer and editor controls.
- `ProCadSharp.Controls.Uno`: Uno Platform viewer and editor controls.
- `ProCadSharp.Controls.Maui`: .NET MAUI viewer and editor controls.
