---
title: "Trace CLI"
---

# Trace CLI

`ProCad.TraceCli` is a headless render harness for profiling and performance analysis.

## Run One File

```bash
dotnet run --project ProCad.TraceCli -- --input /path/to/file.dxf
```

## Run Multiple Files

```bash
dotnet run --project ProCad.TraceCli -- \
  --input /path/to/a.dxf \
  --input /path/to/b.dwg
```

## Common Options

- `--width <px>` and `--height <px>` set output resolution.
- `--warmup <n>` controls warmup render iterations.
- `--iterations <n>` controls measured iterations.
- `--rebuild-each-iteration` rebuilds the render scene each iteration.
- `--load-each-iteration` reloads the document and rebuilds the scene each iteration.
- `--visual-style wireframe|hiddenline|shaded` selects render style.
- `--hidden-dash-continuity per-segment|continuous-path` selects hidden-line dash continuity.
- `--no-image` skips PNG output.

## Trace Collection

```bash
dotnet build ProCad.TraceCli/ProCad.TraceCli.csproj -c Release

dotnet-trace collect \
  --output trace-output/sample.nettrace \
  -- \
  dotnet \
  ProCad.TraceCli/bin/Release/net10.0/ProCad.TraceCli.dll \
  --warmup 1 \
  --iterations 20 \
  --no-image \
  --input /path/to/file.dxf

dotnet-trace report trace-output/sample.nettrace topN -n 25
```

Use this tool before and after render changes to validate real performance effects.
