---
title: "Feature Matrix"
---

# Feature Matrix

| Area | Current Surface |
| --- | --- |
| File IO | DWG/DXF load/save through ACadSharp-backed services; ASCII DXF text loading and diffing. |
| Rendering | Wireframe, hidden-line, shaded, paper space, layouts, viewports, layers, plot styles, linetypes, hatches, gradients, dynamic blocks, XRefs, images, underlays, OLE/proxy fallback, text/SHX, ACIS SAT/SAB helpers, and 3D mesh/modeler primitives. |
| Inspection | Document tree, property grid, layers, entity types, blocks/XRefs, viewports, text styles, line types, dimension styles, semantic DXF/DWG views, raw DXF, preview, diagnostics, and logs. |
| Editing | Draw, modify, annotation, XRef, clipboard, undo/redo, command scripts, script recording, snaps, tracking, grips, geometric/dimensional constraints, and transactions. |
| Compare | Document identity, object/property diffs, summary rows, side-by-side compare ViewModels, and DXF text diff. |
| Batch | Query parsing, multi-document search, status tracking, result rows, and CSV/JSON-style export models. |
| Scripting | Roslyn C# scripts, globals, controlled references/imports, execution results, command script host, and recording. |
| Collaboration | Operation batches, presence, transport adapters, conflict/reapply, session coordinator, snapshots, oplog recovery, file/browser/in-memory stores, and server host. |
| Controls | Platform-neutral viewport, Skia renderer, Avalonia, Uno, and MAUI viewer/editor controls. |
| Validation | xUnit rendering/editing/control/collaboration/service/ViewModel tests plus Avalonia headless smoke coverage and Trace CLI tests. |
