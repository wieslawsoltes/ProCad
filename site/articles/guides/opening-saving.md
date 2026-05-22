---
title: "Opening And Saving"
---

# Opening And Saving

`ProCad.IO` uses ACadSharp behind `ICadDocumentService` so the rest of the app works with a stable document service contract.

## Supported File Formats

- DWG
- DXF

Read and write behavior is configured through `CadReadOptions` and `CadWriteOptions`. The app exposes those settings through the IO Options tool.

## Open Workflow

1. The workspace asks `ICadFileDialogService` for one or more CAD files.
2. The selected format is mapped to read options.
3. `ICadDocumentService` loads the document.
4. A document ViewModel is created with render settings and scene builder services.
5. The document is registered with context, tree, selection, editor session, and dock services.

## Save Workflow

Saving uses the active document's path and format. Save As asks the file dialog service for a target path and format. Write options are built from the IO Options ViewModel.

## Raw DXF

The raw DXF view is useful for ASCII DXF inspection and text diff workflows. Binary DXF and DWG are not raw text formats; use semantic views and document diffing for those files.
