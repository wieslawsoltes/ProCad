---
title: "Architecture"
---

# Architecture

ProCad follows a strict layered shape:

| Layer | Projects | Responsibility |
| --- | --- | --- |
| UI | `ProCad`, `ProCad.Desktop`, `ProCad.Browser`, `ProCad.Controls.*` | XAML views, host lifetimes, platform controls, and rendering surfaces. |
| Presentation | `ProCad.ViewModels`, app services | Reactive state, commands, docking state, document orchestration, and user workflow coordination. |
| Domain and services | `ProCad.Core`, `ProCad.Rendering`, `ProCad.Editing`, `ProCad.Collaboration`, `ProCad.Scripting` | CAD rules, scene construction, editing operations, collaboration, scripting, validation, and diagnostics. |
| Infrastructure | `ProCad.IO`, platform services, transports, snapshot stores | File system, ACadSharp integration, clipboard, storage, browser persistence, and realtime transport wiring. |

## Composition Root

`ProCad.App` registers services with `Microsoft.Extensions.DependencyInjection`. Concrete services are wired once at startup. ViewModels consume abstractions or focused services rather than constructing dependencies directly.

## MVVM

Views are passive Avalonia XAML. User input is routed through bindings, commands, and behaviors. ViewModels inherit from `ReactiveObject` through the local base type and expose `ReactiveCommand` instances for actions.

## Navigation And Docking

The shell uses ReactiveUI routing with a single workspace route. The workspace uses Dock.Model.ReactiveUI to create a root dock with document, inspector, semantic, command, collaboration, and log tool docks.

## Rendering Boundaries

The render scene is platform-neutral. Skia and Avalonia-specific behavior lives in app/control layers, while entity handling, plot styles, hatches, hidden-line logic, hit testing, caching, and diagnostics stay in `ProCad.Rendering`.
