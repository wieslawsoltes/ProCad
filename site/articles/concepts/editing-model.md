---
title: "Editing Model"
---

# Editing Model

`ProCad.Editing` models CAD editing as commands that produce operation batches against editor sessions.

## Command Runtime

The command registry resolves command names and aliases, parses command text, provides descriptors and completions, and executes handlers against the active editor session.

Command descriptors provide:

- summary text
- usage examples
- typed parameters
- keyword completions
- optional/default argument metadata

## Operation Batches

Command handlers produce create, update, transform, and delete operations. Batches are recorded through transactions and become the unit for undo/redo, collaboration, and scripting history.

## Interactive Adapters

Interactive adapters translate pointer and keyboard gestures into command tokens. They keep the command model and UI input model connected without pushing event handling into view code-behind.

## Editing Features

The editing layer includes:

- draw commands for line, polyline, xline, ray, circle, arc, ellipse, spline, polygon, rectangle, point, insert, hatch, and boundary
- modify commands for move, copy, rotate, scale, mirror, stretch, erase, offset, trim, extend, break, join, fillet, chamfer, array, explode, align, and match properties
- annotation commands for text, MText, linear/aligned/radius/diameter/angular dimensions, leader, and multileader
- clipboard commands with dependency graph serialization and DXF fallback payloads
- XRef reload, bind, and detach commands
- undo, redo, clear selection, script, script recording, script save, and help commands

## Precision Tools

Snap, tracking, and grip services provide nearest and mode-specific snap candidates, ortho/polar tracking, grip point discovery, and hot-grip resolution.
