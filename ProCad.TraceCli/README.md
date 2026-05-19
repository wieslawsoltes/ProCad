# ProCad.TraceCli

Headless CAD render harness for profiling and performance analysis.

## Run

```bash
dotnet run --project ProCad.TraceCli -- --input /path/to/file.dxf
```

Run multiple files in one pass:

```bash
dotnet run --project ProCad.TraceCli -- \
  --input /path/to/a.dxf \
  --input /path/to/b.dwg
```

## Common options

- `--width <px>` and `--height <px>`: output resolution (`1920x1080` default)
- `--warmup <n>`: warmup render iterations (`1` default)
- `--iterations <n>`: timed render iterations (`8` default)
- `--rebuild-each-iteration`: rebuild the render scene per iteration
- `--load-each-iteration`: reload document and rebuild scene per iteration
- `--visual-style wireframe|hiddenline|shaded`
- `--hidden-dash-continuity per-segment|continuous-path`
- `--no-image`: skip PNG output

## dotnet-trace

Use the built DLL directly to avoid `dotnet run` build overhead in traces:

```bash
dotnet build ProCad.TraceCli/ProCad.TraceCli.csproj -c Release

dotnet-trace collect \
  --output trace-output/sample.nettrace \
  -- \
  /usr/local/share/dotnet/dotnet \
  ProCad.TraceCli/bin/Release/net10.0/ProCad.TraceCli.dll \
  --warmup 1 \
  --iterations 20 \
  --no-image \
  --input /path/to/file.dxf

dotnet-trace report trace-output/sample.nettrace topN -n 25
dotnet-trace report trace-output/sample.nettrace topN -n 25 --inclusive
```
