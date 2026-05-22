---
title: "API Coverage Index"
---

# API Coverage Index

The generated API reference is built from the projects that provide reusable public surface without requiring MAUI or browser workloads in the docs job:

- `ProCad.Core`
- `ProCad.IO`
- `ProCad.Rendering`
- `ProCad.Editing`
- `ProCad.Collaboration`
- `ProCad.Scripting`
- `ProCad.Controls`
- `ProCad.Controls.Skia`
- `ProCad.Controls.Avalonia`
- `ProCad.Controls.Uno`

`ProCad.Controls.Maui`, `ProCad.Desktop`, `ProCad.Browser`, app-only UI assemblies, and test projects are covered through authored articles and repository source rather than generated API pages.

## External APIs

The Lunet config maps common external assemblies to their upstream documentation or project pages:

- Avalonia API docs
- SkiaSharp .NET API docs
- ReactiveUI API docs
- Dock project pages
- ACadSharp project pages

## Adding API Coverage

1. Confirm the project can build in the Ubuntu docs job without platform-only workloads.
2. Add it to `site/config.scriban` under `api.dotnet.projects`.
3. Add external API mappings for new public dependencies.
4. Run `./check-docs.sh`.
5. Verify generated pages under `site/.lunet/build/www/api`.
