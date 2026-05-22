---
title: "Collaboration And Scripting"
---

# Collaboration And Scripting

## ProCad.Collaboration

The collaboration package contains:

- operation sharing contracts
- realtime session coordination
- presence registry
- conflict and reapply state
- snapshot store abstractions
- in-memory, file, and browser snapshot stores
- ProEdit transport adapters
- UI-facing collaboration service abstractions

## ProCad.Collaboration.ServerHost

The server host contains the server-side hosting helpers for collaboration sessions. Use it when a shared realtime transport is required.

## ProCad.Scripting

The scripting package contains:

- C# script host options
- script globals
- controlled metadata/source reference resolution
- execution results
- command script host integration

Use scripting for repeatable workflows that need more control than command script playback alone.
