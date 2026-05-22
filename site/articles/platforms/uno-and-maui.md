---
title: "Uno And MAUI"
---

# Uno And MAUI

The Uno and MAUI packages bring the same render-scene model to additional app stacks.

## Uno

`ProCad.Controls.Uno` targets .NET 10 with Uno SDK and Skia renderer features. It exposes viewer/editor controls over the shared `ProCad.Controls` and `ProCad.Controls.Skia` layers.

```bash
dotnet build ProCad.Controls.Uno/ProCad.Controls.Uno.csproj -c Release
```

## MAUI

`ProCad.Controls.Maui` targets:

- `net10.0-android`
- `net10.0-ios`
- `net10.0-maccatalyst`

MAUI build and pack jobs should run on macOS when iOS and Mac Catalyst targets are involved.

```bash
dotnet workload restore ProCad.Controls.Maui/ProCad.Controls.Maui.csproj
dotnet build ProCad.Controls.Maui/ProCad.Controls.Maui.csproj -c Release
```

## Shared Guidance

Keep document services and render scene generation in shared app/domain code. The platform packages are view-layer adapters over the same scene contract.
