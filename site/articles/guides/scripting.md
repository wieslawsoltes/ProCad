---
title: "Scripting"
---

# Scripting

`ProCad.Scripting` provides a Roslyn C# scripting host and command-script playback support.

## C# Scripts

Scripts run with controlled globals, imports, metadata references, cancellation, and output capture. Keep scripts focused on document automation and diagnostics.

Typical uses:

- query active document state
- run repeated command sequences
- export custom reports
- inspect render stats or metadata
- automate property edits

## Command Scripts

Command scripts execute command-line commands from a file. Blank lines and comments are skipped. Scripts can stop on first failure or continue based on options.

```text
LINE 0,0 100,0
CIRCLE 50,50 25
MOVE 10,0
```

## Recording

The app can record command execution and save command scripts for later replay. Recording is useful for tests, repros, and repeatable editing workflows.

## Safety

Script reference resolution is intentionally constrained. Add new references through the scripting host configuration rather than allowing arbitrary reflection or ambient application access.
