# GameFactory Agent Guide

GameFactory is a small-session online co-op foundation. Shared gameplay is
server-authoritative by default. Build small, composable technical boundaries
that are justified by playable slices; do not build a second engine, a generic
command bus, transport abstraction, or a Netfox wrapper without multiple real
uses.

## Universal rules

- Keep reusable code in `factory/` and experiments in `sandbox/`.
- Preserve the distinction between Steam/account ID, transient Godot `PeerId`,
  session `PlayerId`, runtime `NetworkObjectId`, and represented-owner
  metadata (`NetworkObject.OwnerPeerId`). A peer ID is not durable identity.
- Clients do not authoritatively spawn players. Never map `OwnerPeerId`
  recursively to a player root's Godot authority.
- Prefer existing Godot and Netfox primitives. Compose normal nodes and focused
  components instead of concealing ownership behind a framework.
- Keep presentation-only data (including owner colors) out of rollback state.
- Preserve unrelated working-tree changes. Stage only task files, review the
  diff, and use focused commits. Push a commit when the user requests it.
- A normal feature task implements one coherent contract and validates it.
  Investigation/hardening starts only after an attempt is terminal, freezes a
  build, asks one narrow question, and changes one variable at a time.

## Task routing

Read this file, then only the nearest relevant scoped guide and any document it
explicitly routes to. Do not read every architecture document by default.

| Task | Read next |
|---|---|
| Current maturity, known blockers, recent evidence | `docs/current-state.md` |
| Network objects, player lifecycle, identity, authority | `factory/networking/AGENTS.md` |
| Netfox or rollback player work | `factory/networking/netfox/AGENTS.md`, then `docs/netfox-integration.md` |
| Gameplay interaction or replicated game state | `factory/gameplay/AGENTS.md` |
| Steam session, lobby, native peer, or adapter work | `factory/steam/AGENTS.md`, then `docs/steam-integration.md` |
| Host/VM A/B execution or runtime investigation | `tools/ab_test/AGENTS.md`, `docs/testing-protocol.md`, and `docs/runtime-test-operator.md` |
| Architecture/ownership boundary change | `docs/architecture.md`, relevant ADRs, and `docs/module-map.md` |
| Investigation after a terminal test | `docs/investigation-protocol.md` |

## Context budget

- Search before opening a large file. Read only the relevant section.
- Extract a narrow event timeline from logs with `rg`, `Select-String`,
  `Get-Content -Tail`, or a bounded time range; do not ingest whole artifacts.
- Compare a known-good and failing run by relevant event sequence, not by
  dumping complete JSONL or engine logs.
- Prefer `git diff <known-good>..HEAD -- <scope>` to rediscovering decisions
  already documented in the scoped guide.
- Do not investigate unrelated warnings or inspect outside a file scope unless
  an actual dependency requires it.

## Validation and runtime

Run the narrowest relevant validation and never describe an unrun check as
passing. For source changes, include an appropriate compile/test and
`git diff --check`; for exports, validate the actual manifest and dependencies.

Every Godot, Steam, VM, or process test must follow the routed test protocols:
define assertions/timeouts, preflight, clean state, dependency-ordered launch,
terminal PASS/FAIL/BLOCKED, preserved evidence, teardown, and verified host/VM
cleanup. A window opening is startup evidence, not success.

Diagnose bottom-up: export/dependencies -> Steam lobby -> native Steam peer ->
Godot connection -> GameFactory lifecycle -> Netfox -> gameplay. A failure at
an earlier layer is not evidence against a later layer; a harness failure can
also be an observability failure, so inspect structured evidence.
