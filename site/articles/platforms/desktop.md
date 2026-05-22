---
title: "Desktop"
---

# Desktop

`ProCad.Desktop` is the primary host for local CAD inspection and editing.

## Runtime

- Avalonia desktop lifetime
- Skia render backend
- Dock workspace
- file system dialogs
- system clipboard bridge
- file-backed collaboration snapshots
- notification manager

## Build

```bash
dotnet build ProCad.Desktop/ProCad.Desktop.csproj -c Release
```

## Publish

```bash
dotnet publish ProCad.Desktop/ProCad.Desktop.csproj -c Release -r osx-arm64 --self-contained true
dotnet publish ProCad.Desktop/ProCad.Desktop.csproj -c Release -r win-x64 --self-contained true
dotnet publish ProCad.Desktop/ProCad.Desktop.csproj -c Release -r linux-x64 --self-contained true
```

Publish runtime identifiers according to the operating systems you support.
