---
title: "Rendering Pipeline"
---

# Rendering Pipeline

`ProCad.Rendering` transforms CAD documents into a `RenderScene` that can be displayed by desktop, browser, and control surfaces.

## Stages

1. Read a DWG/DXF document through `ICadDocumentService`.
2. Build render settings from document headers, layout selection, visual style, frame visibility, hidden-line options, plot style options, and render quality.
3. Resolve entity visibility, layer overrides, plot styles, line patterns, text shaping, SHX shapes, XRefs, dynamic block state, and materials.
4. Dispatch each entity to a focused render handler.
5. Emit render primitives with metadata, bounds, style data, and diagnostic records.
6. Cache expensive geometry/text output using render cache stamps.
7. Render primitives through Skia or a platform-specific backend.

## Entity Coverage

The pipeline includes handlers for lines, rays, xlines, points, arcs, circles, ellipses, splines, polylines, polygon meshes, polyface meshes, face3D, meshes, solids, hatches, wipeouts, raster images, underlays, viewports, shapes, text, MText, dimensions, leaders, multileaders, mlines, tables, inserts, OLE frames, proxy entities, modeler geometry, and fallback diagnostics.

## Visual Styles

Supported visual styles include:

- wireframe
- hidden line
- shaded

Hidden-line rendering uses depth/occlusion utilities, frame visibility options, and hidden dash settings. Shaded rendering uses material and lighting helpers for 3D-oriented primitives.

## Diagnostics And Stats

Rendering records unsupported entities, budget violations, primitive counts, bounds, cache behavior, and exportable render stats. Use these records with the log tool, render options, and Trace CLI when diagnosing real documents.
