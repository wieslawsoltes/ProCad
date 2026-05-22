---
title: "Core And Rendering"
---

# Core And Rendering

## ProCad.Core

`ProCad.Core` contains:

- file formats and read/write options
- document service contracts
- property descriptors and validation
- generated CAD metadata registries
- document identity entries
- object and property diffing
- DXF text diffing
- batch query and search models
- export contracts and results

## ProCad.IO

`ProCad.IO` maps the core document service onto ACadSharp. It is the infrastructure boundary for DWG/DXF file access.

## ProCad.Rendering

`ProCad.Rendering` contains:

- `RenderScene` and render primitives
- entity dispatch and handlers
- render settings and style resolution
- layer, viewport, plot style, and frame visibility logic
- SHX/HarfBuzz text shaping helpers
- dynamic block, XRef, ACIS SAT/SAB, hidden-line, material, and lighting helpers
- hit testing, spatial index, diagnostics, stats, and cache services

These projects are the main reusable surface for non-UI CAD processing.
