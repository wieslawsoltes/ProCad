---
title: "Controls"
---

# Controls

The controls packages let applications display and interact with ProCad render scenes.

| Package | Purpose |
| --- | --- |
| `ProCadSharp.Controls` | Shared viewport, geometry, options, selection contracts, and math. |
| `ProCadSharp.Controls.Skia` | SkiaSharp renderer for render scene primitives. |
| `ProCadSharp.Controls.Avalonia` | Avalonia viewer and editor controls. |
| `ProCadSharp.Controls.Uno` | Uno Platform viewer and editor controls. |
| `ProCadSharp.Controls.Maui` | MAUI viewer and editor controls. |

## Packaging Notes

The release workflow packs the reusable controls explicitly. MAUI packing is separated onto macOS because iOS and Mac Catalyst targets require Apple-platform workloads.

## Integration Rule

Use controls as render surfaces. Keep document loading, editing services, command runtime, collaboration, and persistence in ViewModels/services.
