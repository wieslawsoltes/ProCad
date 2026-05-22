---
title: "Known Limits"
---

# Known Limits

- CAD file support depends on ACadSharp's DWG/DXF reader and writer coverage.
- Raw DXF text views apply to text DXF; DWG and binary DXF are not raw text workflows.
- Some entities are represented through fallback primitives or diagnostics when exact rendering is not implemented.
- Browser builds are constrained by WebAssembly performance, browser file APIs, browser storage, and web security rules.
- MAUI iOS and Mac Catalyst builds require macOS runners and platform workloads.
- Generated API docs intentionally exclude platform projects that would make the Ubuntu docs job depend on MAUI/browser workloads.
- Realtime collaboration depends on the configured transport and snapshot store; conflict recovery is explicit and diagnostic-oriented.
- Rendering performance should be validated with representative DWG/DXF files and `ProCad.TraceCli`, not only small synthetic samples.
