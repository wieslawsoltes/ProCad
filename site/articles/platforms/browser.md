---
title: "Browser"
---

# Browser

`ProCad.Browser` targets `net10.0-browser` and hosts the Avalonia app in WebAssembly.

## Runtime Differences

- storage goes through browser-friendly snapshot stores
- file access is browser constrained
- browser security rules apply to loaded assets and downloads
- render performance depends on WebAssembly and browser canvas behavior

## Build

```bash
dotnet workload install wasm-tools
dotnet build ProCad.Browser/ProCad.Browser.csproj -c Release
```

## Use Cases

The browser host is useful for:

- validating platform-neutral render scenes in WebAssembly
- testing browser collaboration persistence
- experimenting with web-delivered CAD inspection workflows
