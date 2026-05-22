---
title: "Avalonia Controls"
---

# Avalonia Controls

`ProCad.Controls.Avalonia` exposes Avalonia viewer/editor controls backed by the shared viewport and Skia renderer.

## Responsibilities

The controls should:

- display render scenes
- manage viewport state
- expose selection changes
- support pan, zoom, fit, grid, axes, and render options

The application should keep:

- document loading
- render scene building
- editing sessions
- command routing
- persistence
- dialogs
- collaboration state

outside the control.

## Integration Pattern

Use MVVM bindings for scene/options/selection. Avoid code-behind event handlers. For richer interaction, route input through behavior or command services as the app does.
