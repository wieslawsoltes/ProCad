---
title: "Diagnostics And Performance"
---

# Diagnostics And Performance

## Render Diagnostics

Render diagnostics capture unsupported entities, fallback paths, budget violations, stats, primitive counts, and cache behavior. Use diagnostics when a document looks incomplete or rendering regresses.

## Fast Path Diagnostics

The app includes fast-path diagnostics for ProDataGrid and workspace performance surfaces. Clear diagnostics from the workspace when validating a fresh scenario.

## Render Stats Export

Render stats can be exported to JSON for comparison between commits or documents.

## Trace CLI

Use `ProCad.TraceCli` to isolate render performance from UI overhead:

```bash
dotnet run --project ProCad.TraceCli -- \
  --input /path/to/file.dxf \
  --visual-style hiddenline \
  --warmup 1 \
  --iterations 20 \
  --no-image
```

## Optimization Rules

For hot paths:

- prefer loops and pre-sized collections over LINQ
- avoid boxing and unnecessary allocations
- use spans, memory, pooling, and cache stamps where appropriate
- profile before and after changes
- keep diagnostics available so optimization does not hide correctness problems
