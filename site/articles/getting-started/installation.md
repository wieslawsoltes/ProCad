---
title: "Installation"
---

# Installation

## Prerequisites

- .NET SDK 10.0.x
- Git submodules initialized recursively
- Optional workloads for full local coverage: `wasm-tools`, `android`, `ios`, `maccatalyst`, and `maui-android`

```bash
git submodule update --init --recursive
dotnet workload restore
dotnet restore ProCad.slnx
```

## Build

```bash
dotnet build ProCad.slnx -c Release --no-restore
```

For day-to-day development, use focused builds and tests when you do not need browser or MAUI workloads:

```bash
dotnet build ProCad.Desktop/ProCad.Desktop.csproj -c Debug
dotnet test ProCad.Tests/ProCad.Tests.csproj -c Debug -m:1
dotnet test ProCad.Editing.Tests/ProCad.Editing.Tests.csproj -c Debug -m:1
dotnet test ProCad.Controls.Tests/ProCad.Controls.Tests.csproj -c Debug -m:1
```

## Documentation Tooling

Lunet is pinned as a local .NET tool in `.config/dotnet-tools.json`.

```bash
dotnet tool restore
./build-docs.sh
./serve-docs.sh
./check-docs.sh
```

The generated site is written to `site/.lunet/build/www`.
