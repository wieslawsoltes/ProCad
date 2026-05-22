---
title: "Command Editing"
---

# Command Editing

The command line and editor tools share the same command registry.

## Common Draw Commands

```text
LINE 0,0 100,0
PLINE 0,0 50,0 50,50 Close
CIRCLE 25,25 10
RECTANG 0,0 100,80
TEXT 10,10 2.5 0 Label
HATCH SOLID
```

## Common Modify Commands

```text
MOVE 10,0
COPY 0,10
ROTATE 45 0,0
SCALE 2 0,0
OFFSET 2 OUTER
TRIM boundaryHandle targetHandle END
FILLET 5 entity1 entity2
MATCHPROP sourceHandle targetHandle
```

## Clipboard Commands

```text
COPYCLIP
CUT
PASTECLIP 10,10
PASTEORIG
```

The clipboard service serializes entity dependency graphs so pasted entities can be remapped into the target document safely.

## Undo And Redo

Commands record operation batches through transaction and undo services. Undo units can merge by key and window when a tool creates repeated small changes.

```text
UNDO
REDO
```

## Interactive Tools

Interactive adapters are registered for the same command families. Pointer input becomes command tokens and previews rather than view-level event handlers.
