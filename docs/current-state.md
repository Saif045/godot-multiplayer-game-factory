# Current State

This is the compact source of current maturity and evidence. It is not a
replacement for architecture or protocol documentation.

## Proven foundation

- Steam listen-server transport, Godot `MultiplayerAPI`, `NetworkWorld`, and
  `PlayerLifecycle` have accepted two-account Hyper-V evidence.
- The reusable Netfox v1.35.3 `CharacterBody3D` player composition is
  acceptance-proven: split server-state/client-input authority, rollback,
  interpolation, bidirectional walking/jumping, and queued one-shot jump.
- The host-PC -> SSH/SCP -> GPU-P Hyper-V guest release and interactive-task
  path has verified build parity and cleanup behavior.

## Implemented, not yet acceptance-proven

- Server-authoritative interaction: a local E press requests a target by
  `NetworkObjectId`; the server validates and toggles a replicated switch.
- Deterministic per-owner player colors are presentation-only.
- The switch UID startup defect is fixed by `9ad446b`; local validation passed.

## Latest runtime evidence

The focused diagnostic build `gf_295f0fe3_9815b8fad1a3` connected successfully
on the two-account path: lobby membership, native Steam peer Connected, Godot
connection, and two-player lifecycle all passed. The peer mapping was correct:
host owner mapped to Godot peer `1`, and the guest mapped to its distinct local
peer.

The same run was terminal `FAIL` later at
`client_jump_visible_on_host`: client local jumps and host-side remote movement
were recorded, but no host `remote_jump_observed` event appeared within the
assertion window. The operator visually observed both players moving/jumping
and the shared light interaction working. This is evidence of a gameplay
observability/assertion discrepancy, not a Steam transport failure.

Evidence: `artifacts/ab_tests/transport_mapping_20260913_115400/result.json`.

## Transport history

Three earlier frozen interaction-build attempts on `gf_9ad446b1_30e79f95ef50`
reached lobby membership, peer creation, and MultiplayerAPI assignment, then
remained in native `Connecting` for 120 seconds. They were cleanly terminated.
The later successful diagnostic run means this is an intermittent native-peer
issue, not a currently reproducible invariant. Investigate it evidence-first
if it recurs; do not attribute it to Netfox or interaction.

## Recent commits

- `9ad446b` — valid UID for the interaction switch scene.
- `295f0fe` — transport-only Steam peer/mapping diagnostics.
- `d0ed7b2` — app bootstrap UID metadata and Maaack translations registration.

## Working tree

Clean after `d0ed7b2` before this documentation task.
