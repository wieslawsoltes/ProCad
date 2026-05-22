---
title: "Overview"
---

# Overview

ProCad is organized as a layered CAD workspace:

- `ProCad.Core` defines document services, read/write options, file formats, metadata, validation, identity traversal, diffing, batch search, and export contracts.
- `ProCad.IO` maps those contracts onto ACadSharp for DWG and DXF load/save workflows.
- `ProCad.Rendering` turns CAD documents into a platform-neutral `RenderScene` made of primitives, bounds, styles, diagnostics, metadata, and hit-test data.
- `ProCad.Editing` owns command execution, interactive adapters, editor sessions, operation batches, transactions, undo/redo, clipboard, constraints, snaps, tracking, grips, and entity indexing.
- `ProCad.Collaboration` shares operation batches, presence, conflict state, snapshots, and transport events between participants.
- `ProCad.Scripting` runs Roslyn C# scripts and command scripts against ProCad workspace services.
- `ProCad.Controls*` packages expose reusable render-scene viewers/editors for Avalonia, Uno, and MAUI.
- `ProCad` and `ProCad.Desktop` provide the Avalonia app shell.
- `ProCad.Browser` hosts the same app model in WebAssembly.
- `ProCad.TraceCli` provides repeatable render profiling outside the UI.

## Main Workflows

1. Open a DWG or DXF document.
2. Inspect the document tree, properties, layers, blocks, viewports, entity types, style tables, and raw/semantic DXF or DWG data.
3. Render the drawing as wireframe, hidden line, or shaded scene with diagnostics and stats.
4. Edit geometry through command-line commands or interactive tools.
5. Compare documents, run batch queries, export results, or automate changes through scripts.
6. Share a session through the collaboration layer when a transport is configured.

## Current Scope

The repository is not only a control package. It contains a full CAD workspace, platform controls, domain services, and validation harnesses. The API reference documents the reusable library surface; the articles explain the app and integration scenarios.
