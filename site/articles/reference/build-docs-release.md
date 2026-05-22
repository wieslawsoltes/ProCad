---
title: "Build, Docs, And Release Pipeline"
---

# Build, Docs, And Release Pipeline

## Local Build

```bash
git submodule update --init --recursive
dotnet restore ProCad.slnx
dotnet build ProCad.slnx -c Release --no-restore
```

## Focused Test Gate

```bash
dotnet test ProCad.Tests/ProCad.Tests.csproj -c Debug -m:1
dotnet test ProCad.Editing.Tests/ProCad.Editing.Tests.csproj -c Debug -m:1
dotnet test ProCad.Controls.Tests/ProCad.Controls.Tests.csproj -c Debug -m:1
```

## Docs Pipeline

Docs use:

- `.config/dotnet-tools.json` to pin Lunet
- `site/config.scriban` for template, bundle, site metadata, and API docs
- `site/menu.yml` for top navigation
- `site/articles/**` for authored docs
- `site/articles/**/menu.yml` for sidebars
- `site/.lunet/css/template-main.css` for the precompiled template stylesheet
- `site/.lunet/css/site-overrides.css` for ProCad styling
- `site/.lunet/includes/_builtins/bundle.sbn-html` for the custom bundle include
- `site/.lunet/layouts/_default.api-dotnet*.sbn-md` for API page layout overrides

Commands:

```bash
./build-docs.sh
./check-docs.sh
./serve-docs.sh
```

Generated output goes to `site/.lunet/build/www`.

## GitHub Workflows

- `Build`: cross-platform focused build/test validation, macOS full-solution build, and reusable control package artifacts.
- `Docs`: Lunet build, generated output validation, and GitHub Pages deployment from `site/.lunet/build/www`.
- `Release`: tag/workflow-dispatch validation, package creation, optional NuGet publishing, and GitHub release creation.

## Release Tags

Push a tag named `v*` to create a release:

```bash
git tag v0.1.0-preview.1
git push origin v0.1.0-preview.1
```

NuGet publishing requires `NUGET_API_KEY`.
