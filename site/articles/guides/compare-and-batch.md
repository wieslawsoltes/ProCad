---
title: "Compare And Batch"
---

# Compare And Batch

## Document Compare

The compare workflow uses document identity entries, object diffs, property diffs, and DXF text diffs.

Use compare when you need to answer:

- which objects were added or removed
- which properties changed
- whether rendered or semantic state differs between two document versions
- how ASCII DXF text differs at line level

## Batch Query

`CadBatchQueryParser` and `CadBatchQueryEngine` support structured searches over loaded documents.

Common query terms include:

- `type:` entity or object type
- `layer:` layer name
- `name:` named table/block/style item
- `handle:` CAD handle
- `doc:` document name
- free text terms for broad matching

## Export

Batch results can be exported through the batch export service. Formats include CSV and JSON models exposed from `ProCad.Core`.

Use batch export for:

- inventory reports
- layer/entity audits
- style usage checks
- regression evidence after renderer or parser changes
