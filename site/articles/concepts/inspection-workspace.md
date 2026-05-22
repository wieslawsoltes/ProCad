---
title: "Inspection Workspace"
---

# Inspection Workspace

The workspace is built for repeated inspection and editing, not a single static preview.

## Primary Areas

- Documents: open drawing views.
- Document Tree: hierarchical CAD object navigation.
- Properties: selected object metadata and editable property rows.
- Render Options: visual style, quality, visibility, frame, and diagnostics controls.
- Layers and Entity Types: filtering and inspection by document structure.
- Blocks and XRefs: block definitions, references, previews, and external reference workflows.
- Viewports: paper-space and viewport-specific inspection.
- Text, Line Type, and Dimension Style tools: style table inspection and editing surfaces.
- DXF, DXF Raw, and DWG tools: semantic and raw document views.
- Batch: multi-document query and export surface.
- Scripting: C# and command-script automation.
- Command Line: command execution, completions, and prompt state.
- Collaboration: share, join, reconnect, resync, presence, and session state.
- Log Output: app diagnostics and fast-path messages.

## Data Presentation

ProDataGrid-backed table models provide explicit columns, sorting, filtering, searching, and fast-path diagnostics. Column definitions live in ViewModel code where they can be tested and reused.

## Text Editing

AvaloniaEdit is used for code/text editing surfaces such as scripting and raw text-oriented views. Syntax and editor configuration are kept outside view code-behind.
