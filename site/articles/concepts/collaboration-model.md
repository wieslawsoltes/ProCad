---
title: "Collaboration Model"
---

# Collaboration Model

`ProCad.Collaboration` shares CAD operation batches rather than opaque screen state.

## Session Data

Collaboration contracts include:

- operation kind, actor, session, Lamport/version metadata, and payload
- presence state
- selection, cursor, viewport, and tool-preview messages
- conflict and reapply results
- snapshot and oplog records

## Transport Layer

The collaboration layer depends on transport abstractions. The repository includes ProEdit transport adapters and a server host for WebSocket-style sessions.

## Snapshot Stores

Snapshot stores support:

- in-memory sessions for tests and transient use
- file-backed persistence for desktop
- browser persistence for WebAssembly
- legacy location migration
- corrupt payload recovery
- oplog compaction

## Conflict Policy

Remote operation batches are versioned. Stale windows can be registered as conflicts, then reapplied or cleared when the local state catches up. The goal is deterministic recovery with clear diagnostics rather than silent state divergence.
